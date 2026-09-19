extern alias Redirector;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shortener.Application;
using Shortener.LinkManagement.Tests;
using Xunit;

namespace Shortener.Redirection.Tests;

[Collection("redirection")]
public sealed class TelemetryTests(LinkEnvironment environment)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Metrics_distinguish_outcomes_and_capture_failures_without_sensitive_content()
    {
        await using var database = await environment.CreateAsync();
        await ResolutionTests.Seed(database);
        var clock = new FakeTimeProvider();
        var publisher = new MeasuredPublisher(clock);
        using var host = new RedirectHost(database.App.Settings, services =>
        {
            services.AddSingleton<IAccessPublisher>(publisher);
            services.AddSingleton<TimeProvider>(clock);
        });
        using var client = host.Client();
        using var metrics = new MetricProbe();
        async Task<int> Send(string method = "GET", string path = "/z", bool ip = true)
        {
            var response = await host.Server.SendAsync(context =>
            {
                context.Request.Method = method;
                context.Request.Path = path;
                if (ip) context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.123");
            });
            return response.Response.StatusCode;
        }
        foreach (var outcome in Enum.GetValues<PublishOutcome>())
        {
            publisher.Outcome = outcome;
            Assert.Equal(302, await Send());
        }
        publisher.Fail = true;
        Assert.Equal(302, await Send());
        Assert.Equal(302, await Send(ip: false));
        Assert.Equal(302, await Send("HEAD"));
        Assert.Equal(405, await Send("POST"));
        Assert.Equal(404, await Send(path: "/missing"));
        await database.SqlAsync("UPDATE links SET owner_disabled_at = now()");
        Assert.Equal(410, await Send());
        await database.SqlAsync("ALTER TABLE links RENAME TO unavailable_links");
        Assert.Equal(503, await Send());
        Assert.Equal(5, metrics.Sum("eligible"));
        Assert.Equal(10, metrics.Sum("resolutions"));
        Assert.Equal(6, metrics.Sum("resolutions", "resolved"));
        Assert.Equal(1, metrics.Sum("publications", "confirmed"));
        Assert.Equal(1, metrics.Sum("publications", "rejected"));
        Assert.Equal(2, metrics.Sum("publications", "unknown"));
        Assert.Equal(2, metrics.Sum("capture.failures"));
        Assert.All(metrics.Values.Where(x => x.Name.EndsWith("publish.duration", StringComparison.Ordinal)),
            item => Assert.Equal(0.025, item.Value, precision: 6));
        Assert.Equal(10, metrics.Values.Count(x => x.Name == "shortener.redirect.duration"));
        Assert.All(metrics.Values, item =>
        {
            Assert.All(item.Tags.Keys, key => Assert.Contains(key, new[] { "service", "result", "error_class" }));
            Assert.Equal("shortener-redirector", item.Tags["service"]);
        });
        var emitted = string.Join('\n', host.Logs.Messages) + string.Join('\n', metrics.Values.SelectMany(x => x.Tags.Values));
        foreach (var sensitive in new[] { "192.0.2.123", "sensitive-payload", "destination.invalid", "sourceIp", "linkId" })
            Assert.DoesNotContain(sensitive, emitted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Readiness_uses_primary_resolution_only_and_telemetry_failure_preserves_302()
    {
        await using var database = await environment.CreateAsync();
        await ResolutionTests.Seed(database);
        var settings = new Dictionary<string, string?>(database.App.Settings)
        {
            ["ConnectionStrings:Registry"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test",
            ["Messaging:Port"] = "1", ["Email:Port"] = "1", ["Telemetry:Endpoint"] = "http://127.0.0.1:1"
        };
        using var host = new RedirectHost(settings, services =>
        {
            services.AddSingleton<IAccessPublisher>(new CapturePublisher());
            services.AddHealthChecks().AddCheck("live-only", () => HealthCheckResult.Healthy());
        });
        using var client = host.Client();
        var registrations = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Equal("redirect-primary", Assert.Single(registrations, x => x.Tags.Contains("ready")).Name);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        using (var brokenTelemetry = new MetricProbe(throwOnRecord: true))
        {
            var response = await host.Server.SendAsync(context =>
            {
                context.Request.Method = "GET";
                context.Request.Path = "/z";
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
            });
            Assert.Equal(302, response.Response.StatusCode);
            Assert.Equal("https://destination.invalid/Case/%2F?a=1&a=2#fragment", response.Response.Headers.Location);
        }
        await database.SqlAsync("ALTER TABLE links RENAME TO unavailable_links");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/z")).StatusCode);
    }

    private sealed class MeasuredPublisher(FakeTimeProvider clock) : IAccessPublisher
    {
        public PublishOutcome Outcome { get; set; }
        public bool Fail { get; set; }
        public Task<PublishOutcome> PublishAsync(AccessRecorded message, CancellationToken cancellationToken)
        {
            clock.Advance(TimeSpan.FromMilliseconds(25));
            if (Fail) throw new InvalidOperationException("sensitive-payload " + message.SourceIp);
            return Task.FromResult(Outcome);
        }
    }
}

public sealed class MetricProbe : IDisposable
{
    private readonly MeterListener listener = new();
    public ConcurrentQueue<(string Name, double Value, Dictionary<string, string> Tags)> Values { get; } = new();
    public MetricProbe(bool throwOnRecord = false)
    {
        listener.InstrumentPublished = (instrument, observer) =>
        {
            if (instrument.Name.StartsWith("shortener.redirect.", StringComparison.Ordinal)) observer.EnableMeasurementEvents(instrument);
        };
        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (throwOnRecord) throw new InvalidOperationException("telemetry unavailable");
            Values.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(x => x.Key, x => x.Value?.ToString() ?? "")));
        }
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
    }
    public double Sum(string name, string? result = null) => Values.Where(x => x.Name == "shortener.redirect." + name
        && (result is null || x.Tags.GetValueOrDefault("result") == result)).Sum(x => x.Value);
    public void Dispose() => listener.Dispose();
}
