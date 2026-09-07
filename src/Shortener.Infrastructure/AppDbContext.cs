using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
namespace Shortener.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, Microsoft.AspNetCore.Identity.IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ActionToken> ActionTokens => Set<ActionToken>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ApplicationUser>(e => { e.ToTable("users"); e.HasIndex(x => x.NormalizedEmail).IsUnique(); e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.EmailVerifiedAt).HasColumnName("email_verified_at"); e.Property(x => x.BlockedAt).HasColumnName("blocked_at"); e.Property(x => x.DeletionRequestedAt).HasColumnName("deletion_requested_at"); });
        b.Entity<AuthSession>(e => { e.ToTable("auth_sessions"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId); e.HasOne(x => x.User).WithMany(x => x.Sessions).HasForeignKey(x => x.UserId); });
        b.Entity<RefreshToken>(e => { e.ToTable("refresh_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique(); e.HasOne(x => x.Session).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.SessionId); });
        b.Entity<ActionToken>(e => { e.ToTable("action_tokens"); e.HasKey(x => x.Hash); e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId); });
        b.Entity<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>().ToTable("roles");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(System.Text.RegularExpressions.Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
