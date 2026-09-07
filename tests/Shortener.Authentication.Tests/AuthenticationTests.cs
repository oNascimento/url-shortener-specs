using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Application;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.Authentication.Tests;

public sealed class AuthenticationTests : IAsyncLifetime
{
    private ServiceProvider services = null!;
    private ManualClock clock = null!;
    private AppDbContext db = null!;
    private AuthService auth = null!;

    public async Task InitializeAsync()
    {
        clock = new ManualClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDataProtection();
        collection.AddSingleton<TimeProvider>(clock);
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "tests" }).Build());
        collection.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        collection.AddIdentityCore<ApplicationUser>(o => { o.Password.RequiredLength = 12; o.Password.RequireDigit = false; o.Password.RequireLowercase = false; o.Password.RequireUppercase = false; o.Password.RequireNonAlphanumeric = false; o.User.RequireUniqueEmail = true; }).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services = collection.BuildServiceProvider();
        db = services.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        auth = new AuthService(db, services.GetRequiredService<UserManager<ApplicationUser>>(), clock);
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await services.DisposeAsync(); }

    [Theory]
    [InlineData("short")]
    [InlineData("12345678901")]
    public async Task Password_length_is_enforced(string password)
    {
        Assert.Null(await auth.CreateUserAsync(new RegisterInput("person@example.test", password), CancellationToken.None));
        Assert.Null(await auth.CreateUserAsync(new RegisterInput("long@example.test", new string('x', 129)), CancellationToken.None));
    }

    [Fact]
    public async Task Registration_normalizes_email_and_stores_only_token_hash()
    {
        var created = await auth.CreateUserAsync(new RegisterInput(" Person@Example.Test ", "a password with spaces"), CancellationToken.None);
        Assert.NotNull(created);
        Assert.Equal("Person@Example.Test", created!.Value.User.Email);
        var action = await db.ActionTokens.SingleAsync();
        Assert.NotEqual(created.Value.Token, action.Hash);
        Assert.Equal(44, created.Value.Token.Length);
        Assert.NotNull(await auth.FindUserAsync("person@example.test", CancellationToken.None));
    }

    [Fact]
    public async Task Verification_is_single_use_and_expires_after_24_hours()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("person@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        Assert.True(await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None));
        Assert.False(await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None));
        var expired = await auth.CreateActionToken(created.User, "verify_email", TimeSpan.FromHours(24), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(24));
        Assert.False(await auth.ConsumeActionAsync(expired, "verify_email", null, CancellationToken.None));
    }

    [Fact]
    public async Task Login_creates_independent_30_day_session_only_after_verification()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("person@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        Assert.Null(await auth.LoginAsync(new LoginInput("person@example.test", "a password with spaces"), CancellationToken.None));
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var first = await auth.LoginAsync(new LoginInput(" PERSON@example.test ", "a password with spaces"), CancellationToken.None);
        var second = await auth.LoginAsync(new LoginInput("person@example.test", "a password with spaces"), CancellationToken.None);
        Assert.NotNull(first); Assert.NotNull(second); Assert.NotEqual(first!.Value.Session.Id, second!.Value.Session.Id);
        Assert.Equal(clock.GetUtcNow().AddDays(30), first.Value.Session.ExpiresAt);
    }

    [Fact]
    public async Task Refresh_rotates_once_and_replay_revokes_session()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("person@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var login = (await auth.LoginAsync(new LoginInput("person@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        var refreshed = await auth.RefreshAsync(login.Refresh, CancellationToken.None);
        Assert.NotNull(refreshed); Assert.NotEqual(login.Refresh, refreshed!.Value.Refresh);
        Assert.Null(await auth.RefreshAsync(login.Refresh, CancellationToken.None));
        Assert.Null(await auth.GetUserAsync(created.User.Id, login.Session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Reset_consumes_token_and_revokes_all_sessions()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("person@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var login = (await auth.LoginAsync(new LoginInput("person@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        var reset = await auth.CreateActionToken(created.User, "reset_password", TimeSpan.FromMinutes(30), CancellationToken.None);
        Assert.True(await auth.ConsumeActionAsync(reset, "reset_password", "another password", CancellationToken.None));
        Assert.Null(await auth.GetUserAsync(created.User.Id, login.Session.Id, CancellationToken.None));
        Assert.Null(await auth.LoginAsync(new LoginInput("person@example.test", "a password with spaces"), CancellationToken.None));
        Assert.NotNull(await auth.LoginAsync(new LoginInput("person@example.test", "another password"), CancellationToken.None));
    }

    [Fact]
    public void Jwt_contains_required_claims_and_uses_configured_clock()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        using var issuer = new JwtIssuer(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "tests", ["Jwt:KeyId"] = "unit", ["Jwt:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem() }).Build(), clock);
        var session = new AuthSession { Id = Guid.NewGuid(), ExpiresAt = clock.GetUtcNow().AddDays(30) };
        var user = new UserResult(Guid.NewGuid(), "person@example.test", true, "user", clock.GetUtcNow());
        var result = issuer.Issue(session, user);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal(user.Id.ToString(), token.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal(session.Id.ToString(), token.Claims.Single(c => c.Type == "sid").Value);
        Assert.Equal("user", token.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15).UtcDateTime, token.ValidTo);
    }

    [Fact]
    public async Task Blocked_account_cannot_refresh_an_existing_session()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("blocked@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var login = (await auth.LoginAsync(new LoginInput("blocked@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        created.User.BlockedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        Assert.Null(await auth.RefreshAsync(login.Refresh, CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_reset_password_preserves_password_and_action_token()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("reset@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var token = await auth.CreateActionToken(created.User, "reset_password", TimeSpan.FromMinutes(30), CancellationToken.None);
        Assert.False(await auth.ConsumeActionAsync(token, "reset_password", "short", CancellationToken.None));
        Assert.NotNull(await auth.LoginAsync(new LoginInput("reset@example.test", "a password with spaces"), CancellationToken.None));
        Assert.True(await auth.ConsumeActionAsync(token, "reset_password", "another password", CancellationToken.None));
    }

    [Fact]
    public void Jwt_requires_explicit_key_configuration()
    {
        Assert.Throws<InvalidOperationException>(() => new JwtIssuer(new ConfigurationBuilder().Build(), clock));
    }

    [Fact]
    public void Jwt_uses_configured_key_id_and_issued_at()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "tests", ["Jwt:Audience"] = "tests", ["Jwt:KeyId"] = "rotation-new",
            ["Jwt:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem()
        }).Build();
        var issuer = new JwtIssuer(config, clock);
        var result = issuer.Issue(new AuthSession { Id = Guid.NewGuid() }, new UserResult(Guid.NewGuid(), "person@example.test", true, "user", clock.GetUtcNow()));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        Assert.Equal("rotation-new", jwt.Header.Kid);
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds().ToString(), jwt.Claims.Single(c => c.Type == "iat").Value);
        new JwtSecurityTokenHandler().ValidateToken(result.AccessToken, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = "tests", ValidateAudience = true, ValidAudience = "tests",
            ValidateLifetime = false, IssuerSigningKey = new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa),
            ValidAlgorithms = ["RS256"]
        }, out _);
    }

    [Fact]
    public async Task Expired_session_cannot_refresh_or_access_user()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("expired@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var login = (await auth.LoginAsync(new LoginInput("expired@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        clock.Advance(TimeSpan.FromDays(30));
        Assert.Null(await auth.RefreshAsync(login.Refresh, CancellationToken.None));
        Assert.Null(await auth.GetUserAsync(created.User.Id, login.Session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Resend_issues_a_new_single_use_token_for_an_unverified_account()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("resend@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        var resent = await auth.ResendVerificationAsync(" RESEND@example.test ", CancellationToken.None);
        Assert.NotNull(resent);
        Assert.True(created.Token != resent.Value.Token);
        Assert.Equal(2, await db.ActionTokens.CountAsync());
        Assert.All(await db.ActionTokens.ToListAsync(), token => Assert.Equal(clock.GetUtcNow().AddHours(24), token.ExpiresAt));
        Assert.True(await auth.ConsumeActionAsync(resent.Value.Token, "verify_email", null, CancellationToken.None));
        Assert.False(await auth.ConsumeActionAsync(resent.Value.Token, "verify_email", null, CancellationToken.None));
        Assert.Null(await auth.ResendVerificationAsync("resend@example.test", CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_account_email_actions_do_not_create_tokens()
    {
        Assert.Null(await auth.ResendVerificationAsync("absent@example.test", CancellationToken.None));
        Assert.Null(await auth.CreateResetAsync("absent@example.test", CancellationToken.None));
        Assert.Empty(await db.ActionTokens.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_account_cannot_consume_or_request_email_actions(bool deleting)
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("unavailable@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        if (deleting) created.User.DeletionRequestedAt = clock.GetUtcNow();
        else created.User.BlockedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        Assert.Null(await auth.ResendVerificationAsync("unavailable@example.test", CancellationToken.None));
        Assert.Null(await auth.CreateResetAsync("unavailable@example.test", CancellationToken.None));
        Assert.False(await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None));
        Assert.Null((await db.ActionTokens.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Action_token_cannot_be_used_for_another_purpose()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("purpose@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        Assert.False(await auth.ConsumeActionAsync(created.Token, "reset_password", "another password", CancellationToken.None));
        Assert.True(await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None));
    }

    [Fact]
    public async Task Reset_expires_at_30_minutes_without_changing_password()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("expiry@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var reset = (await auth.CreateResetAsync("expiry@example.test", CancellationToken.None))!.Value;
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.False(await auth.ConsumeActionAsync(reset.Token, "reset_password", "another password", CancellationToken.None));
        Assert.NotNull(await auth.LoginAsync(new LoginInput("expiry@example.test", "a password with spaces"), CancellationToken.None));
    }

    [Fact]
    public async Task Logout_is_idempotent_and_only_revokes_its_session()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("sessions@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var first = (await auth.LoginAsync(new LoginInput("sessions@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        var second = (await auth.LoginAsync(new LoginInput("sessions@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.LogoutAsync(first.Refresh, CancellationToken.None);
        var revoked = first.Session.RevokedAt;
        clock.Advance(TimeSpan.FromSeconds(1));
        await auth.LogoutAsync(first.Refresh, CancellationToken.None);
        await auth.LogoutAsync("unknown", CancellationToken.None);
        Assert.Equal(revoked, first.Session.RevokedAt);
        Assert.Null(await auth.GetUserAsync(created.User.Id, first.Session.Id, CancellationToken.None));
        Assert.Null(await auth.RefreshAsync(first.Refresh, CancellationToken.None));
        Assert.NotNull(await auth.GetUserAsync(created.User.Id, second.Session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_preserves_expiration_and_deleting_account_is_rejected()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("deleting@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var first = (await auth.LoginAsync(new LoginInput("deleting@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        var expires = first.Session.ExpiresAt;
        clock.Advance(TimeSpan.FromDays(29));
        var refreshed = await auth.RefreshAsync(first.Refresh, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.Equal(expires, refreshed.Value.Session.ExpiresAt);
        created.User.DeletionRequestedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        Assert.Null(await auth.RefreshAsync(refreshed.Value.Refresh, CancellationToken.None));
        Assert.Null(await auth.GetUserAsync(created.User.Id, first.Session.Id, CancellationToken.None));
        Assert.Null(await auth.LoginAsync(new LoginInput("deleting@example.test", "a password with spaces"), CancellationToken.None));
    }

    [Fact]
    public async Task Reset_invalidates_all_other_reset_tokens()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("other-tokens@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        var first = (await auth.CreateResetAsync(created.User.Email!, CancellationToken.None))!.Value;
        var second = (await auth.CreateResetAsync(created.User.Email!, CancellationToken.None))!.Value;
        Assert.True(await auth.ConsumeActionAsync(first.Token, "reset_password", "another password", CancellationToken.None));
        Assert.False(await auth.ConsumeActionAsync(second.Token, "reset_password", "third password", CancellationToken.None));
    }

    [Fact]
    public async Task Session_id_cannot_be_used_for_another_user()
    {
        var created = (await auth.CreateUserAsync(new RegisterInput("owner@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        await auth.ConsumeActionAsync(created.Token, "verify_email", null, CancellationToken.None);
        var login = (await auth.LoginAsync(new LoginInput("owner@example.test", "a password with spaces"), CancellationToken.None))!.Value;
        Assert.Null(await auth.GetUserAsync(Guid.NewGuid(), login.Session.Id, CancellationToken.None));
        Assert.Null(await auth.GetUserAsync(created.User.Id, Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class ManualClock(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset current = value;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current = current.Add(amount);
    }
}
