using System.Text;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.Events.Tests;

public sealed class EnvelopeTests
{
    public static byte[] Body(Guid id, long link, DateTimeOffset at, string ip = "192.0.2.1") =>
        Encoding.UTF8.GetBytes($$"""{"schemaVersion":1,"eventId":"{{id}}","linkId":"{{link}}","occurredAt":"{{at.UtcDateTime:yyyy-MM-ddTHH:mm:ss.ffffffZ}}","sourceIp":"{{ip}}"}""");

    [Fact]
    public void T10_envelope_is_strict_preserves_identity_microseconds_and_normalizes_ip()
    {
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero).AddTicks(1234560);
        var id = Guid.NewGuid();
        var json = Encoding.UTF8.GetString(Body(id, 9007199254740993, now));
        var parsed = AccessEnvelope.Parse(Encoding.UTF8.GetBytes(json), now);
        Assert.Equal(id, parsed.EventId);
        Assert.Equal(now, parsed.OccurredAt);
        Assert.Equal("9007199254740993", parsed.LinkId);
        Assert.Equal(parsed, AccessEnvelope.Parse(Encoding.UTF8.GetBytes(json), now));
        Assert.Equal("192.0.2.1", AccessEnvelope.Parse(Body(id, 1, now, "::ffff:192.0.2.1"), now).SourceIp);
        Assert.Equal("2001:db8::1", AccessEnvelope.Parse(Body(id, 1, now, "2001:db8::1"), now).SourceIp);
        Assert.NotNull(AccessEnvelope.Parse(Body(id, 1, now.AddMinutes(5)), now));
        foreach (var invalid in new[] { "{", "[]", "{}", json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            json.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""), json.Replace(id.ToString(), Guid.Empty.ToString()),
            json.Replace(id.ToString(), "not-uuid"), json.Replace(id.ToString(), "00000000-0000-1000-8000-000000000001"),
            json.Replace("9007199254740993", "0"), json.Replace("9007199254740993", "01"), json.Replace("9007199254740993", "9223372036854775808"),
            json.Replace("192.0.2.1", "invalid"), json.Replace("192.0.2.1", ""), json.Replace("192.0.2.1", "localhost"),
            json.Replace(".123456Z", "Z"), json.Replace(".123456Z", ".123456+01:00"), json.Replace("sourceIp", "other"),
            json.Replace("\"sourceIp\":\"192.0.2.1\"", "\"sourceIp\":null"),
            json.Replace("\"linkId\":\"9007199254740993\"", "\"linkId\":5"),
            Encoding.UTF8.GetString(Body(id, 1, now.AddMinutes(5).AddTicks(10))) })
            Assert.Throws<InvalidAccess>(() => AccessEnvelope.Parse(Encoding.UTF8.GetBytes(invalid), now));
    }
}
