using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shortener.Application;
using Shortener.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Shortener.Authentication.Tests;

public sealed class AuthenticationEnvironment : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.6").Build();
    private readonly IContainer mail = new ContainerBuilder("axllent/mailpit:v1.27.8")
        .WithPortBinding(1025, true).WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(x => x.ForPort(8025).ForPath("/api/v1/info"))).Build();
    private readonly RSA key = RSA.Create(2048);
    public async Task InitializeAsync() => await Task.WhenAll(postgres.StartAsync(), mail.StartAsync());
    public async Task DisposeAsync()
    {
        await mail.DisposeAsync();
        await postgres.DisposeAsync();
        key.Dispose();
    }

    public async Task<AuthenticationHost> CreateHostAsync()
    {
        var name = "auth_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        await command.ExecuteNonQueryAsync();
        var primary = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        { Database = name, GssEncryptionMode = GssEncryptionMode.Disable, Timeout = 60, CommandTimeout = 60 };
        var host = new AuthenticationHost(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Primary"] = primary.ConnectionString,
            ["ConnectionStrings:Registry"] = primary.ConnectionString,
            ["Messaging:Host"] = "127.0.0.1",
            ["Email:Host"] = mail.Hostname,
            ["Email:Port"] = mail.GetMappedPublicPort(1025).ToString(),
            ["Email:From"] = "no-reply@example.test",
            ["Origins:Management"] = "https://management.test",
            ["Origins:Short"] = "https://s.test",
            ["Telemetry:Endpoint"] = "http://127.0.0.1:1",
            ["Jwt:Issuer"] = "integration", ["Jwt:Audience"] = "integration",
            ["Jwt:KeyId"] = "integration-key", ["Jwt:PrivateKeyPem"] = key.ExportRSAPrivateKeyPem()
        }, new ControlledClock(DateTimeOffset.UtcNow), new EphemeralDataProtectionProvider(),
            new Uri($"http://{mail.Hostname}:{mail.GetMappedPublicPort(8025)}"));
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        return host;
    }
}

public sealed class AuthenticationHost(Dictionary<string, string?> settings, ControlledClock clock, IDataProtectionProvider protection, Uri mailUri) : WebApplicationFactory<Program>
{
    public Dictionary<string, string?> Settings { get; } = settings;
    public ControlledClock Clock { get; } = clock;
    public CaptureLogs Logs { get; } = new();
    public AuthenticationHost Replica(Dictionary<string, string?>? overrides = null)
    {
        var config = new Dictionary<string, string?>(Settings);
        if (overrides is not null) foreach (var pair in overrides) config[pair.Key] = pair.Value;
        return new AuthenticationHost(config, Clock, protection, mailUri);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in Settings) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton(protection);
            services.AddLogging(logging => logging.AddProvider(Logs));
        });
    }

    public async Task<T> WithDatabase<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<string> EmailToken(string recipient, string action)
    {
        using var http = new HttpClient { BaseAddress = mailUri };
        using var messages = await http.GetFromJsonAsync<JsonDocument>("/api/v1/messages");
        var match = messages!.RootElement.GetProperty("messages").EnumerateArray()
            .First(x => x.GetProperty("To").EnumerateArray().Any(to => to.GetProperty("Address").GetString() == recipient)
                && x.GetProperty("Subject").GetString() == (action == "verify-email" ? "Confirme seu e-mail" : "Redefinição de senha"));
        using var message = await http.GetFromJsonAsync<JsonDocument>("/api/v1/message/" + match.GetProperty("ID").GetString());
        var body = message!.RootElement.GetProperty("Text").GetString()!;
        var encoded = System.Text.RegularExpressions.Regex.Match(body, "#token=([^\\s]+)").Groups[1].Value;
        Assert.NotEmpty(encoded);
        return Uri.UnescapeDataString(encoded);
    }

    public async Task<AuthBrowser> Browser()
    {
        var browser = new AuthBrowser(CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://management.test"), AllowAutoRedirect = false, HandleCookies = false }), Logs);
        await browser.Csrf();
        return browser;
    }
}

public sealed class AuthBrowser(HttpClient client, CaptureLogs logs) : IDisposable
{
    public Dictionary<string, string> Cookies { get; } = [];
    public string? RequestToken { get; private set; }
    public string? AccessToken { get; set; }
    public void ImportSession(AuthBrowser other)
    {
        Cookies.Clear();
        foreach (var pair in other.Cookies) Cookies[pair.Key] = pair.Value;
        RequestToken = other.RequestToken;
        AccessToken = other.AccessToken;
    }
    public async Task Csrf()
    {
        using var response = await Send(HttpMethod.Get, "/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        RequestToken = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("requestToken").GetString();
    }
    public Task<HttpResponseMessage> Post(string operation, object? input = null) => Send(HttpMethod.Post, "/api/v1/auth/" + operation, input);
    public Task<HttpResponseMessage> Me() => Send(HttpMethod.Get, "/api/v1/me", bearer: AccessToken);

    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? input = null,
        bool csrf = true, string? origin = "https://management.test", string? bearer = null, string? cookies = null, string? forwardedIp = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (input is not null) request.Content = JsonContent.Create(input);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (csrf && RequestToken is not null) request.Headers.Add("X-CSRF-Token", RequestToken);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        var cookieHeader = cookies ?? string.Join("; ", Cookies.Select(x => x.Key + "=" + x.Value));
        if (cookieHeader.Length != 0) request.Headers.Add("Cookie", cookieHeader);
        if (forwardedIp is not null) request.Headers.Add("X-Forwarded-For", forwardedIp);
        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values)
            {
                var pair = value.Split(';')[0].Split('=', 2);
                Cookies[pair[0]] = pair[1];
            }
        return response;
    }

    public async Task<AuthResult> Login(string email, string password = "a password with spaces")
    {
        using var response = await Post("login", new LoginInput(email, password));
        Assert.True(response.IsSuccessStatusCode, $"Login returned {(int)response.StatusCode}. Operational logs: {string.Join('\n', logs.Messages)}");
        var result = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        AccessToken = result.AccessToken;
        return result;
    }
    public void Dispose() => client.Dispose();
}

public sealed class ControlledClock(DateTimeOffset now) : TimeProvider
{
    private long ticks = now.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}

public sealed class CaptureLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
    public void Dispose() { }
    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
