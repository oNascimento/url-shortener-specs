using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shortener.Application;
using Shortener.Domain;
using Shortener.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;
namespace Shortener.Foundation.Tests;

public class ContractTests
{
    private static JsonSerializerOptions Options => new() { Converters = { new DecimalInt64Converter() } };
    [Theory]
    [InlineData(9007199254740993L)]
    [InlineData(long.MaxValue)]
    public void Int64RoundTripsAsString(long value)
    {
        var json = JsonSerializer.Serialize(value, Options);
        Assert.Equal($"\"{value}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<long>(json, Options));
    }
    [Theory]
    [InlineData("9007199254740993")]
    [InlineData("\"9223372036854775808\"")]
    public void InvalidWireNumbersAreRejected(string json) => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<long>(json, Options));
    [Fact, Trait("Category", "Integration")]
    public async Task MigrationIsRepeatableAndDatabasePreservesInt64()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.6").Build();
        await postgres.StartAsync();
        var connection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
            // Container startup on a busy development machine is not a performance benchmark.
            Timeout = 60,
            CommandTimeout = 60
        };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        var value = await db.Database.SqlQueryRaw<long>("SELECT 9007199254740993::bigint AS \"Value\"").SingleAsync();
        Assert.Equal("\"9007199254740993\"", JsonSerializer.Serialize(value, Options));
    }

    [Fact, Trait("Category", "Integration")]
    public async Task RecoveryRegistryUsesCompareExchangeAndIdempotentDeletionMarkers()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.6").Build();
        await postgres.StartAsync();
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var schema = new NpgsqlCommand("""
            CREATE TABLE id_reservations (singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton), upper_bound bigint NOT NULL CHECK (upper_bound >= 0));
            INSERT INTO id_reservations VALUES (true, 0);
            CREATE TABLE deletion_markers (user_id uuid PRIMARY KEY, job_id uuid UNIQUE NOT NULL, requested_at timestamptz NOT NULL, completed_at timestamptz);
            """, connection))
        {
            await schema.ExecuteNonQueryAsync();
        }

        var registry = new PostgresRecoveryRegistry(postgres.GetConnectionString());
        Assert.Equal(0, await registry.ReadUpperBoundAsync(CancellationToken.None));
        Assert.True(await registry.CompareExchangeUpperBoundAsync(0, 100, CancellationToken.None));
        Assert.False(await registry.CompareExchangeUpperBoundAsync(0, 200, CancellationToken.None));
        Assert.Equal(100, await registry.ReadUpperBoundAsync(CancellationToken.None));

        var requestedAt = DateTimeOffset.UtcNow;
        requestedAt = requestedAt.AddTicks(-(requestedAt.Ticks % 10));
        var marker = new DeletionMarker(Guid.NewGuid(), Guid.NewGuid(), requestedAt);
        Assert.Equal(marker, await registry.RecordDeletionAsync(marker, CancellationToken.None));
        Assert.Equal(marker, await registry.RecordDeletionAsync(marker, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.RecordDeletionAsync(
            marker with { JobId = Guid.NewGuid() }, CancellationToken.None));
    }
}
