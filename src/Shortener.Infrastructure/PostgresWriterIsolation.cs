using Npgsql;
using Shortener.Application;

namespace Shortener.Infrastructure;

// The connection is a read-capable administrative connection to the OLD primary.
// Network unreachability is not evidence of fencing: positively inspect role and live sessions.
public sealed class PostgresWriterIsolation(string oldPrimaryConnection, string writerRole) : IWriterIsolation
{
    public async Task<bool> IsConfirmedAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(oldPrimaryConnection);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM pg_roles WHERE rolname = $1 AND NOT rolcanlogin)
               AND NOT EXISTS(SELECT 1 FROM pg_stat_activity WHERE usename = $1)
            """, connection);
        command.Parameters.AddWithValue(writerRole);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
