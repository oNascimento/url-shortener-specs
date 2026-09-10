using System.Globalization;

namespace Shortener.Domain;

public sealed class DestinationRules(IEnumerable<string> shortHosts)
{
    private readonly HashSet<string> forbiddenHosts = shortHosts.Select(NormalizeHost).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public string? Validate(string? input)
    {
        if (input is null) return null;
        // Controls must be rejected even at the edges, before whitespace trimming.
        if (input.Any(char.IsControl) || input.Contains('\\')) return null;
        var value = input.Trim();
        if (value.Length is 0 or > 8192) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.UserInfo.Length != 0) return null;
        // Reject even an empty user-info component (http://@host/).
        var authority = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/', '?', '#')[0];
        if (authority.Contains('@')) return null;
        return forbiddenHosts.Contains(NormalizeHost(uri.IdnHost)) ? null : value;
    }

    private static string NormalizeHost(string host) => new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
}
