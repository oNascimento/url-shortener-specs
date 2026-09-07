using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Shortener.Application;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.Authentication.Tests;

public sealed class JwtTests
{
    private static Dictionary<string, string?> Configuration(RSA rsa) => new()
    {
        ["Jwt:Issuer"] = "unit-tests", ["Jwt:Audience"] = "unit-tests", ["Jwt:KeyId"] = "current",
        ["Jwt:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem()
    };

    private static JwtIssuer Issuer(Dictionary<string, string?> config, TimeProvider clock) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(config).Build(), clock);

    [Theory]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    [InlineData("Jwt:KeyId")]
    [InlineData("Jwt:PrivateKeyPem")]
    public void Missing_required_configuration_is_rejected(string missing)
    {
        using var rsa = RSA.Create(2048);
        var config = Configuration(rsa);
        config.Remove(missing);
        Assert.Throws<InvalidOperationException>(() => Issuer(config, new UnitClock()));
    }

    [Fact]
    public void Weak_rsa_key_is_rejected()
    {
        using var rsa = RSA.Create(1024);
        Assert.Throws<InvalidOperationException>(() => Issuer(Configuration(rsa), new UnitClock()));
    }

    [Fact]
    public void Duplicate_key_ids_are_rejected()
    {
        using var rsa = RSA.Create(2048);
        var config = Configuration(rsa);
        config["Jwt:ValidationKeys:0:KeyId"] = "current";
        config["Jwt:ValidationKeys:0:PublicKeyPem"] = rsa.ExportRSAPublicKeyPem();
        Assert.Throws<InvalidOperationException>(() => Issuer(config, new UnitClock()));
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("algorithm")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("kid")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("unsigned")]
    public void Invalid_jwt_is_rejected(string scenario)
    {
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var clock = new UnitClock();
        using var issuer = Issuer(Configuration(rsa), clock);
        var key = new RsaSecurityKey(scenario == "signature" ? other : rsa)
        { KeyId = scenario == "kid" ? "unknown" : "current", CryptoProviderFactory = new() { CacheSignatureProviders = false } };
        var now = clock.GetUtcNow().UtcDateTime;
        var jwt = new JwtSecurityToken(scenario == "issuer" ? "foreign" : "unit-tests", scenario == "audience" ? "foreign" : "unit-tests",
            [new Claim("sub", Guid.NewGuid().ToString())],
            scenario == "expired" ? now.AddMinutes(-20) : scenario == "future" ? now.AddSeconds(31) : now,
            scenario == "expired" ? now.AddSeconds(-31) : now.AddMinutes(15),
            scenario == "unsigned" ? null : new SigningCredentials(key, scenario == "algorithm" ? "RS512" : "RS256"));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token, issuer.ValidationParameters(), out _));
    }

    [Fact]
    public void Issued_token_validates_until_the_lifetime_and_skew_boundary()
    {
        using var rsa = RSA.Create(2048);
        var clock = new UnitClock();
        using var issuer = Issuer(Configuration(rsa), clock);
        var token = issuer.Issue(new AuthSession { Id = Guid.NewGuid() },
            new UserResult(Guid.NewGuid(), "person@example.test", true, "admin", clock.GetUtcNow())).AccessToken;
        clock.Advance(TimeSpan.FromSeconds(929));
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, issuer.ValidationParameters(), out _);
        Assert.Equal("admin", principal.FindFirst("role")?.Value);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token, issuer.ValidationParameters(), out _));
    }

    [Fact]
    public void Published_key_is_accepted_and_retired_key_expires_after_930_seconds()
    {
        using var rsa = RSA.Create(2048);
        using var next = RSA.Create(2048);
        var clock = new UnitClock();
        var config = Configuration(rsa);
        config["Jwt:ValidationKeys:0:KeyId"] = "next";
        config["Jwt:ValidationKeys:0:PublicKeyPem"] = next.ExportRSAPublicKeyPem();
        using var before = Issuer(config, clock);
        var nextConfig = Configuration(next);
        nextConfig["Jwt:KeyId"] = "next";
        using var nextIssuer = Issuer(nextConfig, clock);
        var user = new UserResult(Guid.NewGuid(), "person@example.test", true, "user", clock.GetUtcNow());
        var session = new AuthSession { Id = Guid.NewGuid() };
        var nextToken = nextIssuer.Issue(session, user).AccessToken;
        new JwtSecurityTokenHandler().ValidateToken(nextToken, before.ValidationParameters(), out _);

        nextConfig["Jwt:ValidationKeys:0:KeyId"] = "current";
        nextConfig["Jwt:ValidationKeys:0:PublicKeyPem"] = rsa.ExportRSAPublicKeyPem();
        nextConfig["Jwt:ValidationKeys:0:RetiredAt"] = clock.GetUtcNow().ToString("O");
        using var after = Issuer(nextConfig, clock);
        var oldToken = before.Issue(session, user).AccessToken;
        var parameters = after.ValidationParameters();
        // Disable token lifetime here to isolate retirement of the signing key itself.
        parameters.ValidateLifetime = false;
        parameters.LifetimeValidator = null;
        clock.Advance(TimeSpan.FromSeconds(929));
        new JwtSecurityTokenHandler().ValidateToken(oldToken, parameters, out _);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(oldToken, parameters, out _));
    }

    [Fact]
    public void Recreated_issuer_with_same_key_can_sign_after_previous_instance_disposal()
    {
        using var rsa = RSA.Create(2048);
        var clock = new UnitClock();
        var config = Configuration(rsa);
        var user = new UserResult(Guid.NewGuid(), "person@example.test", true, "user", clock.GetUtcNow());
        var session = new AuthSession { Id = Guid.NewGuid() };
        using (var first = Issuer(config, clock)) first.Issue(session, user);
        using var second = Issuer(config, clock);
        var token = second.Issue(session, user).AccessToken;
        new JwtSecurityTokenHandler().ValidateToken(token, second.ValidationParameters(), out _);
    }

    private sealed class UnitClock : TimeProvider
    {
        private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
