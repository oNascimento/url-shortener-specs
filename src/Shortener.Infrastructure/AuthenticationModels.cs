using Microsoft.AspNetCore.Identity;
namespace Shortener.Infrastructure;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public DateTimeOffset? DeletionRequestedAt { get; set; }
    public ICollection<AuthSession> Sessions { get; set; } = new List<AuthSession>();
}
public sealed class AuthSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public AuthSession Session { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? ReplacedBy { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class ActionToken
{
    public string Hash { get; set; } = null!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}
