using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Shortener.Application;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.Authentication.Tests;

[Trait("Category", "Integration")]
public sealed class AuthenticationIntegrationTests(AuthenticationEnvironment environment) : IClassFixture<AuthenticationEnvironment>
{
    private const string Password = "a password with spaces";
    private static string NewEmail() => Guid.NewGuid().ToString("N") + "@example.test";

    private static async Task<string> Register(AuthenticationHost host, AuthBrowser browser, bool verify = true)
    {
        var email = NewEmail();
        using var response = await browser.Post("register", new RegisterInput(email, Password));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var token = await host.EmailToken(email, "verify-email");
        if (verify)
        {
            using var confirmation = await browser.Post("verify-email", new ActionTokenInput(token));
            Assert.Equal(HttpStatusCode.NoContent, confirmation.StatusCode);
        }
        return email;
    }

    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var name in new[] { "type", "title", "status", "code", "traceId" }) Assert.True(body.TryGetProperty(name, out _), "Missing Problem Details property: " + name);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Http_lifecycle_uses_real_email_secure_cookies_and_immediate_logout()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        var email = await Register(host, browser, verify: false);
        var token = await host.EmailToken(email, "verify-email");
        using var scanner = await browser.Send(HttpMethod.Get, "/verify-email#token=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.NotFound, scanner.StatusCode);
        using var verify = await browser.Post("verify-email", new ActionTokenInput(token));
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        using var duplicate = await browser.Post("verify-email", new ActionTokenInput(token));
        await Problem(duplicate, HttpStatusCode.BadRequest);
        using var login = await browser.Post("login", new LoginInput(email, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(login.Headers.CacheControl?.NoStore);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Secure-refresh="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "accessToken", "expiresIn", "tokenType", "user" }, payload.EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.Equal(900, payload.GetProperty("expiresIn").GetInt32());
        browser.AccessToken = payload.GetProperty("accessToken").GetString();
        using var me = await browser.Me();
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var user = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "createdAt", "email", "emailVerified", "id", "role" }, user.EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.True(Guid.TryParse(user.GetProperty("id").GetString(), out _));
        Assert.True(await host.WithDatabase(db => db.RefreshTokens.AllAsync(x => x.TokenHash.Length == 64)));
        using var logout = await browser.Send(HttpMethod.Post, "/api/v1/auth/logout", bearer: browser.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("path=/api/v1/auth", logout.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
        using var revoked = await browser.Me();
        await Problem(revoked, HttpStatusCode.Unauthorized);
        using var again = await browser.Post("logout");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var logged = string.Join('\n', host.Logs.Messages);
        foreach (var value in new[] { email, Password, token, browser.AccessToken!, "__Secure-refresh" }) Assert.DoesNotContain(value, logged);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("verify-email")]
    [InlineData("resend-verification")]
    [InlineData("login")]
    [InlineData("refresh")]
    [InlineData("logout")]
    [InlineData("forgot-password")]
    [InlineData("reset-password")]
    public async Task Every_auth_post_rejects_missing_csrf_and_missing_or_foreign_origin(string operation)
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        using var missing = await browser.Send(HttpMethod.Post, "/api/v1/auth/" + operation, csrf: false);
        await Problem(missing, HttpStatusCode.Forbidden);
        using var foreign = await browser.Send(HttpMethod.Post, "/api/v1/auth/" + operation, origin: "https://evil.test");
        await Problem(foreign, HttpStatusCode.Forbidden);
        using var absent = await browser.Send(HttpMethod.Post, "/api/v1/auth/" + operation, origin: null);
        await Problem(absent, HttpStatusCode.Forbidden);
        Assert.Equal(0, await host.WithDatabase(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Resend_produces_usable_token_and_unverified_login_does_not_enumerate_accounts()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        var email = await Register(host, browser, verify: false);
        using var unverified = await browser.Post("login", new LoginInput(email, Password));
        using var absent = await browser.Post("login", new LoginInput(NewEmail(), Password));
        await Problem(unverified, HttpStatusCode.Unauthorized);
        await Problem(absent, HttpStatusCode.Unauthorized);
        using var resend = await browser.Post("resend-verification", new EmailInput(email));
        using var unknown = await browser.Post("resend-verification", new EmailInput(NewEmail()));
        Assert.Equal(await resend.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(2, await host.WithDatabase(db => db.ActionTokens.CountAsync()));
        using var verify = await browser.Post("verify-email", new ActionTokenInput(await host.EmailToken(email, "verify-email")));
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        await browser.Login(email);
    }

    [Fact]
    public async Task Concurrent_refresh_across_replicas_rotates_once_and_replay_revokes_family()
    {
        await using var host = await environment.CreateHostAsync();
        await using var replica = host.Replica();
        using var first = await host.Browser();
        using var second = await replica.Browser();
        var email = await Register(host, first);
        await first.Login(email);
        second.ImportSession(first);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Refresh(AuthBrowser browser) { await start.Task; return await browser.Post("refresh"); }
        var requests = new[] { Refresh(first), Refresh(second) };
        start.SetResult();
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(2, await host.WithDatabase(db => db.RefreshTokens.CountAsync()));
        Assert.True(await host.WithDatabase(db => db.AuthSessions.AllAsync(x => x.RevokedAt != null)));
        foreach (var response in responses) response.Dispose();
        using var me = await first.Me();
        await Problem(me, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_is_atomic_single_use_and_revokes_every_session_and_other_reset_token()
    {
        await using var host = await environment.CreateHostAsync();
        using var first = await host.Browser();
        using var second = await host.Browser();
        var email = await Register(host, first);
        await first.Login(email);
        await second.Login(email);
        using var forgot = await first.Post("forgot-password", new EmailInput(email));
        var oldReset = await host.EmailToken(email, "reset-password");
        using var forgotAgain = await first.Post("forgot-password", new EmailInput(email));
        var reset = await host.EmailToken(email, "reset-password");
        var responses = await Task.WhenAll(first.Post("reset-password", new ResetPasswordInput(reset, "another password")),
            second.Post("reset-password", new ResetPasswordInput(reset, "another password")));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in responses) response.Dispose();
        using var old = await first.Post("reset-password", new ResetPasswordInput(oldReset, "third password"));
        await Problem(old, HttpStatusCode.BadRequest);
        using var me = await first.Me();
        using var other = await second.Me();
        await Problem(me, HttpStatusCode.Unauthorized);
        await Problem(other, HttpStatusCode.Unauthorized);
        using var wrong = await first.Post("login", new LoginInput(email, Password));
        await Problem(wrong, HttpStatusCode.Unauthorized);
        await first.Login(email, "another password");
    }

    [Fact]
    public async Task Expiration_uses_injected_clock_and_session_does_not_slide()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        var email = await Register(host, browser, verify: false);
        var verification = await host.EmailToken(email, "verify-email");
        host.Clock.Advance(TimeSpan.FromHours(24));
        using var expired = await browser.Post("verify-email", new ActionTokenInput(verification));
        await Problem(expired, HttpStatusCode.BadRequest);
        using var resend = await browser.Post("resend-verification", new EmailInput(email));
        using var valid = await browser.Post("verify-email", new ActionTokenInput(await host.EmailToken(email, "verify-email")));
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        await browser.Login(email);
        var expires = await host.WithDatabase(db => db.AuthSessions.Select(x => x.ExpiresAt).SingleAsync());
        using var forgot = await browser.Post("forgot-password", new EmailInput(email));
        var reset = await host.EmailToken(email, "reset-password");
        host.Clock.Advance(TimeSpan.FromMinutes(30));
        using var expiredReset = await browser.Post("reset-password", new ResetPasswordInput(reset, "another password"));
        await Problem(expiredReset, HttpStatusCode.BadRequest);
        using var refresh = await browser.Post("refresh");
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(expires, await host.WithDatabase(db => db.AuthSessions.Select(x => x.ExpiresAt).SingleAsync()));
        host.Clock.Advance(TimeSpan.FromDays(30));
        using var expiredSession = await browser.Post("refresh");
        await Problem(expiredSession, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rate_limits_are_shared_across_replicas_and_ignore_untrusted_forwarded_ip()
    {
        await using var host = await environment.CreateHostAsync();
        await using var replica = host.Replica();
        using var first = await host.Browser();
        using var second = await replica.Browser();
        for (var i = 0; i < 10; i++)
        {
            using var response = await (i % 2 == 0 ? first : second).Send(HttpMethod.Post, "/api/v1/auth/login", new LoginInput(NewEmail(), Password), forwardedIp: "192.0.2." + (i + 1));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var limited = await second.Post("login", new LoginInput(NewEmail(), Password));
        await Problem(limited, HttpStatusCode.TooManyRequests);
        Assert.Equal(600, limited.Headers.RetryAfter?.Delta?.TotalSeconds);
        host.Clock.Advance(TimeSpan.FromMinutes(10));
        using var resumed = await first.Post("login", new LoginInput(NewEmail(), Password));
        Assert.Equal(HttpStatusCode.Unauthorized, resumed.StatusCode);
        var address = NewEmail();
        using var one = await first.Post("forgot-password", new EmailInput(address));
        using var two = await second.Post("resend-verification", new EmailInput(address.ToUpperInvariant()));
        using var three = await first.Post("forgot-password", new EmailInput(address));
        using var four = await second.Post("resend-verification", new EmailInput(address));
        Assert.Equal(HttpStatusCode.Accepted, three.StatusCode);
        await Problem(four, HttpStatusCode.TooManyRequests);
        Assert.Equal(3600, four.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("signature")]
    [InlineData("algorithm")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("kid")]
    public async Task Jwt_validation_rejects_invalid_tokens(string scenario)
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        var email = await Register(host, browser);
        var login = await browser.Login(email);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        using var rsa = RSA.Create(2048);
        if (scenario != "signature") rsa.ImportFromPem(host.Settings["Jwt:PrivateKeyPem"]);
        var key = new RsaSecurityKey(rsa)
        { KeyId = scenario == "kid" ? "unknown" : "integration-key", CryptoProviderFactory = new() { CacheSignatureProviders = false } };
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var claims = original.Claims.Where(x => x.Type is not ("iss" or "aud" or "nbf" or "exp"));
        var jwt = new JwtSecurityToken(scenario == "issuer" ? "wrong" : "integration", scenario == "audience" ? "wrong" : "integration", claims,
            scenario == "expired" ? now.AddMinutes(-20) : scenario == "future" ? now.AddMinutes(2) : now,
            scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(15),
            new SigningCredentials(key, scenario == "algorithm" ? "RS512" : "RS256"));
        browser.AccessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        using var response = await browser.Me();
        await Problem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Key_rotation_accepts_previous_key_only_during_ttl_plus_skew()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        var email = await Register(host, browser);
        await browser.Login(email);
        using var oldRsa = RSA.Create();
        oldRsa.ImportFromPem(host.Settings["Jwt:PrivateKeyPem"]);
        using var newRsa = RSA.Create(2048);
        await using var replica = host.Replica(new Dictionary<string, string?>
        {
            ["Jwt:KeyId"] = "rotated", ["Jwt:PrivateKeyPem"] = newRsa.ExportRSAPrivateKeyPem(),
            ["Jwt:ValidationKeys:0:KeyId"] = "integration-key", ["Jwt:ValidationKeys:0:PublicKeyPem"] = oldRsa.ExportRSAPublicKeyPem(),
            ["Jwt:ValidationKeys:0:RetiredAt"] = host.Clock.GetUtcNow().ToString("O")
        });
        using var rotated = await replica.Browser();
        rotated.ImportSession(browser);
        using var accepted = await rotated.Me();
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        host.Clock.Advance(TimeSpan.FromSeconds(931));
        var parameters = replica.Services.GetService(typeof(JwtIssuer)) as JwtIssuer;
        Assert.Empty(parameters!.ValidationParameters().IssuerSigningKeyResolver!("", null!, "integration-key", null!));
        using var expired = await rotated.Me();
        await Problem(expired, HttpStatusCode.Unauthorized);
        using var renewed = await rotated.Post("refresh");
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var payload = await renewed.Content.ReadFromJsonAsync<AuthResult>();
        Assert.Equal("rotated", new JwtSecurityTokenHandler().ReadJwtToken(payload!.AccessToken).Header.Kid);
    }

    [Fact]
    public async Task Invalid_request_returns_problem_without_creating_user()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        foreach (var input in new object[] { new { email = "not-email", password = Password }, new { email = NewEmail(), password = "short" },
            new { email = NewEmail(), password = (string?)null }, new { email = NewEmail(), password = Password, role = "admin" } })
        {
            using var response = await browser.Post("register", input);
            await Problem(response, HttpStatusCode.BadRequest);
        }
        using var cookieOnly = await browser.Me();
        await Problem(cookieOnly, HttpStatusCode.Unauthorized);
        Assert.Equal(0, await host.WithDatabase(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Current_account_state_is_enforced_for_jwt_refresh_and_login()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        var email = await Register(host, browser);
        var login = await browser.Login(email);
        await host.WithDatabase(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == login.User.Id);
            user.BlockedAt = host.Clock.GetUtcNow();
            await db.SaveChangesAsync();
            return true;
        });
        using var me = await browser.Me();
        using var refresh = await browser.Post("refresh");
        using var blocked = await browser.Post("login", new LoginInput(email, Password));
        await Problem(me, HttpStatusCode.Unauthorized);
        await Problem(refresh, HttpStatusCode.Unauthorized);
        await Problem(blocked, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_request_budget_is_atomic_across_replicas()
    {
        await using var host = await environment.CreateHostAsync();
        await using var replica = host.Replica();
        using var first = await host.Browser();
        using var second = await replica.Browser();
        await first.Login(await Register(host, first));
        second.ImportSession(first);
        var statuses = new System.Collections.Concurrent.ConcurrentBag<HttpStatusCode>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 301), new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (i, _) =>
        {
            using var response = await (i % 2 == 0 ? first : second).Me();
            statuses.Add(response.StatusCode);
        });
        Assert.Equal(300, statuses.Count(x => x == HttpStatusCode.OK));
        Assert.Single(statuses, x => x == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Email_ip_budget_covers_different_recipients()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        for (var i = 0; i < 20; i++)
        {
            using var accepted = await browser.Post("forgot-password", new EmailInput(NewEmail()));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }
        using var limited = await browser.Post("forgot-password", new EmailInput(NewEmail()));
        await Problem(limited, HttpStatusCode.TooManyRequests);
        Assert.Equal(3600, limited.Headers.RetryAfter?.Delta?.TotalSeconds);
    }

    [Fact]
    public async Task Database_failure_returns_service_unavailable_including_session_validation()
    {
        await using var host = await environment.CreateHostAsync();
        using var browser = await host.Browser();
        await browser.Login(await Register(host, browser));
        await using var unavailable = host.Replica(new Dictionary<string, string?>
        { ["ConnectionStrings:Primary"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1;Command Timeout=1;GSS Encryption Mode=Disable" });
        using var client = await unavailable.Browser();
        client.ImportSession(browser);
        using var me = await client.Me();
        await Problem(me, HttpStatusCode.ServiceUnavailable);
        using var register = await client.Post("register", new RegisterInput(NewEmail(), Password));
        await Problem(register, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Migrations_support_upgrade_from_authentication_and_reapplication()
    {
        await using var host = await environment.CreateHostAsync();
        await host.WithDatabase(async db =>
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            // Legacy migration IDs have twelve timestamp digits; use EF's resolved name.
            var ids = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsIdGenerator>(db);
            await migrator.MigrateAsync(ids.GetName("202609070001_Authentication"));
            await db.Database.MigrateAsync();
            await db.Database.MigrateAsync();
            return true;
        });
        using var browser = await host.Browser();
        await browser.Login(await Register(host, browser));
    }
}
