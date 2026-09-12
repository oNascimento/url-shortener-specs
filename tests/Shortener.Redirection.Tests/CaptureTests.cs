extern alias Redirector;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shortener.Application;
using Shortener.Authentication.Tests;
using Shortener.LinkManagement.Tests;
using Xunit;
using AccessCapture = Redirector::Shortener.Redirector.AccessCapture;
using AccessEnvelopeSerializer = Redirector::Shortener.Redirector.AccessEnvelopeSerializer;
using ResolvedLink = Redirector::Shortener.Redirector.ResolvedLink;
using RedirectForwarding = Redirector::Shortener.Redirector.RedirectForwarding;

namespace Shortener.Redirection.Tests;

[Collection("redirection")]
public sealed class CaptureTests(LinkEnvironment environment)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Forwarded_IP_requires_trusted_peer_network_and_hop_limit()
    {
        await using var database = await environment.CreateAsync();
        await ResolutionTests.Seed(database);
        var cases = new[]
        {
            ("192.0.2.10", "198.51.100.1", "", "", "1", "192.0.2.10"),
            ("::ffff:192.0.2.10", "198.51.100.1", "", "", "1", "192.0.2.10"),
            ("2001:db8::10", "198.51.100.1", "", "", "1", "2001:db8::10"),
            ("10.0.0.2", "198.51.100.1", "10.0.0.2", "", "1", "198.51.100.1"),
            ("10.0.0.2", "198.51.100.1, 10.0.0.1", "10.0.0.2", "10.0.0.0/24", "2", "198.51.100.1"),
            ("10.0.0.2", "198.51.100.1, 10.0.0.1", "", "10.0.0.0/24", "1", "10.0.0.1"),
            ("10.0.0.2", "198.51.100.1, 192.0.2.99", "10.0.0.2", "", "2", "192.0.2.99"),
            ("10.0.0.2", "invalid", "10.0.0.2", "", "1", "10.0.0.2")
        };
        foreach (var (peer, header, proxies, network, hops, expected) in cases)
        {
            var settings = new Dictionary<string, string?>(database.App.Settings) { ["ForwardedHeaders:ForwardLimit"] = hops };
            if (proxies.Length != 0) settings["TrustedProxies:0"] = proxies;
            if (network.Length != 0) settings["TrustedNetworks:0"] = network;
            var publisher = new CapturePublisher();
            using var host = new RedirectHost(settings, services => services.AddSingleton<IAccessPublisher>(publisher));
            using var client = host.Client();
            var response = await host.Server.SendAsync(context =>
            {
                context.Request.Method = "GET";
                context.Request.Path = "/z";
                context.Request.QueryString = new QueryString("?sourceIp=203.0.113.99");
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                context.Request.Headers["X-Forwarded-For"] = header;
            });
            Assert.Equal(302, response.Response.StatusCode);
            Assert.Equal(expected, Assert.Single(publisher.Messages).SourceIp);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Eligible_GETs_have_distinct_immutable_envelopes_and_others_publish_nothing()
    {
        await using var database = await environment.CreateAsync();
        await ResolutionTests.Seed(database, id: 9007199254740993);
        var publisher = new CapturePublisher();
        var now = new DateTimeOffset(2026, 9, 12, 15, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        using var host = new RedirectHost(database.App.Settings, services =>
        {
            services.AddSingleton<IAccessPublisher>(publisher);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ControlledClock(now));
        });
        using var client = host.Client();
        async Task Send(string method, string path, bool ip = true)
        {
            await host.Server.SendAsync(context =>
            {
                context.Request.Method = method;
                context.Request.Path = path;
                if (ip) context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
                context.Request.Headers["X-Forwarded-For"] = "203.0.113.1";
            });
        }
        for (var i = 0; i < 10; i++) await Send("GET", "/z");
        Assert.Equal(10, publisher.Messages.Select(x => x.EventId).Distinct().Count());
        foreach (var message in publisher.Messages)
        {
            Assert.Equal('4', message.EventId.ToString()[14]);
            Assert.Equal("9007199254740993", message.LinkId);
            Assert.Equal(now.UtcTicks - now.UtcTicks % 10, message.OccurredAt.UtcTicks);
            var bytes = AccessEnvelopeSerializer.Serialize(message);
            Assert.Equal(bytes, AccessEnvelopeSerializer.Serialize(message));
            using var json = JsonDocument.Parse(bytes);
            Assert.Equal(new[] { "eventId", "linkId", "occurredAt", "schemaVersion", "sourceIp" },
                json.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
            Assert.Equal("2026-09-12T15:00:00.123456Z", json.RootElement.GetProperty("occurredAt").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        }
        await Send("HEAD", "/z");
        await Send("POST", "/z");
        await Send("GET", "/missing");
        await Send("GET", "/!");
        await Send("GET", "/z", false);
        await database.SqlAsync("UPDATE links SET owner_disabled_at = now()");
        await Send("GET", "/z");
        await database.SqlAsync("ALTER TABLE links RENAME TO unavailable_links");
        await Send("GET", "/z");
        Assert.Equal(10, publisher.Messages.Count);
        Assert.Contains(host.Logs.Messages, line => line.Contains("missing_ip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unavailable_or_throwing_publisher_does_not_escape_capture()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        var link = new ResolvedLink(1, "https://destination.invalid", false);
        await new AccessCapture(TimeProvider.System, NullLogger<AccessCapture>.Instance).CaptureAsync(context, link);
        var publisher = new CapturePublisher { Failure = new InvalidOperationException("sensitive payload") };
        await new AccessCapture(TimeProvider.System, NullLogger<AccessCapture>.Instance, publisher).CaptureAsync(context, link);
        Assert.Single(publisher.Messages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Invalid_hop_configuration_fails_closed(int hops)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:ForwardLimit"] = hops.ToString() });
        RedirectForwarding.ConfigureRedirectForwarding(builder);
        using var services = builder.Services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }
}

public sealed class CapturePublisher : IAccessPublisher
{
    public ConcurrentQueue<AccessRecorded> Messages { get; } = new();
    public Exception? Failure { get; init; }
    public Task<PublishOutcome> PublishAsync(AccessRecorded message, CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);
        if (Failure is not null) throw Failure;
        return Task.FromResult(PublishOutcome.Confirmed);
    }
}
