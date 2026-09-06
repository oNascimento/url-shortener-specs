using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        var value = await db.Database.SqlQueryRaw<long>("SELECT 9007199254740993::bigint AS \"Value\"").SingleAsync();
        Assert.Equal("\"9007199254740993\"", JsonSerializer.Serialize(value, Options));
    }
}
