using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Shortener.Domain;
using Shortener.Infrastructure;

namespace Shortener.ServiceDefaults;

public static class ServiceSetup
{
    public const string MeterName = "Shortener";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("shortener.requests");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("shortener.request.duration", "s");

    public static void AddFoundation(this WebApplicationBuilder builder, string service)
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        foreach (var name in new[] { "ConnectionStrings:Primary", "ConnectionStrings:Registry", "Messaging:Host", "Email:Host", "Origins:Management", "Origins:Short", "Telemetry:Endpoint" })
            if (string.IsNullOrWhiteSpace(builder.Configuration[name]))
                throw new InvalidOperationException($"Required configuration missing: {name}");
        foreach (var name in new[] { "Origins:Management", "Origins:Short", "Telemetry:Endpoint" })
            if (!Uri.TryCreate(builder.Configuration[name], UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new InvalidOperationException($"Invalid HTTP origin/endpoint configuration: {name}");
        var resource = ResourceBuilder.CreateDefault().AddService(service, serviceVersion: builder.Configuration["Build:Version"] ?? "development")
            .AddAttributes(new Dictionary<string, object> { ["deployment.environment.name"] = builder.Environment.EnvironmentName });
        builder.Logging.ClearProviders();
        // Only application-owned templates may reach console/OTLP. Framework diagnostics can contain URLs and SQL.
        builder.Logging.AddFilter((category, level) => category?.StartsWith("Shortener", StringComparison.Ordinal) == true && level >= LogLevel.Information);
        builder.Logging.AddJsonConsole(options => { options.IncludeScopes = true; options.UseUtcTimestamp = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; });
        builder.Services.Configure<ConsoleLoggerOptions>(options =>
        {
            options.MaxQueueLength = 2048;
            options.QueueFullMode = ConsoleLoggerQueueFullMode.DropWrite;
        });
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resource);
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.AddOtlpExporter(exporter => { exporter.Endpoint = new Uri(builder.Configuration["Telemetry:Endpoint"]!); exporter.TimeoutMilliseconds = 1000; });
        });
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.SetResourceBuilder(resource).AddMeter(MeterName)
            .AddView("shortener.request.duration", new ExplicitBucketHistogramConfiguration { Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.2, 0.5, 1, 2, 5] })
            .AddOtlpExporter(exporter => { exporter.Endpoint = new Uri(builder.Configuration["Telemetry:Endpoint"]!); exporter.TimeoutMilliseconds = 1000; }));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new ServiceIdentity(service, builder.Environment.EnvironmentName, builder.Configuration["Build:Version"] ?? "development"));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Primary")));
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalInt64Converter()));
        builder.Services.AddHealthChecks();
    }

    public static void UseFoundation(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Shortener.Requests");
        var identity = app.Services.GetRequiredService<ServiceIdentity>();
        app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            var operation = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["traceId"] = Activity.Current?.TraceId.ToString(),
                ["spanId"] = Activity.Current?.SpanId.ToString(),
                ["operation"] = operation,
                ["service"] = identity.Service,
                ["environment"] = identity.Environment,
                ["version"] = identity.Version
            });
            context.Response.Headers.CacheControl = "no-store";
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
            }
            catch (Exception error)
            {
                // Do not pass exception.Message/ToString: third-party exceptions can embed secrets.
                logger.LogError(new EventId(1001, "RequestFailed"), "Request failed with outcome {outcome}, type {errorType}, duration {durationMs}", "unexpected_error", error.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (context.Response.HasStarted) { context.Abort(); return; }
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { type = "about:blank", title = "Erro inesperado", status = 500, code = "internal_error", traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier }));
            }
            finally
            {
                var tags = new TagList { { "operation", operation }, { "status", context.Response.StatusCode } };
                Requests.Add(1, tags);
                Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
            }
        });
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapGet("/health/ready", async (AppDbContext db, CancellationToken token) =>
        {
            try { return await db.Database.SqlQueryRaw<int>("SELECT id AS \"Value\" FROM schema_metadata WHERE id = 1").SingleAsync(token) == 1 ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
            catch { return Results.StatusCode(503); }
        });
        app.Lifetime.ApplicationStarted.Register(() => logger.LogInformation(new EventId(1000, "ServiceStarted"), "Service {service} started in {environment} version {version} with outcome {outcome}", identity.Service, identity.Environment, identity.Version, "started"));
    }
}
public sealed record ServiceIdentity(string Service, string Environment, string Version);
