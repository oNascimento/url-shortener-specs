using System.Globalization;
using System.Net;
using Npgsql;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed record BatchResult(long Inserted, int Expired, int Ineligible, DateTimeOffset? LastPersistedEventAt);

public sealed class AccessBatchStore(string connectionString, TimeProvider clock)
{
    // F005-T01/T12: the INSERT's RETURNING set is the only source of aggregate increments.
    public async Task<BatchResult> PersistAsync(IReadOnlyList<AccessRecorded> batch, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var expired = batch.Count(x => x.OccurredAt < now.AddDays(-90));
        var events = batch.Where(x => x.OccurredAt >= now.AddDays(-90))
            .DistinctBy(x => (x.OccurredAt, x.EventId)).ToArray();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var ids = events.Select(x => long.Parse(x.LinkId, CultureInfo.InvariantCulture)).Distinct().ToArray();
        await using (var users = new NpgsqlCommand("""
            SELECT id FROM users WHERE id IN (SELECT owner_id FROM links WHERE id = ANY($1))
            ORDER BY id FOR SHARE
            """, connection, transaction))
        {
            users.Parameters.AddWithValue(ids);
            await using var locked = await users.ExecuteReaderAsync(ct);
            while (await locked.ReadAsync(ct)) { }
        }
        var eligible = new HashSet<long>();
        await using (var links = new NpgsqlCommand("""
            SELECT l.id, l.deleted_at IS NULL AND u.deletion_requested_at IS NULL AND u.id IS NOT NULL
            FROM links l LEFT JOIN users u ON u.id = l.owner_id WHERE l.id = ANY($1)
            ORDER BY l.id FOR SHARE OF l
            """, connection, transaction))
        {
            links.Parameters.AddWithValue(ids);
            await using var reader = await links.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) if (reader.GetBoolean(1)) eligible.Add(reader.GetInt64(0));
        }
        var accepted = events.Where(x => eligible.Contains(long.Parse(x.LinkId, CultureInfo.InvariantCulture))).ToArray();
        await using var command = new NpgsqlCommand("""
            WITH source AS (
                SELECT * FROM unnest($1::timestamptz[], $2::uuid[], $3::bigint[], $4::inet[])
                    AS s(occurred_at,event_id,link_id,source_ip)
            ), inserted AS (
                INSERT INTO access_events SELECT * FROM source
                ON CONFLICT (occurred_at,event_id) DO NOTHING RETURNING occurred_at,event_id,link_id
            ), aggregated AS (
                INSERT INTO daily_totals(link_id,day_utc,stripe,count,updated_at)
                SELECT link_id, (occurred_at AT TIME ZONE 'UTC')::date,
                    (('x' || substr(event_id::text,1,2))::bit(8)::int % 16)::smallint, count(*), $5
                FROM inserted GROUP BY 1,2,3 ORDER BY 1,2,3
                ON CONFLICT (link_id,day_utc,stripe) DO UPDATE
                    SET count = daily_totals.count + EXCLUDED.count, updated_at = EXCLUDED.updated_at
                RETURNING count
            ) SELECT count(*), max(occurred_at) FROM inserted
            """, connection, transaction);
        command.Parameters.AddWithValue(accepted.Select(x => x.OccurredAt).ToArray());
        command.Parameters.AddWithValue(accepted.Select(x => x.EventId).ToArray());
        command.Parameters.AddWithValue(accepted.Select(x => long.Parse(x.LinkId, CultureInfo.InvariantCulture)).ToArray());
        command.Parameters.AddWithValue(accepted.Select(x => IPAddress.Parse(x.SourceIp)).ToArray());
        command.Parameters.AddWithValue(now);
        long inserted;
        DateTimeOffset? last;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            inserted = reader.GetInt64(0);
            last = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }
        await transaction.CommitAsync(ct);
        return new(inserted, expired, events.Length - accepted.Length, last);
    }
}
