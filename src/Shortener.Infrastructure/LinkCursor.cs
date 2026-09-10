using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed record LinkPosition(int Version, Guid Owner, string Route, string Filters, int Limit,
    DateTimeOffset CreatedAt, long Id, DateTimeOffset ExpiresAt);

public sealed class LinkCursor(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Shortener.Links.Cursor.v1");
    public string Encode(LinkPosition position) => protector.Protect(JsonSerializer.Serialize(position));

    public LinkPosition? Decode(string? token, Guid owner, int? limit)
    {
        if (token is null) return null;
        try
        {
            if (token.Length is 0 or > 2048) throw new FormatException();
            var position = JsonSerializer.Deserialize<LinkPosition>(protector.Unprotect(token));
            if (position is null) throw new FormatException();
            if (position.Version != 1 || position.Owner != owner || position.Route != "/api/v1/links"
                || position.Filters != "" || position.Limit is < 1 or > 100 || position.Id <= 0
                || position.ExpiresAt <= clock.GetUtcNow() || (limit.HasValue && limit.Value != position.Limit))
                throw new FormatException();
            return position;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            throw new LinkFailure(400, "invalid_cursor");
        }
    }
}
