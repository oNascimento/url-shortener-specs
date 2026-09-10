using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shortener.Authentication.Tests;
using Shortener.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Shortener.LinkManagement.Tests;

[CollectionDefinition("links")]
public sealed class LinkCollection : ICollectionFixture<LinkEnvironment>;

public sealed class LinkEnvironment : IAsyncLifetime
{
    private readonly PostgreSqlContainer primary = new PostgreSqlBuilder("postgres:17.6").WithCommand("-c", "max_connections=250").Build();
    private readonly PostgreSqlContainer registry = new PostgreSqlBuilder("postgres:17.6").Build();
    private readonly RSA key = RSA.Create(2048);
    public async Task InitializeAsync() => await Task.WhenAll(primary.StartAsync(), registry.StartAsync());
    public async Task DisposeAsync()
    {
        await primary.DisposeAsync();
        await registry.DisposeAsync();
        key.Dispose();
    }

    public async Task<LinkHost> CreateAsync()
    {
        static async Task<string> Database(string root)
        {
            var name = "links_" + Guid.NewGuid().ToString("N");
            await using var connection = new NpgsqlConnection(root);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE DATABASE " + name, connection);
            await command.ExecuteNonQueryAsync();
            return new NpgsqlConnectionStringBuilder(root) { Database = name, MaxPoolSize = 150,
                GssEncryptionMode = GssEncryptionMode.Disable, Timeout = 60, CommandTimeout = 180 }.ConnectionString;
        }
        var primaryConnection = await Database(primary.GetConnectionString());
        var registryConnection = await Database(registry.GetConnectionString());
        await using (var connection = new NpgsqlConnection(registryConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE TABLE id_reservations(singleton boolean PRIMARY KEY, upper_bound bigint NOT NULL); INSERT INTO id_reservations VALUES(true, 0)", connection);
            await command.ExecuteNonQueryAsync();
        }
        var host = new AuthenticationHost(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Primary"] = primaryConnection, ["ConnectionStrings:Registry"] = registryConnection,
            ["Messaging:Host"] = "127.0.0.1", ["Email:Host"] = "127.0.0.1", ["Email:From"] = "test@example.test",
            ["Origins:Management"] = "https://management.test", ["Origins:Short"] = "https://s.test",
            ["Origins:ShortAliases:0"] = "https://alias.test", ["Telemetry:Endpoint"] = "http://127.0.0.1:1",
            ["Jwt:Issuer"] = "integration", ["Jwt:Audience"] = "integration", ["Jwt:KeyId"] = "integration-key",
            ["Jwt:PrivateKeyPem"] = key.ExportRSAPrivateKeyPem()
        }, new ControlledClock(DateTimeOffset.UtcNow), new EphemeralDataProtectionProvider(), new Uri("http://127.0.0.1:1"));
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        return new LinkHost(host, primaryConnection, registryConnection);
    }
}

public sealed class LinkHost(AuthenticationHost host, string primary, string registry) : IAsyncDisposable
{
    public AuthenticationHost App { get; } = host;
    public string Primary { get; } = primary;
    public string Registry { get; } = registry;
    public LinkService Service => App.Services.GetRequiredService<LinkService>();

    public async Task<(Guid Owner, HttpClient Client)> UserAsync()
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = Guid.NewGuid() + "@example.test", EmailVerifiedAt = App.Clock.GetUtcNow() };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        var session = new AuthSession { Id = Guid.NewGuid(), UserId = user.Id, User = user, CreatedAt = App.Clock.GetUtcNow(), ExpiresAt = App.Clock.GetUtcNow().AddDays(30) };
        db.Users.Add(user);
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();
        var token = scope.ServiceProvider.GetRequiredService<JwtIssuer>().Issue(session,
            new Shortener.Application.UserResult(user.Id, user.Email, true, "user", user.CreatedAt)).AccessToken;
        var client = App.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://management.test"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (user.Id, client);
    }

    public async Task<object?> SqlAsync(string sql, bool external = false, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(external ? Registry : Primary);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter);
        return await command.ExecuteScalarAsync();
    }

    public async Task<T> TransactionAsync<T>(Func<NpgsqlConnection, Task<T>> action)
    {
        await using var connection = new NpgsqlConnection(Primary);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var result = await action(connection);
        await transaction.CommitAsync();
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        using var primaryPool = new NpgsqlConnection(Primary);
        using var registryPool = new NpgsqlConnection(Registry);
        NpgsqlConnection.ClearPool(primaryPool);
        NpgsqlConnection.ClearPool(registryPool);
    }
}
