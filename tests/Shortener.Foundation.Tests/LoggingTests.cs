using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortener.ServiceDefaults;
using Xunit;

namespace Shortener.Foundation.Tests;

public class LoggingTests
{
    [Fact]
    public void TimeProviderCanBeReplacedByTheHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Primary"] = "Host=localhost;Database=test",
            ["ConnectionStrings:Registry"] = "Host=localhost;Database=registry",
            ["Messaging:Host"] = "localhost",
            ["Email:Host"] = "localhost",
            ["Origins:Management"] = "https://localhost",
            ["Origins:Short"] = "https://s.localhost",
            ["Telemetry:Endpoint"] = "http://127.0.0.1:1"
        });
        builder.AddFoundation("shortener-test");
        var fake = new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        builder.Services.AddSingleton<TimeProvider>(fake);

        using var app = builder.Build();
        Assert.Same(fake, app.Services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void MissingConfigurationFailsBeforeStartup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();
        var error = Assert.Throws<InvalidOperationException>(() => builder.AddFoundation("shortener-test"));
        Assert.Contains("ConnectionStrings:Primary", error.Message);
    }

    [Fact]
    public async Task ExceptionDoesNotLeakSensitiveDataAndUnavailableCollectorDoesNotBlockRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Primary"] = "Host=localhost;Database=test;Username=test;Password=sentinel-password",
            ["ConnectionStrings:Registry"] = "Host=localhost;Database=test",
            ["Messaging:Host"] = "localhost",
            ["Email:Host"] = "localhost",
            ["Origins:Management"] = "https://localhost",
            ["Origins:Short"] = "https://s.localhost",
            ["Telemetry:Endpoint"] = "http://127.0.0.1:1"
        });
        builder.AddFoundation("shortener-test");
        var capture = new CaptureProvider();
        builder.Logging.AddProvider(capture);
        await using var app = builder.Build();
        app.UseFoundation();
        app.MapGet("/failure", (Func<string>)(() => throw new InvalidOperationException("sentinel-password sentinel-token person@example.test https://private.test 192.0.2.1")));
        await app.StartAsync();
        var client = app.GetTestClient();
        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await ready.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("Authorization", "Bearer sentinel-token");
        var timer = Stopwatch.StartNew();
        var response = await client.GetAsync("/failure?secret=sentinel-query");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        var captured = string.Join('\n', capture.Messages) + body;
        foreach (var secret in new[] { "sentinel-password", "sentinel-token", "sentinel-query", "person@example.test", "private.test", "192.0.2.1" })
            Assert.DoesNotContain(secret, captured);
        Assert.Single(capture.Messages, message => message.Contains("Request failed"));
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }
    }
    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
