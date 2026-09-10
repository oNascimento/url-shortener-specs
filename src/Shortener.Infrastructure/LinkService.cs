using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Shortener.Application;
using Shortener.Domain;

namespace Shortener.Infrastructure;

public sealed class LinkService(string connectionString, string shortOrigin, DestinationRules destinations,
    LinkSequence sequence, LinkCursor cursors, TimeProvider clock)
{
    private const string Projection = """
        SELECT l.id, l.code, l.destination_url, l.created_at,
            CASE WHEN l.owner_disabled_at IS NOT NULL THEN 'disabled'
                 WHEN l.admin_blocked_at IS NOT NULL OR u.blocked_at IS NOT NULL THEN 'blocked'
                 ELSE 'active' END
        FROM links l JOIN users u ON u.id = l.owner_id
        """;

    public async Task<LinkResult> CreateAsync(Guid owner, Guid key, string? destination, CancellationToken ct)
    {
        var url = destinations.Validate(destination) ?? throw new LinkFailure(400, "invalid_input");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var user = new NpgsqlCommand("""
            SELECT id FROM users WHERE id = $1 AND email_verified_at IS NOT NULL
                AND blocked_at IS NULL AND deletion_requested_at IS NULL FOR SHARE
            """, connection))
        {
            user.Parameters.AddWithValue(owner);
            if (await user.ExecuteScalarAsync(ct) is null) throw new LinkFailure(401, "unauthorized");
        }
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection))
        {
            gate.Parameters.AddWithValue(BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"link:{owner}:{key}"))));
            await gate.ExecuteNonQueryAsync(ct);
        }
        var now = clock.GetUtcNow();
        now = now.AddTicks(-(now.Ticks % 10)); // Match PostgreSQL microseconds on the original response and every replay.
        await using (var request = new NpgsqlCommand("""
            SELECT url_hash, link_id FROM creation_requests
            WHERE owner_id = $1 AND idempotency_key = $2 AND expires_at > $3
            """, connection))
        {
            request.Parameters.AddWithValue(owner);
            request.Parameters.AddWithValue(key);
            request.Parameters.AddWithValue(now);
            await using var reader = await request.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (!hash.AsSpan().SequenceEqual(reader.GetFieldValue<byte[]>(0))) throw new LinkFailure(409, "idempotency_conflict");
                var existing = reader.GetInt64(1);
                await reader.CloseAsync();
                return await FindAsync(connection, owner, existing, ct);
            }
        }
        var id = await sequence.NextAsync(connection, ct);
        var code = Base62.Encode(id);
        await using (var insert = new NpgsqlCommand("""
            WITH created AS (
                INSERT INTO links(id, code, owner_id, destination_url, created_at) VALUES ($1, $2, $3, $4, $5) RETURNING id
            )
            INSERT INTO creation_requests(owner_id, idempotency_key, url_hash, link_id, expires_at)
            SELECT $3, $6, $7, id, $8 FROM created
            ON CONFLICT(owner_id, idempotency_key) DO UPDATE
                SET url_hash = EXCLUDED.url_hash, link_id = EXCLUDED.link_id, expires_at = EXCLUDED.expires_at;
            """, connection))
        {
            insert.Parameters.AddWithValue(id);
            insert.Parameters.AddWithValue(code);
            insert.Parameters.AddWithValue(owner);
            insert.Parameters.AddWithValue(url);
            insert.Parameters.AddWithValue(now);
            insert.Parameters.AddWithValue(key);
            insert.Parameters.AddWithValue(hash);
            insert.Parameters.AddWithValue(now.AddHours(24));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new LinkResult(id, code, shortOrigin.TrimEnd('/') + "/" + code, url, now, "active");
    }

    public async Task<LinkResult> GetAsync(Guid owner, long id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return await FindAsync(connection, owner, id, ct);
    }

    public async Task<LinkResult> DeactivateAsync(Guid owner, long id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand("""
            UPDATE links SET owner_disabled_at = COALESCE(owner_disabled_at, $3)
            WHERE id = $1 AND owner_id = $2 AND deleted_at IS NULL
            """, connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(owner);
        command.Parameters.AddWithValue(clock.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(ct) == 0) throw new LinkFailure(404, "not_found");
        var link = await FindAsync(connection, owner, id, ct);
        await transaction.CommitAsync(ct);
        return link;
    }

    public async Task<LinkPage> ListAsync(Guid owner, int? requestedLimit, string? cursor, CancellationToken ct)
    {
        var position = cursors.Decode(cursor, owner, requestedLimit);
        var limit = requestedLimit ?? position?.Limit ?? 50;
        if (limit is < 1 or > 100) throw new LinkFailure(400, "invalid_input");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        var boundary = position is null ? "" : " AND (l.created_at, l.id) < ($3, $4)";
        await using var command = new NpgsqlCommand(Projection +
            " WHERE l.owner_id = $1 AND l.deleted_at IS NULL" + boundary + " ORDER BY l.created_at DESC, l.id DESC LIMIT $2", connection);
        command.Parameters.AddWithValue(owner);
        command.Parameters.AddWithValue(limit + 1);
        if (position is not null)
        {
            command.Parameters.AddWithValue(position.CreatedAt);
            command.Parameters.AddWithValue(position.Id);
        }
        await using var reader = await command.ExecuteReaderAsync(ct);
        var links = new List<LinkResult>();
        while (await reader.ReadAsync(ct)) links.Add(Read(reader));
        string? next = null;
        if (links.Count > limit)
        {
            links.RemoveAt(limit);
            var last = links[^1];
            next = cursors.Encode(new LinkPosition(1, owner, "/api/v1/links", "", limit, last.CreatedAt, last.Id,
                position?.ExpiresAt ?? clock.GetUtcNow().AddHours(24)));
        }
        return new LinkPage(links, next);
    }

    private async Task<LinkResult> FindAsync(NpgsqlConnection connection, Guid owner, long id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(Projection + " WHERE l.id = $1 AND l.owner_id = $2 AND l.deleted_at IS NULL", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new LinkFailure(404, "not_found");
        return Read(reader);
    }

    private LinkResult Read(NpgsqlDataReader reader) => new(reader.GetInt64(0), reader.GetString(1),
        shortOrigin.TrimEnd('/') + "/" + reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4));
}
