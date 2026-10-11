using System.Globalization;
using System.Net;
using System.Text.Json;
using Shortener.Application;

namespace Shortener.Infrastructure;

public sealed class InvalidAccess : Exception;

public static class AccessEnvelope
{
    public static AccessRecorded Parse(ReadOnlyMemory<byte> body, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 5
                || root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidAccess();
            var id = root.GetProperty("eventId").GetGuid();
            var link = root.GetProperty("linkId").GetString()!;
            var instant = root.GetProperty("occurredAt").GetString()!;
            var source = root.GetProperty("sourceIp").GetString()!;
            if (id == Guid.Empty || id.ToString("D")[14] != '4'
                || !long.TryParse(link, NumberStyles.None, CultureInfo.InvariantCulture, out var linkId)
                || linkId <= 0 || link != linkId.ToString(CultureInfo.InvariantCulture)
                || !DateTimeOffset.TryParseExact(instant, "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var occurred)
                || occurred > now.AddMinutes(5) || !IPAddress.TryParse(source, out var ip)) throw new InvalidAccess();
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return new(1, id, link, occurred, ip.ToString());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new InvalidAccess();
        }
    }
}
