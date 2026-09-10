using Npgsql;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed class LinkSequence(IRecoveryRegistry registry)
{
    // Shared by allocation and maintenance, across every API replica of the elected primary.
    private const long CoordinatorLock = 300030003;
    public const long RangeSize = 1_000_000;

    public async Task<long> NextAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var state = await LockAsync(connection, ct);
        if (state.Suspended) throw new LinkFailure(503, "creation_suspended");
        await using var sequence = new NpgsqlCommand("SELECT last_value, is_called FROM link_ids", connection);
        await using var reader = await sequence.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var last = reader.GetInt64(0);
        var called = reader.GetBoolean(1);
        await reader.CloseAsync();
        var remaining = state.Upper - last + (called ? 0 : 1);
        if (remaining <= RangeSize / 5 && state.Upper != long.MaxValue)
        {
            (long Upper, long? First)? authorized = null;
            try { authorized = await AuthorizeAsync(state.Upper, ct); }
            catch (Exception error) when (error is not OperationCanceledException && error is not LinkFailure { Code: "registry_integrity" })
            {
                if (remaining <= 0) throw new LinkFailure(503, "reservation_unavailable");
            }
            if (authorized.HasValue) await ExtendAsync(connection, authorized.Value.Upper, authorized.Value.First, ct);
        }
        long id;
        try
        {
            await using var next = new NpgsqlCommand("SELECT nextval('link_ids')", connection);
            id = (long)(await next.ExecuteScalarAsync(ct))!;
        }
        catch (PostgresException error) when (error.SqlState == "2200H")
        {
            throw new LinkFailure(503, "capacity_exhausted");
        }
        return id;
    }

    public async Task SuspendAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await LockAsync(connection, ct);
        await using var command = new NpgsqlCommand("UPDATE link_allocation SET suspended = true", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task PrepareRestoreAsync(NpgsqlConnection connection, IWriterIsolation isolation, CancellationToken ct)
    {
        var state = await LockAsync(connection, ct);
        if (!state.Suspended) throw new LinkFailure(503, "creation_not_suspended");
        if (!await isolation.IsConfirmedAsync(ct)) throw new LinkFailure(503, "writer_not_isolated");
        var previous = await registry.ReadUpperBoundAsync(ct);
        if (previous < state.Upper) throw new LinkFailure(503, "registry_integrity");
        if (previous == long.MaxValue) throw new LinkFailure(503, "capacity_exhausted");
        var upper = await ReserveAsync(previous, ct);
        await SetBoundAsync(connection, upper, ct);
        await using var restart = new NpgsqlCommand("SELECT setval('link_ids', $1, false)", connection);
        restart.Parameters.AddWithValue(checked(previous + 1));
        await restart.ExecuteNonQueryAsync(ct);
        await using var resume = new NpgsqlCommand("UPDATE link_allocation SET suspended = false", connection);
        await resume.ExecuteNonQueryAsync(ct);
    }

    private async Task<(long Upper, long? First)> AuthorizeAsync(long localUpper, CancellationToken ct)
    {
        var external = await registry.ReadUpperBoundAsync(ct);
        if (external < localUpper) throw new LinkFailure(503, "registry_integrity");
        if (external == localUpper || localUpper == 0)
        {
            var upper = await ReserveAsync(external, ct);
            return (upper, localUpper == 0 ? checked(external + 1) : null);
        }
        return (external, null);
    }

    private static async Task ExtendAsync(NpgsqlConnection connection, long upper, long? first, CancellationToken ct)
    {
        await SetBoundAsync(connection, upper, ct);
        if (first.HasValue)
        {
            await using var restart = new NpgsqlCommand("SELECT setval('link_ids', $1, false)", connection);
            restart.Parameters.AddWithValue(first.Value);
            await restart.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<long> ReserveAsync(long previous, CancellationToken ct)
    {
        if (previous == long.MaxValue) throw new LinkFailure(503, "capacity_exhausted");
        var upper = checked(previous + Math.Min(RangeSize, long.MaxValue - previous));
        try
        {
            if (await registry.CompareExchangeUpperBoundAsync(previous, upper, ct)) return upper;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A failed response is ambiguous: durable state, not the exception, decides authorization.
        }
        var confirmed = await registry.ReadUpperBoundAsync(ct);
        if (confirmed < upper) throw new LinkFailure(503, "reservation_unconfirmed");
        return confirmed;
    }

    private static async Task SetBoundAsync(NpgsqlConnection connection, long upper, CancellationToken ct)
    {
        await using var alter = new NpgsqlCommand($"ALTER SEQUENCE link_ids MAXVALUE {upper.ToString(System.Globalization.CultureInfo.InvariantCulture)}", connection);
        await alter.ExecuteNonQueryAsync(ct);
        await using var save = new NpgsqlCommand("UPDATE link_allocation SET upper_bound = $1", connection);
        save.Parameters.AddWithValue(upper);
        await save.ExecuteNonQueryAsync(ct);
    }

    private static async Task<(long Upper, bool Suspended)> LockAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection);
        gate.Parameters.AddWithValue(CoordinatorLock);
        await gate.ExecuteNonQueryAsync(ct);
        await using var command = new NpgsqlCommand("SELECT upper_bound, suspended FROM link_allocation WHERE singleton", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.GetBoolean(1));
    }
}
