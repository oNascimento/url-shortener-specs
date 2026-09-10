using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Shortener.Application;
using Shortener.Authentication.Tests;
using Shortener.Domain;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.LinkManagement.Tests;

public sealed class DomainTests
{
    [Theory]
    [InlineData(1, "1")][InlineData(9, "9")][InlineData(10, "a")][InlineData(35, "z")]
    [InlineData(36, "A")][InlineData(61, "Z")][InlineData(62, "10")]
    [InlineData(3843, "ZZ")][InlineData(3844, "100")]
    [InlineData(56800235583, "ZZZZZZ")][InlineData(56800235584, "1000000")]
    [InlineData(long.MaxValue, "aZl8N0y58M7")]
    [Trait("Requirement", "T01,T22,RF-17,RF-18")]
    public void Base62_vectors(long id, string expected) => Assert.Equal(expected, Base62.Encode(id));

    [Theory][InlineData(0)][InlineData(-1)][InlineData(long.MinValue)]
    public void Base62_rejects_nonpositive(long id) => Assert.Throws<ArgumentOutOfRangeException>(() => Base62.Encode(id));

    [Theory]
    [InlineData(" https://Example.test/Case/%2F?a=1&a=2#Fragment ")]
    [InlineData("HTTP://example.test")][InlineData("https://例え.test/path")]
    [InlineData("http://127.0.0.1:1/no-fetch")][InlineData("http://[::1]/")]
    [Trait("Requirement", "T08,RF-06,RF-09")]
    public void Valid_urls_are_preserved(string url) => Assert.Equal(url.Trim(), Rules().Validate(url));

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("   ")][InlineData("relative")]
    [InlineData("ftp://example.test")][InlineData("https://")][InlineData("https:example.test")]
    [InlineData("https://a:b@example.test")][InlineData("https://@example.test")]
    [InlineData("https://example.test/\r\nfoo")][InlineData("\thttps://example.test")]
    [InlineData("https://example.test/\\foo")][InlineData("https://s.test")]
    [InlineData("HTTP://S.TEST.:999/x")][InlineData("https://alias.test/")]
    [InlineData("https://xn--bcher-kva.test/")][InlineData("https://BÜCHER.test./")]
    [Trait("Requirement", "T08,RF-06")]
    public void Forbidden_urls_are_rejected(string? url) => Assert.Null(Rules().Validate(url));

    [Fact]
    public void Url_length_boundary()
    {
        var url = "https://example.test/";
        url = url.PadRight(8192, 'a');
        Assert.Equal(url, Rules().Validate(url));
        Assert.Null(Rules().Validate(url + "a"));
    }

    [Fact]
    [Trait("Requirement", "T17,RF-08")]
    public void Cursor_binding_and_expiration()
    {
        var provider = new EphemeralDataProtectionProvider();
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        var cursors = new LinkCursor(provider, clock);
        var owner = Guid.NewGuid();
        var valid = new LinkPosition(1, owner, "/api/v1/links", "", 50, clock.GetUtcNow(), 1, clock.GetUtcNow().AddHours(24));
        Assert.Null(cursors.Decode(null, owner, null));
        var token = cursors.Encode(valid);
        Assert.Equal(valid, cursors.Decode(token, owner, null));
        Assert.Equal(valid, new LinkCursor(provider, clock).Decode(token, owner, 50));
        Assert.Equal("invalid_cursor", Assert.Throws<LinkFailure>(() => cursors.Decode(token, owner, 100)).Code);
        foreach (var invalid in new[] { valid with { Version = 2 }, valid with { Owner = Guid.NewGuid() },
            valid with { Route = "/elsewhere" }, valid with { Filters = "changed" }, valid with { Limit = 0 },
            valid with { Limit = 101 }, valid with { Id = 0 }, valid with { ExpiresAt = clock.GetUtcNow() } })
            Assert.Throws<LinkFailure>(() => cursors.Decode(cursors.Encode(invalid), owner, null));
        foreach (var invalid in new[] { "", new string('a', 2049), "tampered" })
            Assert.Throws<LinkFailure>(() => cursors.Decode(invalid, owner, null));
        var protector = provider.CreateProtector("Shortener.Links.Cursor.v1");
        foreach (var json in new[] { "null", "{", "[]" })
            Assert.Throws<LinkFailure>(() => cursors.Decode(protector.Protect(json), owner, null));
        clock.Advance(TimeSpan.FromHours(24));
        Assert.Throws<LinkFailure>(() => cursors.Decode(token, owner, null));
    }

    private static DestinationRules Rules() => new(["s.test", "alias.test", "bücher.test"]);
}
