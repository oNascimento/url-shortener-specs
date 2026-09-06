using Npgsql;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed class PostgresRecoveryRegistry(string connectionString) : IRecoveryRegistry
{
    public async Task<long> ReadUpperBoundAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT upper_bound FROM id_reservations WHERE singleton = true", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is long upperBound
            ? upperBound
            : throw new InvalidOperationException("Recovery registry reservation row is missing.");
    }

    public async Task<bool> CompareExchangeUpperBoundAsync(long expected, long next, CancellationToken cancellationToken)
    {
        if (next < expected)
            throw new ArgumentOutOfRangeException(nameof(next), "The next upper bound cannot be lower than the expected value.");

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE id_reservations SET upper_bound = $1 WHERE singleton = true AND upper_bound = $2",
            connection,
            transaction);
        command.Parameters.AddWithValue(next);
        command.Parameters.AddWithValue(expected);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated == 1;
    }

    public async Task<DeletionMarker> RecordDeletionAsync(DeletionMarker marker, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO deletion_markers (user_id, job_id, requested_at)
            VALUES ($1, $2, $3)
            ON CONFLICT (user_id) DO NOTHING
            RETURNING user_id, job_id, requested_at
            """, connection, transaction);
        insert.Parameters.AddWithValue(marker.UserId);
        insert.Parameters.AddWithValue(marker.JobId);
        insert.Parameters.AddWithValue(marker.RequestedAt);

        await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var recorded = ReadMarker(reader);
            await reader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return recorded;
        }

        await reader.CloseAsync();
        await using var existing = new NpgsqlCommand("""
            SELECT user_id, job_id, requested_at
            FROM deletion_markers
            WHERE user_id = $1
            """, connection, transaction);
        existing.Parameters.AddWithValue(marker.UserId);
        await using var existingReader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await existingReader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Recovery registry deletion marker disappeared during reconciliation.");

        var prior = ReadMarker(existingReader);
        await existingReader.CloseAsync();
        await transaction.CommitAsync(cancellationToken);
        if (prior != marker)
            throw new InvalidOperationException("A different deletion marker already exists for this user.");
        return prior;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static DeletionMarker ReadMarker(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetFieldValue<DateTimeOffset>(2));
}
