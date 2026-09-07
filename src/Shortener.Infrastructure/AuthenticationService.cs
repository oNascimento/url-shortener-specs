using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Shortener.Application;
namespace Shortener.Infrastructure;

public sealed class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        try
        {
            using var client = new SmtpClient(config["Email:Host"] ?? "localhost", int.TryParse(config["Email:Port"], out var port) ? port : 1025) { EnableSsl = bool.TryParse(config["Email:UseTls"], out var tls) && tls, Timeout = 5000 };
            var from = config["Email:From"] ?? "no-reply@localhost";
            using var message = new MailMessage(from, recipient, subject, body);
            if (!string.IsNullOrWhiteSpace(config["Email:Username"])) client.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex) { logger.LogError("Email delivery failed ({type})", ex.GetType().Name); }
    }
}

public sealed class AuthService(AppDbContext db, UserManager<ApplicationUser> users, TimeProvider clock)
{
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public async Task<UserResult?> FindUserAsync(string email, CancellationToken ct) => await db.Set<ApplicationUser>().SingleOrDefaultAsync(x => x.NormalizedEmail == email.Trim().ToUpperInvariant(), ct) is { } u ? ToResult(u) : null;
    public async Task<ApplicationUser?> FindUserEntityAsync(string email, CancellationToken ct) => await db.Set<ApplicationUser>().SingleOrDefaultAsync(x => x.NormalizedEmail == email.Trim().ToUpperInvariant(), ct);
    public async Task<UserResult?> GetUserAsync(Guid id, Guid sessionId, CancellationToken ct) => await db.Set<ApplicationUser>().SingleOrDefaultAsync(x => x.Id == id, ct) is { } u && u.BlockedAt is null && u.DeletionRequestedAt is null && await db.AuthSessions.AnyAsync(x => x.Id == sessionId && x.UserId == id && x.RevokedAt == null && x.ExpiresAt > clock.GetUtcNow(), ct) ? ToResult(u) : null;
    public async Task LogoutAsync(string raw, CancellationToken ct) { var t = await db.RefreshTokens.Include(x => x.Session).SingleOrDefaultAsync(x => x.TokenHash == Hash(raw), ct); if (t?.Session is { } s && s.RevokedAt is null) { s.RevokedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); } }
    public static UserResult ToResult(ApplicationUser u) => new(u.Id, u.Email!, u.EmailVerifiedAt is not null, "user", u.CreatedAt);
    public async Task<(ApplicationUser User, string Token)?> CreateUserAsync(RegisterInput input, CancellationToken ct)
    { if (input.Password.Length is < 12 or > 128) return null; var email = input.Email.Trim(); var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(), CreatedAt = clock.GetUtcNow() }; var r = await users.CreateAsync(user, input.Password); return r.Succeeded ? (user, await CreateActionToken(user, "verify_email", TimeSpan.FromHours(24), ct)) : null; }
    public async Task<string> CreateActionToken(ApplicationUser u, string purpose, TimeSpan ttl, CancellationToken ct) { var token = RandomToken(); db.ActionTokens.Add(new ActionToken { Hash = Hash(token), UserId = u.Id, Purpose = purpose, ExpiresAt = clock.GetUtcNow().Add(ttl) }); await db.SaveChangesAsync(ct); return token; }
    public async Task<bool> ConsumeActionAsync(string token, string purpose, string? newPassword, CancellationToken ct) { var t = await db.ActionTokens.Include(x => x.User).SingleOrDefaultAsync(x => x.Hash == Hash(token) && x.Purpose == purpose, ct); if (t is null || t.ConsumedAt is not null || t.ExpiresAt <= clock.GetUtcNow()) return false; t.ConsumedAt = clock.GetUtcNow(); if (purpose == "verify_email") t.User.EmailVerifiedAt = clock.GetUtcNow(); if (purpose == "reset_password" && newPassword is not null) { await users.RemovePasswordAsync(t.User); await users.AddPasswordAsync(t.User, newPassword); foreach (var session in await db.AuthSessions.Where(x => x.UserId == t.UserId && x.RevokedAt == null).ToListAsync(ct)) session.RevokedAt = clock.GetUtcNow(); foreach (var other in await db.ActionTokens.Where(x => x.UserId == t.UserId && x.Purpose == "reset_password" && x.ConsumedAt == null).ToListAsync(ct)) other.ConsumedAt = clock.GetUtcNow(); } await db.SaveChangesAsync(ct); return true; }
    public async Task<(AuthSession Session, string Refresh, UserResult User)?> LoginAsync(LoginInput input, CancellationToken ct) { var email = input.Email.Trim().ToUpperInvariant(); var user = await db.Set<ApplicationUser>().SingleOrDefaultAsync(x => x.NormalizedEmail == email, ct); var check = user is null ? false : await users.CheckPasswordAsync(user, input.Password); if (user is null || !check || user.EmailVerifiedAt is null || user.BlockedAt is not null || user.DeletionRequestedAt is not null) return null; var now = clock.GetUtcNow(); var session = new AuthSession { Id = Guid.NewGuid(), UserId = user.Id, CreatedAt = now, ExpiresAt = now.AddDays(30) }; var refresh = RandomToken(); db.AuthSessions.Add(session); db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), Session = session, TokenHash = Hash(refresh), CreatedAt = now, ExpiresAt = session.ExpiresAt }); await db.SaveChangesAsync(ct); return (session, refresh, ToResult(user)); }
    public async Task<(AuthSession Session, string Refresh, UserResult User)?> RefreshAsync(string raw, CancellationToken ct) { var token = await db.RefreshTokens.Include(x => x.Session).ThenInclude(x => x.User).SingleOrDefaultAsync(x => x.TokenHash == Hash(raw), ct); if (token is null) return null; if (token.ConsumedAt is not null) { token.Session.RevokedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return null; } if (token.Session.RevokedAt is not null || token.ExpiresAt <= clock.GetUtcNow()) return null; token.ConsumedAt = clock.GetUtcNow(); var replacement = RandomToken(); var next = new RefreshToken { Id = Guid.NewGuid(), SessionId = token.SessionId, TokenHash = Hash(replacement), CreatedAt = clock.GetUtcNow(), ExpiresAt = token.ExpiresAt }; token.ReplacedBy = next.Id; db.RefreshTokens.Add(next); await db.SaveChangesAsync(ct); return (token.Session, replacement, ToResult(token.Session.User)); }
}

public sealed class JwtIssuer(IConfiguration config, TimeProvider clock)
{
    private static readonly RSA Rsa = RSA.Create(2048);
    public static SecurityKey SigningKey => new RsaSecurityKey(Rsa) { KeyId = "dev" };
    public AuthResult Issue(AuthSession session, UserResult user) { var now = clock.GetUtcNow(); var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256); var jwt = new JwtSecurityToken(config["Jwt:Issuer"] ?? "shortener", config["Jwt:Audience"] ?? "shortener", [new Claim("sub", user.Id.ToString()), new Claim("sid", session.Id.ToString()), new Claim("jti", Guid.NewGuid().ToString()), new Claim("role", user.Role)], now.UtcDateTime, now.AddMinutes(15).UtcDateTime, creds); return new AuthResult(new JwtSecurityTokenHandler().WriteToken(jwt), "Bearer", 900, user); }
}
