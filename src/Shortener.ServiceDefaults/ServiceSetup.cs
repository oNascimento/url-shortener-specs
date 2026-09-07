using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Sockets;
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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Shortener.Application;
using Shortener.Domain;
using Shortener.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

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
        builder.Services.AddSingleton<TimeProvider>(_ => TimeProvider.System);
        builder.Services.AddSingleton(new ServiceIdentity(service, builder.Environment.EnvironmentName, builder.Configuration["Build:Version"] ?? "development"));
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Primary")));
        builder.Services.AddIdentityCore<ApplicationUser>(options => { options.Password.RequiredLength = 12; options.Password.RequiredUniqueChars = 1; options.Password.RequireDigit = false; options.Password.RequireLowercase = false; options.Password.RequireUppercase = false; options.Password.RequireNonAlphanumeric = false; options.User.RequireUniqueEmail = true; }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<PostgresRateLimiter>();
        if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Jwt:GenerateDevelopmentKey"))
            throw new InvalidOperationException("Development JWT key generation is not allowed outside Development.");
        builder.Services.AddSingleton<JwtIssuer>();
        var protection = builder.Services.AddDataProtection().SetApplicationName("Shortener");
        if (builder.Configuration["DataProtection:KeyPath"] is { } keyPath)
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
        builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-Token"; o.Cookie.Name = "__Secure-antiforgery"; o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.Path = "/api/v1/auth"; });
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<JwtIssuer>((options, issuer) =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = issuer.ValidationParameters();
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api/v1/auth")) context.NoResult();
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal!;
                    if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId)
                        || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId)
                        || !Guid.TryParse(principal.FindFirst("jti")?.Value, out _)
                        || !long.TryParse(principal.FindFirst("iat")?.Value, out _)
                        || await context.HttpContext.RequestServices.GetRequiredService<AuthService>().GetUserAsync(userId, sessionId, context.HttpContext.RequestAborted) is not { } user)
                    {
                        context.Fail("Invalid session.");
                        return;
                    }
                    var identity = (ClaimsIdentity)principal.Identity!;
                    foreach (var claim in identity.FindAll("role").ToArray()) identity.RemoveClaim(claim);
                    identity.AddClaim(new Claim("role", user.Role));
                },
                OnChallenge = async context => { context.HandleResponse(); await AuthHttp.WriteProblemAsync(context.HttpContext, 401, "unauthorized"); },
                OnAuthenticationFailed = context =>
                {
                    if (IsDatabaseFailure(context.Exception))
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(context.Exception).Throw();
                    return Task.CompletedTask;
                },
                OnForbidden = context => AuthHttp.WriteProblemAsync(context.HttpContext, 403, "forbidden")
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in builder.Configuration.GetSection("TrustedProxies").GetChildren())
                options.KnownProxies.Add(IPAddress.Parse(proxy.Value!));
            // Empty trusted lists mean trust all in the framework; preserve deny-by-default.
            if (options.KnownProxies.Count == 0) options.KnownProxies.Add(IPAddress.None);
        });
        builder.Services.AddSingleton<IRecoveryRegistry>(services =>
            new PostgresRecoveryRegistry(builder.Configuration.GetConnectionString("Registry")!));
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new DecimalInt64Converter()));
        var primary = builder.Configuration.GetConnectionString("Primary")!;
        var registry = builder.Configuration.GetConnectionString("Registry")!;
        var messagingHost = builder.Configuration["Messaging:Host"]!;
        var emailHost = builder.Configuration["Email:Host"]!;
        builder.Services.AddHealthChecks()
            .AddCheck("primary", new DelegateHealthCheck(cancellationToken => CheckDatabaseAsync(primary, "schema_metadata", cancellationToken)), tags: ["ready"])
            .AddCheck("registry", new DelegateHealthCheck(cancellationToken => CheckDatabaseAsync(registry, "id_reservations", cancellationToken)), tags: ["ready"])
            .AddCheck("messaging", new DelegateHealthCheck(cancellationToken => CheckTcpAsync(messagingHost, 5672, cancellationToken)), tags: ["ready"])
            .AddCheck("email", new DelegateHealthCheck(cancellationToken => CheckTcpAsync(emailHost, 1025, cancellationToken)), tags: ["ready"]);
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
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
            }
            catch (BadHttpRequestException error)
            {
                await AuthHttp.WriteProblemAsync(context, error.StatusCode, "invalid_input");
            }
            catch (Exception error) when (IsDatabaseFailure(error))
            {
                logger.LogError("Database operation unavailable ({type})", error.GetType().Name);
                await AuthHttp.WriteProblemAsync(context, 503, "service_unavailable");
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
        app.UseStatusCodePages(context => AuthHttp.WriteProblemAsync(context.HttpContext, context.HttpContext.Response.StatusCode,
            context.HttpContext.Response.StatusCode switch { 401 => "unauthorized", 403 => "forbidden", 404 => "not_found", _ => "invalid_input" }));
        app.Use(async (context, next) =>
        {
            if (context.Connection.RemoteIpAddress is null)
            {
                context.Request.Headers.Remove("X-Forwarded-For");
                context.Request.Headers.Remove("X-Forwarded-Proto");
            }
            await next(context);
        });
        app.UseForwardedHeaders();
        app.UseAuthentication();
        app.UseMiddleware<AuthHttp>();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status == HealthStatus.Healthy ? "ready" : "unavailable" }));
            }
        });
        app.Lifetime.ApplicationStarted.Register(() => logger.LogInformation(new EventId(1000, "ServiceStarted"), "Service {service} started in {environment} version {version} with outcome {outcome}", identity.Service, identity.Environment, identity.Version, "started"));
    }

    private static async Task<HealthCheckResult> CheckDatabaseAsync(string connectionString, string table, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"SELECT 1 FROM {table} LIMIT 1", connection);
            return await command.ExecuteScalarAsync(cancellationToken) is not null
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("dependency unavailable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("dependency unavailable");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("dependency unavailable");
        }
    }

    private static bool IsDatabaseFailure(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is System.Data.Common.DbException or DbUpdateException) return true;
        return false;
    }

    private static async Task<HealthCheckResult> CheckTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("dependency unavailable");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("dependency unavailable");
        }
    }

    private sealed class DelegateHealthCheck(Func<CancellationToken, Task<HealthCheckResult>> check) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken) => check(cancellationToken);
    }
}
public sealed record ServiceIdentity(string Service, string Environment, string Version);
