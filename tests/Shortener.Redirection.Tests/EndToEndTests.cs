extern alias Redirector;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shortener.LinkManagement.Tests;
using Xunit;
using RabbitTransport = Redirector::Shortener.Redirector.RabbitTransport;
using RabbitSettings = Redirector::Shortener.Redirector.RabbitSettings;

namespace Shortener.Redirection.Tests;

[Collection("redirection")]
[Trait("Category", "Integration")]
public sealed class EndToEndTests(LinkEnvironment environment)
{
    [Fact]
    public async Task Creation_proxy_two_redirectors_and_three_node_quorum_preserve_contract_during_failures()
    {
        await using var cluster = new QuorumCluster();
        await cluster.StartAsync();
        var settings = new RabbitSettings(cluster.Nodes[0].Hostname, cluster.Nodes[0].GetMappedPublicPort(5672),
            "test", "integration-only", "/", 4, 4L * 1024 * 1024 * 1024);
        await using (var topology = new RabbitHarness(settings))
        {
            await topology.StartAsync();
            await cluster.AssertMembersAsync(3);
        }
        for (var node = 2; node >= 0; node--) await cluster.CommandAsync(node, "stop_app");
        await using var database = await environment.CreateAsync();
        var captured = new ConcurrentQueue<string>();
        var destinationBuilder = WebApplication.CreateBuilder();
        destinationBuilder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var destination = destinationBuilder.Build();
        destination.Run(context =>
        {
            captured.Enqueue(context.Request.Path + context.Request.QueryString);
            return context.Response.WriteAsync("destination reached by test client");
        });
        await destination.StartAsync();
        var destinationUrl = destination.Urls.Single() + "/Case/segment?a=1&a=2#fragment";
        var (_, management) = await database.UserAsync();
        using var managementClient = management;
        using var creation = new HttpRequestMessage(HttpMethod.Post, "/api/v1/links")
        {
            Content = JsonContent.Create(new { destinationUrl })
        };
        creation.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var created = await managementClient.SendAsync(creation);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await created.Content.ReadFromJsonAsync<JsonElement>();
        var code = link.GetProperty("code").GetString()!;
        var id = link.GetProperty("id").ToString();

        Dictionary<string, string?> Settings(int node) => new(database.App.Settings)
        {
            ["Messaging:Host"] = cluster.Nodes[node].Hostname,
            ["Messaging:Port"] = cluster.Nodes[node].GetMappedPublicPort(5672).ToString(),
            ["Messaging:UserName"] = "test", ["Messaging:Password"] = "integration-only",
            ["Messaging:PoolSize"] = "4", ["TrustedProxies:0"] = "127.0.0.1", ["TrustedProxies:1"] = "::1"
        };
        using var first = new RedirectHost(Settings(0));
        using var second = new RedirectHost(Settings(1));
        first.UseKestrel(0);
        second.UseKestrel(0);
        using var firstClient = first.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var secondClient = second.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var firstPort = Port(first);
        var secondPort = Port(second);
        firstClient.BaseAddress = new Uri($"http://127.0.0.1:{firstPort}");
        secondClient.BaseAddress = new Uri($"http://127.0.0.1:{secondPort}");
        await TestcontainersSettings.ExposeHostPortsAsync([(ushort)firstPort, (ushort)secondPort]);
        var caddyfile = $$"""
            {
                auto_https off
            }
            :8080 {
                @internal path /health/*
                handle @internal {
                    respond 404
                }
                handle {
                    reverse_proxy host.testcontainers.internal:{{firstPort}} host.testcontainers.internal:{{secondPort}} {
                        lb_policy round_robin
                        header_down X-Test-Upstream {http.reverse_proxy.upstream.address}
                        header_down X-Test-Client-IP {http.request.remote.host}
                    }
                }
            }
            """;
        await using var proxy = new ContainerBuilder("caddy:2.10.2-alpine")
            .WithResourceMapping(Encoding.UTF8.GetBytes(caddyfile), "/etc/caddy/Caddyfile")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080)).Build();
        await proxy.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://{proxy.Hostname}:{proxy.GetMappedPublicPort(8080)}"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.254");
        using var metrics = new MetricProbe();
        var upstreams = new HashSet<string>();
        var sourceIps = new HashSet<string>();
        async Task RedirectAsync(string method = "GET")
        {
            var timer = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(new HttpMethod(method), "/" + code + "?ignored=short");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal(destinationUrl, response.Headers.Location!.OriginalString);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
            upstreams.Add(Assert.Single(response.Headers.GetValues("X-Test-Upstream")));
            var remote = IPAddress.Parse(Assert.Single(response.Headers.GetValues("X-Test-Client-IP")));
            sourceIps.Add((remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString());
            if (method == "HEAD") Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Console.WriteLine($"E2E real HTTP {method}: {timer.Elapsed.TotalMilliseconds:F3} ms");
        }
        // Both application instances start with all three broker applications unavailable.
        await RedirectAsync();
        await RedirectAsync();
        Assert.Equal(2, metrics.Sum("publications", "rejected"));
        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/health/ready")).StatusCode);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(node => cluster.CommandAsync(node, "start_app")));
        await RabbitEnvironment.WaitUntil(() => first.Services.GetRequiredService<RabbitTransport>().Ready
            && second.Services.GetRequiredService<RabbitTransport>().Ready);
        await cluster.AssertMembersAsync(3);
        await using var inspection = await RabbitEnvironment.Factory(settings).CreateConnectionAsync();
        await using var channel = await inspection.CreateChannelAsync();
        // Membership/connection readiness precedes Raft recovery; establish confirmed traffic before the ten-event sample.
        await AwaitConfirmedPairAsync();
        await channel.QueuePurgeAsync(RabbitTransport.Queue);
        upstreams.Clear();
        for (var i = 0; i < 10; i++) await RedirectAsync();
        Assert.Equal(2, upstreams.Count);
        await RedirectAsync("HEAD");
        Assert.Equal(10u, await channel.MessageCountAsync(RabbitTransport.Queue));
        var messages = new List<JsonElement>();
        for (var i = 0; i < 10; i++)
        {
            var delivery = await channel.BasicGetAsync(RabbitTransport.Queue, true);
            Assert.NotNull(delivery);
            Assert.True(delivery.BasicProperties.Persistent);
            messages.Add(JsonSerializer.Deserialize<JsonElement>(delivery.Body.Span));
        }
        Assert.Equal(10, messages.Select(item => item.GetProperty("eventId").GetString()).Distinct().Count());
        Assert.All(messages, item =>
        {
            Assert.Equal(id, item.GetProperty("linkId").GetString());
            Assert.Equal(Assert.Single(sourceIps), item.GetProperty("sourceIp").GetString());
            Assert.NotEqual("203.0.113.254", item.GetProperty("sourceIp").GetString());
            Assert.True(IPAddress.TryParse(item.GetProperty("sourceIp").GetString(), out _));
            Assert.EndsWith("Z", item.GetProperty("occurredAt").GetString());
        });
        Assert.Empty(captured);
        using (var followingClient = new HttpClient())
            Assert.Contains("destination reached", await followingClient.GetStringAsync(new Uri(client.BaseAddress, "/" + code)));
        Assert.Equal("/Case/segment?a=1&a=2", Assert.Single(captured));

        // Separate connection setup from the concurrent request scenario (cold startup is exercised above).
        var warmConnections = Enumerable.Range(0, 8).Select(_ => new NpgsqlConnection(database.Primary)).ToArray();
        try { await Task.WhenAll(warmConnections.Select(connection => connection.OpenAsync())); }
        finally { foreach (var connection in warmConnections) await connection.DisposeAsync(); }
        var publicationsBefore = metrics.Sum("publications");
        var activeConcurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.GetAsync("/" + code)));
        Assert.All(activeConcurrent, response =>
        {
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal(destinationUrl, response.Headers.Location!.OriginalString);
        });
        foreach (var response in activeConcurrent) response.Dispose();
        Assert.Equal(publicationsBefore + 8, metrics.Sum("publications"));

        async Task AwaitConfirmedPairAsync()
        {
            var recovery = Stopwatch.StartNew();
            while (recovery.Elapsed < TimeSpan.FromSeconds(20))
            {
                var before = metrics.Sum("publications", "confirmed");
                // Each request creates a NEW event: this observes recovery without retrying an uncertain event.
                await RedirectAsync();
                await RedirectAsync();
                if (metrics.Sum("publications", "confirmed") == before + 2) return;
                await Task.Delay(100);
            }
            Assert.Fail("Both redirectors did not regain confirmed publication within the recovery window.");
        }
        // A node loss leaves a real two-of-three majority; an election may temporarily exceed 100 ms.
        await cluster.CommandAsync(2, "stop_app");
        try
        {
            await cluster.AssertMembersAsync(2);
            await AwaitConfirmedPairAsync();
            await cluster.CommandAsync(1, "stop_app");
            try
            {
                var failuresBefore = metrics.Sum("publications", "unknown") + metrics.Sum("publications", "rejected");
                await RedirectAsync();
                await RedirectAsync();
                Assert.Equal(failuresBefore + 2, metrics.Sum("publications", "unknown") + metrics.Sum("publications", "rejected"));
                Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync("/health/ready")).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await secondClient.GetAsync("/health/ready")).StatusCode);
            }
            finally { await cluster.CommandAsync(1, "start_app"); }
        }
        finally { await cluster.CommandAsync(2, "start_app"); }
        await cluster.AssertMembersAsync(3);
        await RabbitEnvironment.WaitUntil(() => first.Services.GetRequiredService<RabbitTransport>().Ready
            && second.Services.GetRequiredService<RabbitTransport>().Ready);
        await AwaitConfirmedPairAsync();

        // Requests begun after the state transaction commits must see the new state on both instances.
        await database.SqlAsync("UPDATE links SET owner_disabled_at = now() WHERE code = $1", parameters: [code]);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => client.GetAsync("/" + code)));
        Assert.All(concurrent, response => Assert.Equal(HttpStatusCode.Gone, response.StatusCode));
        foreach (var response in concurrent) response.Dispose();
        await database.SqlAsync("UPDATE links SET owner_disabled_at = NULL WHERE code = $1", parameters: [code]);
        await RedirectAsync();
        // Control the outage from the maintenance database; PostgreSQL cannot disable its current database.
        var targetDatabase = new NpgsqlConnectionStringBuilder(database.Primary).Database!;
        await using var control = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.Primary)
            { Database = "postgres" }.ConnectionString);
        await control.OpenAsync();
        var databaseName = new NpgsqlCommandBuilder().QuoteIdentifier(targetDatabase);
        async Task Sql(string sql, string? target = null)
        {
            await using var command = new NpgsqlCommand(sql, control);
            if (target is not null) command.Parameters.AddWithValue(target);
            await command.ExecuteNonQueryAsync();
        }
        await Sql($"ALTER DATABASE {databaseName} ALLOW_CONNECTIONS false");
        try
        {
            await Sql("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = $1", targetDatabase);
            using var unavailable = await client.GetAsync("/" + code);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), unavailable.Headers.RetryAfter!.Delta);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await firstClient.GetAsync("/health/ready")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await secondClient.GetAsync("/health/ready")).StatusCode);
        }
        finally { await Sql($"ALTER DATABASE {databaseName} ALLOW_CONNECTIONS true"); }
        await RedirectAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Single(captured);
    }

    private static int Port(RedirectHost host) => new Uri(host.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
}

public sealed class QuorumCluster : IAsyncDisposable
{
    private readonly INetwork network = new NetworkBuilder().Build();
    public IContainer[] Nodes { get; }
    public QuorumCluster()
    {
        var cookie = Guid.NewGuid().ToString("N");
        Nodes = Enumerable.Range(0, 3).Select(index => new ContainerBuilder("rabbitmq:4.1.4-management")
            .WithHostname("mq" + index).WithNetwork(network).WithNetworkAliases("mq" + index)
            .WithEnvironment("RABBITMQ_NODENAME", "rabbit@mq" + index)
            .WithEnvironment("RABBITMQ_ERLANG_COOKIE", cookie)
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "test").WithEnvironment("RABBITMQ_DEFAULT_PASS", "integration-only")
            .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2")
            .WithPortBinding(5672, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672)
                .UntilCommandIsCompleted("rabbitmq-diagnostics", "-q", "ping")).Build()).ToArray();
    }
    public async Task StartAsync()
    {
        await network.CreateAsync();
        await Task.WhenAll(Nodes.Select(node => node.StartAsync()));
        for (var i = 1; i < Nodes.Length; i++)
        {
            await CommandAsync(i, "stop_app");
            await CommandAsync(i, "reset");
            await CommandAsync(i, "join_cluster", "rabbit@mq0");
            await CommandAsync(i, "start_app");
        }
    }
    public async Task CommandAsync(int node, params string[] arguments)
    {
        var result = await Nodes[node].ExecAsync(["rabbitmqctl", .. arguments]);
        Assert.True(result.ExitCode == 0, result.Stderr);
    }
    public async Task AssertMembersAsync(int online)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = await Nodes[0].ExecAsync(["rabbitmqctl", "list_queues", "name", "type", "members", "online", "--formatter", "json", "--timeout", "10"]);
            if (result.ExitCode == 0)
            {
                var queues = JsonSerializer.Deserialize<JsonElement>(result.Stdout);
                var queue = queues.EnumerateArray().Single(item => item.GetProperty("name").GetString() == RabbitTransport.Queue);
                Assert.Equal("quorum", queue.GetProperty("type").GetString());
                Assert.Equal(3, queue.GetProperty("members").GetArrayLength());
                if (queue.GetProperty("online").GetArrayLength() == online) return;
            }
            await Task.Delay(100);
        }
        Assert.Fail($"Quorum did not report {online} online members.");
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var node in Nodes) await node.DisposeAsync();
        await network.DisposeAsync();
    }
}
