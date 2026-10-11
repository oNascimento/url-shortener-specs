using System.Globalization;
using Npgsql;
using Shortener.Application;
using Shortener.Infrastructure;
using Shortener.LinkManagement.Tests;
using Xunit;

namespace Shortener.Events.Tests;

[CollectionDefinition("links")]
public sealed class EventsLinkCollection : ICollectionFixture<LinkEnvironment>;

[Collection("links")]
[Trait("Category", "Integration")]
public sealed class StoreTests(LinkEnvironment environment)
{
    public static async Task<(Guid Owner, long Link)> LinkAsync(LinkHost host)
    {
        host.App.Clock.Advance(TimeSpan.FromTicks(-host.App.Clock.GetUtcNow().Ticks % 10));
        var (owner, client) = await host.UserAsync();
        client.Dispose();
        var link = await host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test/path", default);
        return (owner, link.Id);
    }

    public static AccessRecorded Event(long link, DateTimeOffset at, string ip = "192.0.2.1", Guid? id = null) =>
        new(1, id ?? Guid.NewGuid(), link.ToString(CultureInfo.InvariantCulture), at, ip);

    [Fact]
    public async Task T11_T12_concurrent_duplicates_commit_replay_and_rollbacks_are_atomic()
    {
        await using var host = await environment.CreateAsync();
        var (_, link) = await LinkAsync(host);
        var now = host.App.Clock.GetUtcNow();
        var events = Enumerable.Range(0, 100).Select(i => Event(link, now.AddTicks(i * 10))).ToArray();
        var store = new AccessBatchStore(host.Primary, host.App.Clock);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => store.PersistAsync(events.Concat(events).ToArray(), default)));
        Assert.Equal(100, results.Sum(x => x.Inserted));
        Assert.Equal(100L, await host.SqlAsync("SELECT count(*) FROM access_events"));
        Assert.Equal(100m, await host.SqlAsync("SELECT sum(count) FROM daily_totals"));
        Assert.Equal(0, (await store.PersistAsync(events, default)).Inserted);
        Assert.Null((await store.PersistAsync([], default)).LastPersistedEventAt);
        var failing = Event(link, now.AddSeconds(1));
        var stripe = Convert.ToInt32(failing.EventId.ToString("N")[..2], 16) % 16;
        await host.SqlAsync("INSERT INTO daily_totals VALUES($1,$2,$3,9223372036854775807,$4) ON CONFLICT(link_id,day_utc,stripe) DO UPDATE SET count=EXCLUDED.count",
            false, link, DateOnly.FromDateTime(now.UtcDateTime), (short)stripe, now);
        await Assert.ThrowsAsync<PostgresException>(() => store.PersistAsync([failing], default));
        Assert.Equal(100L, await host.SqlAsync("SELECT count(*) FROM access_events"));
        await host.SqlAsync("UPDATE daily_totals SET count=0 WHERE stripe=$1", false, (short)stripe);
        Assert.Equal(1, (await store.PersistAsync([failing], default)).Inserted);
        Assert.Equal(now.AddSeconds(1), (await store.PersistAsync([Event(link, now.AddSeconds(2), "2001:db8::1")], default)).LastPersistedEventAt!.Value.AddSeconds(-1));
        Assert.Equal("2001:db8::1", await host.SqlAsync("SELECT host(source_ip) FROM access_events WHERE source_ip <<= '2001:db8::/32'"));
    }

    [Fact]
    public async Task T15_T16_expired_deleted_and_missing_links_are_discarded_but_blocked_links_count()
    {
        await using var host = await environment.CreateAsync();
        var (owner, link) = await LinkAsync(host);
        var (_, tombstone) = await LinkAsync(host);
        var (deletingOwner, deleting) = await LinkAsync(host);
        var now = host.App.Clock.GetUtcNow();
        await host.SqlAsync("UPDATE users SET blocked_at=$2 WHERE id=$1", false, owner, now);
        await host.SqlAsync("UPDATE links SET owner_disabled_at=$2,admin_blocked_at=$2 WHERE id=$1", false, link, now);
        await host.SqlAsync("UPDATE links SET owner_id=NULL,destination_url=NULL,deleted_at=$2 WHERE id=$1", false, tombstone, now);
        await host.SqlAsync("UPDATE users SET deletion_requested_at=$2 WHERE id=$1", false, deletingOwner, now);
        var expired = Event(link, now.AddDays(-90).AddTicks(-10));
        var result = await new AccessBatchStore(host.Primary, host.App.Clock).PersistAsync([
            Event(link, now.AddDays(-90)), expired, Event(tombstone, now), Event(deleting, now), Event(long.MaxValue, now)], default);
        Assert.Equal(1, result.Inserted);
        Assert.Equal(1, result.Expired);
        Assert.Equal(3, result.Ineligible);
        Assert.Equal(1m, await host.SqlAsync("SELECT sum(count) FROM daily_totals"));
        Assert.Equal(0, (await new AccessBatchStore(host.Primary, host.App.Clock).PersistAsync([expired], default)).Inserted);
    }
}
