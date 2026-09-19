using Npgsql;

namespace Shortener.Redirector;

public sealed record ResolvedLink(long Id, string? Destination, bool Unavailable);

public interface IRedirectResolver
{
    Task<ResolvedLink?> ResolveAsync(string code, CancellationToken cancellationToken);
}

public sealed class RedirectResolver(IConfiguration configuration) : IRedirectResolver
{
    public async Task<ResolvedLink?> ResolveAsync(string code, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Primary"));
        await connection.OpenAsync(budget.Token);
        // LEFT JOIN preserves tombstones whose account and destination have been removed.
        await using var command = new NpgsqlCommand("""
            SELECT l.id, l.destination_url,
                   l.owner_disabled_at IS NOT NULL OR l.admin_blocked_at IS NOT NULL
                   OR l.deleted_at IS NOT NULL OR u.id IS NULL
                   OR u.blocked_at IS NOT NULL OR u.deletion_requested_at IS NOT NULL
            FROM links l LEFT JOIN users u ON u.id = l.owner_id WHERE l.code = $1
            """, connection) { CommandTimeout = 2 };
        command.Parameters.AddWithValue(code);
        await using var reader = await command.ExecuteReaderAsync(budget.Token);
        if (!await reader.ReadAsync(budget.Token)) return null;
        return new ResolvedLink(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2));
    }
}
