extern alias Redirector;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Shortener.Application;
using Shortener.LinkManagement.Tests;
using Xunit;
using RabbitTransport = Redirector::Shortener.Redirector.RabbitTransport;
using BoundedAccessPublisher = Redirector::Shortener.Redirector.BoundedAccessPublisher;
using AccessEnvelopeSerializer = Redirector::Shortener.Redirector.AccessEnvelopeSerializer;

namespace Shortener.Redirection.Tests;

[Collection("rabbit-publisher")]
[Trait("Category", "Integration")]
public sealed class RabbitTests(RabbitEnvironment rabbit, LinkEnvironment links)
{
    [Fact]
    public async Task Lost_real_confirms_are_unknown_and_capacity_recovers_without_republishing()
    {
        var broker = await rabbit.SettingsAsync();
        await using var proxy = new AmqpProxy(broker.Host, broker.Port);
        var settings = broker with { Host = "127.0.0.1", Port = proxy.Port };
        await using var harness = new RabbitHarness(settings);
        await harness.StartAsync();
        var publisher = new BoundedAccessPublisher(harness.Transport, TimeProvider.System, settings);
        Assert.Equal(PublishOutcome.Confirmed, await publisher.PublishAsync(PublisherTests.Message, default));
        proxy.Pause();
        var timer = Stopwatch.StartNew();
        Assert.Equal(PublishOutcome.Unknown, await publisher.PublishAsync(PublisherTests.Message, default).WaitAsync(TimeSpan.FromSeconds(2)));
        timer.Stop();
        Console.WriteLine($"T13 real confirm timeout: {timer.Elapsed.TotalMilliseconds:F3} ms; configured budget: 100 ms");
        proxy.Resume();
        await RabbitEnvironment.WaitUntil(() => harness.Transport.Ready);
        Assert.Equal(PublishOutcome.Confirmed, await publisher.PublishAsync(PublisherTests.Message, default));
        await using var inspection = await RabbitEnvironment.Factory(broker).CreateConnectionAsync();
        await using var channel = await inspection.CreateChannelAsync();
        Assert.Equal(3u, await channel.MessageCountAsync(RabbitTransport.Queue));
    }

    [Fact]
    public async Task Confirmed_messages_are_persistent_and_redelivery_preserves_exact_envelope()
    {
        var settings = await rabbit.SettingsAsync();
        await using var harness = new RabbitHarness(settings);
        await harness.StartAsync();
        var publisher = new BoundedAccessPublisher(harness.Transport, TimeProvider.System, settings);
        // Warm serialization/JIT before measuring the request budget.
        var bytes = AccessEnvelopeSerializer.Serialize(PublisherTests.Message);
        Assert.Equal(PublishOutcome.Confirmed, await publisher.PublishAsync(PublisherTests.Message, default));
        await using var connection = await RabbitEnvironment.Factory(settings).CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        var delivery = await channel.BasicGetAsync(RabbitTransport.Queue, autoAck: false);
        Assert.NotNull(delivery);
        Assert.True(delivery.BasicProperties.Persistent);
        Assert.Equal("application/json", delivery.BasicProperties.ContentType);
        Assert.Equal(RabbitTransport.Queue, delivery.BasicProperties.Type);
        Assert.Equal(bytes, delivery.Body.ToArray());
        await channel.CloseAsync();
        await channel.DisposeAsync();
        await using var retryChannel = await connection.CreateChannelAsync();
        BasicGetResult? retry = null;
        var timer = Stopwatch.StartNew();
        while (retry is null && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            retry = await retryChannel.BasicGetAsync(RabbitTransport.Queue, autoAck: true);
            if (retry is null) await Task.Delay(25);
        }
        Assert.NotNull(retry);
        Assert.True(retry.Redelivered);
        Assert.Equal(bytes, retry.Body.ToArray());
    }

    [Fact]
    public async Task Mandatory_returns_and_queue_overflow_are_rejected_even_when_exchange_acks()
    {
        var settings = await rabbit.SettingsAsync();
        await using var harness = new RabbitHarness(settings);
        await harness.StartAsync();
        await using var connection = await RabbitEnvironment.Factory(settings).CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var publisher = new BoundedAccessPublisher(harness.Transport, TimeProvider.System, settings);
        await channel.QueueUnbindAsync(RabbitTransport.Queue, RabbitTransport.Exchange, RabbitTransport.RoutingKey);
        Assert.Equal(PublishOutcome.Rejected, await publisher.PublishAsync(PublisherTests.Message, default));
        await channel.QueueBindAsync(RabbitTransport.Queue, RabbitTransport.Exchange, RabbitTransport.RoutingKey);
        Assert.Equal(PublishOutcome.Confirmed, await publisher.PublishAsync(PublisherTests.Message, default));

        var small = await rabbit.SettingsAsync(1);
        await using var saturated = new RabbitHarness(small);
        await saturated.StartAsync();
        var limited = new BoundedAccessPublisher(saturated.Transport, TimeProvider.System, small);
        var outcomes = new List<PublishOutcome>();
        for (var i = 0; i < 5; i++) outcomes.Add(await limited.PublishAsync(PublisherTests.Message, default));
        Assert.Contains(PublishOutcome.Rejected, outcomes);
    }

    [Fact]
    public async Task Broker_failure_and_startup_without_broker_preserve_HTTP_and_recover()
    {
        var settings = await rabbit.SettingsAsync();
        await using var database = await links.CreateAsync();
        await ResolutionTests.Seed(database);
        var configuration = new Dictionary<string, string?>(database.App.Settings)
        {
            ["Messaging:Host"] = settings.Host, ["Messaging:Port"] = settings.Port.ToString(),
            ["Messaging:UserName"] = settings.UserName, ["Messaging:Password"] = settings.Password,
            ["Messaging:VirtualHost"] = settings.VirtualHost, ["Messaging:PoolSize"] = "2"
        };
        using var host = new RedirectHost(configuration);
        using var client = host.Client();
        var transport = host.Services.GetRequiredService<RabbitTransport>();
        await RabbitEnvironment.WaitUntil(() => transport.Ready);
        async Task Redirect()
        {
            var response = await host.Server.SendAsync(context =>
            {
                context.Request.Method = "GET";
                context.Request.Path = "/z";
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
            });
            Assert.Equal(302, response.Response.StatusCode);
            Assert.Equal("https://destination.invalid/Case/%2F?a=1&a=2#fragment", response.Response.Headers.Location.ToString());
        }
        await Redirect();
        // Stop the broker application without changing Docker's dynamically allocated host port.
        Assert.Equal(0, (await rabbit.Container.ExecAsync(["rabbitmqctl", "stop_app"])).ExitCode);
        try
        {
            await Redirect();
            using var cold = new RedirectHost(configuration);
            using var coldClient = cold.Client();
            var response = await cold.Server.SendAsync(context =>
            {
                context.Request.Method = "GET";
                context.Request.Path = "/z";
                context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
            });
            Assert.Equal(302, response.Response.StatusCode);
            Assert.False(cold.Services.GetRequiredService<RabbitTransport>().Ready);
        }
        finally { Assert.Equal(0, (await rabbit.Container.ExecAsync(["rabbitmqctl", "start_app"])).ExitCode); }
        await RabbitEnvironment.WaitUntil(() => transport.Ready);
        await Redirect();
        Assert.DoesNotContain(host.Logs.Messages, line => line.Contains(settings.Password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Incompatible_topology_does_not_kill_host_and_recovers_after_repair()
    {
        var settings = await rabbit.SettingsAsync();
        await using var connection = await RabbitEnvironment.Factory(settings).CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(RabbitTransport.Queue, durable: false, exclusive: false, autoDelete: false);
        await using var harness = new RabbitHarness(settings);
        await harness.Transport.StartAsync(default);
        await Task.Delay(1500);
        Assert.False(harness.Transport.Ready);
        Assert.Equal(PublishOutcome.Rejected, await harness.Transport.PublishAsync(AccessEnvelopeSerializer.Serialize(PublisherTests.Message), default));
        await channel.QueueDeleteAsync(RabbitTransport.Queue);
        await RabbitEnvironment.WaitUntil(() => harness.Transport.Ready);
        Assert.Equal(PublishOutcome.Confirmed, await harness.Transport.PublishAsync(AccessEnvelopeSerializer.Serialize(PublisherTests.Message), default));
    }
}
