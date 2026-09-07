using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed class JwtIssuer : IDisposable
{
    private readonly TimeProvider clock;
    private readonly string issuer;
    private readonly string audience;
    private readonly RsaSecurityKey signingKey;
    private readonly CryptoProviderFactory crypto = new() { CacheSignatureProviders = false };
    private readonly List<(RsaSecurityKey Key, DateTimeOffset? RetiredAt)> validationKeys = [];

    public JwtIssuer(IConfiguration config, TimeProvider clock)
    {
        this.clock = clock;
        issuer = Required(config, "Jwt:Issuer");
        audience = Required(config, "Jwt:Audience");
        var keyId = Required(config, "Jwt:KeyId");
        var pem = config["Jwt:PrivateKeyPem"];
        if (string.IsNullOrWhiteSpace(pem))
        {
            var path = Required(config, "Jwt:PrivateKeyPath");
            if (!File.Exists(path) && config.GetValue<bool>("Jwt:GenerateDevelopmentKey")) CreateDevelopmentKey(path);
            pem = File.ReadAllText(path);
        }
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        if (rsa.KeySize < 2048) throw new InvalidOperationException("JWT RSA key must be at least 2048 bits.");
        _ = rsa.ExportParameters(true);
        signingKey = new RsaSecurityKey(rsa) { KeyId = keyId, CryptoProviderFactory = crypto };
        validationKeys.Add((signingKey, null));
        foreach (var entry in config.GetSection("Jwt:ValidationKeys").GetChildren())
        {
            var key = RSA.Create();
            key.ImportFromPem(Required(entry, "PublicKeyPem"));
            if (key.KeySize < 2048) throw new InvalidOperationException("JWT RSA key must be at least 2048 bits.");
            var id = Required(entry, "KeyId");
            if (validationKeys.Any(x => x.Key.KeyId == id)) throw new InvalidOperationException("Duplicate JWT key id.");
            DateTimeOffset? retired = entry["RetiredAt"] is { } value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) : null;
            validationKeys.Add((new RsaSecurityKey(key) { KeyId = id, CryptoProviderFactory = crypto }, retired));
        }
    }

    public AuthResult Issue(AuthSession session, UserResult user)
    {
        var now = clock.GetUtcNow();
        var jwt = new JwtSecurityToken(issuer, audience,
            [new Claim("sub", user.Id.ToString()), new Claim("sid", session.Id.ToString()), new Claim("jti", Guid.NewGuid().ToString()),
             new Claim("iat", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64), new Claim("role", user.Role)],
            now.UtcDateTime, now.AddMinutes(15).UtcDateTime, new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        return new AuthResult(new JwtSecurityTokenHandler().WriteToken(jwt), "Bearer", 900, user);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
        ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub", RoleClaimType = "role",
        CryptoProviderFactory = crypto,
        IssuerSigningKeyResolver = (_, _, kid, _) => validationKeys
            .Where(x => x.Key.KeyId == kid && (x.RetiredAt is null || clock.GetUtcNow() < x.RetiredAt.Value.AddSeconds(930)))
            .Select(x => (SecurityKey)x.Key),
        LifetimeValidator = (notBefore, expires, _, _) => notBefore.HasValue && expires.HasValue && expires > notBefore
            && notBefore.Value <= clock.GetUtcNow().AddSeconds(30).UtcDateTime && expires.Value > clock.GetUtcNow().AddSeconds(-30).UtcDateTime
    };

    private static string Required(IConfiguration config, string name) => !string.IsNullOrWhiteSpace(config[name])
        ? config[name]! : throw new InvalidOperationException($"Required configuration missing: {name}");

    private static void CreateDevelopmentKey(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".tmp");
        try
        {
            using var rsa = RSA.Create(2048);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var writer = new StreamWriter(new FileStream(temporary, options))) writer.Write(rsa.ExportRSAPrivateKeyPem());
            try { File.Move(temporary, path, false); }
            catch (IOException) when (File.Exists(path)) { /* Another development instance won initialization. */ }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose()
    {
        foreach (var item in validationKeys) item.Key.Rsa.Dispose();
    }
}
