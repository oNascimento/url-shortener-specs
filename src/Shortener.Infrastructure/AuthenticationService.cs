using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        try
        {
            using var client = new SmtpClient(config["Email:Host"] ?? "localhost", int.TryParse(config["Email:Port"], out var port) ? port : 1025)
            { EnableSsl = bool.TryParse(config["Email:UseTls"], out var tls) && tls, Timeout = 5000 };
            if (!string.IsNullOrWhiteSpace(config["Email:Username"])) client.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
            using var message = new MailMessage(config["Email:From"] ?? "no-reply@localhost", recipient, subject, body);
            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException)
        {
            logger.LogError("Email delivery failed ({type})", ex.GetType().Name);
        }
    }
}

public sealed class AuthService(AppDbContext db, UserManager<ApplicationUser> users, TimeProvider clock)
{
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static bool Available(ApplicationUser user) => user.EmailVerifiedAt is not null && user.BlockedAt is null && user.DeletionRequestedAt is null;

    public async Task<UserResult?> FindUserAsync(string email, CancellationToken ct) =>
        await FindUserEntityAsync(email, ct) is { } user ? await ToResultAsync(user) : null;

    public Task<ApplicationUser?> FindUserEntityAsync(string email, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == users.NormalizeEmail(email.Trim()), ct);

    public async Task<UserResult?> GetUserAsync(Guid id, Guid sessionId, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        return user is not null && Available(user) && await db.AuthSessions.AnyAsync(x => x.Id == sessionId && x.UserId == id && x.RevokedAt == null && x.ExpiresAt > clock.GetUtcNow(), ct)
            ? await ToResultAsync(user) : null;
    }

    private async Task<UserResult> ToResultAsync(ApplicationUser user) => new(user.Id, user.Email!, user.EmailVerifiedAt is not null,
        await users.IsInRoleAsync(user, "admin") ? "admin" : "user", user.CreatedAt);

    public async Task<(ApplicationUser User, string Token)?> CreateUserAsync(RegisterInput input, CancellationToken ct)
    {
        if (input.Password.Length is < 12 or > 128) return null;
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = input.Email.Trim(), Email = input.Email.Trim(), CreatedAt = clock.GetUtcNow() };
        try
        {
            if (!(await users.CreateAsync(user, input.Password)).Succeeded) return null;
            var token = await CreateActionToken(user, "verify_email", TimeSpan.FromHours(24), ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return (user, token);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return null;
        }
    }

    public async Task<string> CreateActionToken(ApplicationUser user, string purpose, TimeSpan ttl, CancellationToken ct)
    {
        var token = RandomToken();
        db.ActionTokens.Add(new ActionToken { Hash = Hash(token), UserId = user.Id, Purpose = purpose, ExpiresAt = clock.GetUtcNow().Add(ttl) });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public Task<(ApplicationUser User, string Token)?> ResendVerificationAsync(string email, CancellationToken ct) => CreateEmailActionAsync(email, "verify_email", TimeSpan.FromHours(24), ct);
    public Task<(ApplicationUser User, string Token)?> CreateResetAsync(string email, CancellationToken ct) => CreateEmailActionAsync(email, "reset_password", TimeSpan.FromMinutes(30), ct);

    private async Task<(ApplicationUser User, string Token)?> CreateEmailActionAsync(string email, string purpose, TimeSpan ttl, CancellationToken ct)
    {
        var userId = await db.Users.Where(x => x.NormalizedEmail == users.NormalizeEmail(email.Trim())).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (userId is null) return null;
        return await WithUserLockAsync<(ApplicationUser User, string Token)?>(userId.Value, async () =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
            if (user.BlockedAt is not null || user.DeletionRequestedAt is not null || (purpose == "verify_email" && user.EmailVerifiedAt is not null)) return null;
            return (user, await CreateActionToken(user, purpose, ttl, ct));
        }, ct);
    }

    public async Task<bool> ConsumeActionAsync(string token, string purpose, string? newPassword, CancellationToken ct)
    {
        if (purpose == "reset_password" && (newPassword is null || newPassword.Length is < 12 or > 128)) return false;
        var hash = Hash(token);
        var userId = await db.ActionTokens.Where(x => x.Hash == hash && x.Purpose == purpose).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return false;
        return await WithUserLockAsync(userId.Value, async () =>
        {
            var action = await db.ActionTokens.Include(x => x.User).SingleOrDefaultAsync(x => x.Hash == hash && x.Purpose == purpose, ct);
            if (action is null || action.ConsumedAt is not null || action.ExpiresAt <= clock.GetUtcNow() || action.User.BlockedAt is not null || action.User.DeletionRequestedAt is not null) return false;
            if (purpose == "reset_password")
            {
                foreach (var validator in users.PasswordValidators)
                    if (!(await validator.ValidateAsync(users, action.User, newPassword)).Succeeded) return false;
                action.User.PasswordHash = users.PasswordHasher.HashPassword(action.User, newPassword!);
                action.User.SecurityStamp = Guid.NewGuid().ToString();
                action.User.ConcurrencyStamp = Guid.NewGuid().ToString();
                foreach (var session in await db.AuthSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct)) session.RevokedAt = clock.GetUtcNow();
                foreach (var other in await db.ActionTokens.Where(x => x.UserId == userId && x.Purpose == purpose && x.ConsumedAt == null).ToListAsync(ct)) other.ConsumedAt = clock.GetUtcNow();
            }
            else if (purpose == "verify_email")
            {
                action.User.EmailVerifiedAt = clock.GetUtcNow();
                action.User.EmailConfirmed = true;
            }
            else return false;
            action.ConsumedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<(AuthSession Session, string Refresh, UserResult User)?> LoginAsync(LoginInput input, CancellationToken ct)
    {
        var userId = await db.Users.Where(x => x.NormalizedEmail == users.NormalizeEmail(input.Email.Trim())).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (userId is null) return null;
        return await WithUserLockAsync<(AuthSession Session, string Refresh, UserResult User)?>(userId.Value, async () =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
            if (!await users.CheckPasswordAsync(user, input.Password) || !Available(user)) return null;
            var now = clock.GetUtcNow();
            var session = new AuthSession { Id = Guid.NewGuid(), UserId = user.Id, CreatedAt = now, ExpiresAt = now.AddDays(30) };
            var refresh = RandomToken();
            db.AuthSessions.Add(session);
            db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), Session = session, TokenHash = Hash(refresh), CreatedAt = now, ExpiresAt = session.ExpiresAt });
            await db.SaveChangesAsync(ct);
            return (session, refresh, await ToResultAsync(user));
        }, ct);
    }

    public async Task<(AuthSession Session, string Refresh, UserResult User)?> RefreshAsync(string raw, CancellationToken ct)
    {
        var hash = Hash(raw);
        var userId = await db.RefreshTokens.Where(x => x.TokenHash == hash).Select(x => (Guid?)x.Session.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return null;
        return await WithUserLockAsync<(AuthSession Session, string Refresh, UserResult User)?>(userId.Value, async () =>
        {
            // Lock order is user then session, shared with logout/reset and future deletion jobs.
            if (db.Database.IsRelational())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM auth_sessions WHERE id = (SELECT session_id FROM refresh_tokens WHERE token_hash = {hash}) FOR UPDATE", ct);
            var token = await db.RefreshTokens.Include(x => x.Session).ThenInclude(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
            if (token is null) return null;
            if (token.ConsumedAt is not null)
            {
                token.Session.RevokedAt ??= clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return null;
            }
            if (!Available(token.Session.User) || token.Session.RevokedAt is not null || token.Session.ExpiresAt <= clock.GetUtcNow() || token.ExpiresAt <= clock.GetUtcNow()) return null;
            token.ConsumedAt = clock.GetUtcNow();
            var replacement = RandomToken();
            var next = new RefreshToken { Id = Guid.NewGuid(), SessionId = token.SessionId, TokenHash = Hash(replacement), CreatedAt = clock.GetUtcNow(), ExpiresAt = token.Session.ExpiresAt };
            token.ReplacedBy = next.Id;
            db.RefreshTokens.Add(next);
            await db.SaveChangesAsync(ct);
            return (token.Session, replacement, await ToResultAsync(token.Session.User));
        }, ct);
    }

    public async Task LogoutAsync(string raw, CancellationToken ct)
    {
        var hash = Hash(raw);
        var userId = await db.RefreshTokens.Where(x => x.TokenHash == hash).Select(x => (Guid?)x.Session.UserId).SingleOrDefaultAsync(ct);
        if (userId is null) return;
        await WithUserLockAsync(userId.Value, async () =>
        {
            var token = await db.RefreshTokens.Include(x => x.Session).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
            if (token is not null)
            {
                token.Session.RevokedAt ??= clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }
            return true;
        }, ct);
    }

    private async Task<T> WithUserLockAsync<T>(Guid userId, Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (transaction is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE", ct);
            db.ChangeTracker.Clear();
        }
        var result = await action();
        if (transaction is not null) await transaction.CommitAsync(ct);
        return result;
    }
}
