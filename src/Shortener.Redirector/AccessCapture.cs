using System.Globalization;
using System.Text.Json;
using Shortener.Application;

namespace Shortener.Redirector;

public sealed class AccessCapture(TimeProvider clock, ILogger<AccessCapture> logger, IAccessPublisher? publisher = null)
{
    public async Task CaptureAsync(HttpContext context, ResolvedLink link)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
        {
            RedirectTelemetry.CaptureFailed("missing_ip");
            logger.LogWarning("Access capture unavailable ({reason})", "missing_ip");
            return;
        }
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var ticks = clock.GetUtcNow().UtcTicks;
        var envelope = new AccessRecorded(1, Guid.NewGuid(), link.Id.ToString(CultureInfo.InvariantCulture),
            new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero), ip.ToString());
        var started = clock.GetTimestamp();
        try
        {
            if (publisher is null)
            {
                RedirectTelemetry.CaptureFailed("publisher_unavailable");
                logger.LogWarning("Access capture unavailable ({reason})", "publisher_unavailable");
                return;
            }
            var outcome = await publisher.PublishAsync(envelope, context.RequestAborted);
            RedirectTelemetry.Published(outcome, clock.GetElapsedTime(started).TotalSeconds);
        }
        catch (Exception)
        {
            RedirectTelemetry.CaptureFailed("unexpected");
            RedirectTelemetry.Published(PublishOutcome.Unknown, clock.GetElapsedTime(started).TotalSeconds);
            logger.LogWarning("Access capture failed ({errorClass})", "unexpected");
        }
    }
}

public static class AccessEnvelopeSerializer
{
    public static byte[] Serialize(AccessRecorded envelope) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = envelope.SchemaVersion,
        eventId = envelope.EventId,
        linkId = envelope.LinkId,
        occurredAt = envelope.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture),
        sourceIp = envelope.SourceIp
    });
}
