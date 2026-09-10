using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Shortener.Infrastructure;

public sealed record RateLimitRule(string Key, int Limit, TimeSpan Window);

public static class AuthRateLimits
{
    private static RateLimitRule Rule(string scope, string identity, int limit, TimeSpan window) =>
        new(scope + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))), limit, window);

    public static RateLimitRule[] ForAuth(string operation, string ip, string? email) => operation switch
    {
        "login" => [Rule("login", ip, 10, TimeSpan.FromMinutes(10))],
        "register" or "resend-verification" or "forgot-password" =>
        [Rule("email-ip", ip, 20, TimeSpan.FromHours(1)), Rule("email", (email ?? "").Trim().ToUpperInvariant(), 3, TimeSpan.FromHours(1))],
        _ => []
    };

    public static RateLimitRule ForUser(Guid user, bool creation = false) => creation
        ? Rule("creation", user.ToString(), 60, TimeSpan.FromMinutes(1))
        : Rule("user", user.ToString(), 300, TimeSpan.FromMinutes(1));
}

public sealed class PostgresRateLimiter(AppDbContext db, TimeProvider clock)
{
    public async Task<int> AcquireAsync(IEnumerable<RateLimitRule> rules, CancellationToken ct)
    {
        var ordered = rules.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0) return 0;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        // Bound cleanup work; rate-limit keys contain hashes, never raw IPs or e-mails.
        await using (var cleanup = new NpgsqlCommand("DELETE FROM auth_rate_limits WHERE key IN (SELECT key FROM auth_rate_limits WHERE expires_at <= $1 LIMIT 100 FOR UPDATE SKIP LOCKED)", connection))
        {
            cleanup.Parameters.AddWithValue(now);
            await cleanup.ExecuteNonQueryAsync(ct);
        }
        var retryAfter = 0;
        foreach (var rule in ordered)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO auth_rate_limits(key, count, expires_at) VALUES ($1, 1, $2)
                ON CONFLICT (key) DO UPDATE SET
                    count = CASE WHEN auth_rate_limits.expires_at <= $3 THEN 1 ELSE LEAST(auth_rate_limits.count + 1, $4 + 1) END,
                    expires_at = CASE WHEN auth_rate_limits.expires_at <= $3 THEN $2 ELSE auth_rate_limits.expires_at END
                RETURNING count, expires_at
                """, connection);
            command.Parameters.AddWithValue(rule.Key);
            command.Parameters.AddWithValue(now.Add(rule.Window));
            command.Parameters.AddWithValue(now);
            command.Parameters.AddWithValue(rule.Limit);
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            if (reader.GetInt32(0) > rule.Limit)
                retryAfter = Math.Max(retryAfter, Math.Max(1, (int)Math.Ceiling((reader.GetFieldValue<DateTimeOffset>(1) - now).TotalSeconds)));
        }
        await transaction.CommitAsync(ct);
        return retryAfter;
    }
}
