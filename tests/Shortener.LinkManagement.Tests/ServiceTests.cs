using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shortener.Application;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.LinkManagement.Tests;

[Collection("links")]
[Trait("Category", "Integration")]
public sealed class ServiceTests(LinkEnvironment environment)
{
    [Fact]
    [Trait("Requirement", "T08,RF-06")]
    public async Task Destination_validation_never_contacts_the_target()
    {
        await using var host = await environment.CreateAsync();
        var (owner, _) = await host.UserAsync();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/no-preview";
        Assert.Equal(url, (await host.Service.CreateAsync(owner, Guid.NewGuid(), url, default)).DestinationUrl);
        Assert.False(listener.Pending());
        var nonexistent = "https://" + Guid.NewGuid().ToString("N") + ".invalid/";
        Assert.Equal(nonexistent, (await host.Service.CreateAsync(owner, Guid.NewGuid(), nonexistent, default)).DestinationUrl);
    }

    [Fact]
    [Trait("Requirement", "T03,RF-07,RF-09,RF-10")]
    public async Task Idempotency_concurrency_conflict_expiration_and_current_state()
    {
        await using var host = await environment.CreateAsync();
        var (owner, _) = await host.UserAsync();
        var (other, _) = await host.UserAsync();
        var key = Guid.NewGuid();
        var url = "https://EXAMPLE.test/Case/%2F?a=1&a=2#Fragment";
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => host.Service.CreateAsync(owner, key, " " + url + " ", default)));
        Assert.Single(results.Select(x => x.Id).Distinct());
        Assert.All(results, x => Assert.Equal(url, x.DestinationUrl));
        var first = results[0];
        Assert.Equal(1L, await host.SqlAsync("SELECT count(*) FROM links"));
        Assert.Equal(409, (await Assert.ThrowsAsync<LinkFailure>(() => host.Service.CreateAsync(owner, key, url + "x", default))).Status);
        var conflictingKey = Guid.NewGuid();
        var conflictResults = await Task.WhenAll(new[] { url, url + "different" }.Select(async destination =>
        {
            try { await host.Service.CreateAsync(owner, conflictingKey, destination, default); return 201; }
            catch (LinkFailure error) { return error.Status; }
        }));
        Assert.Equal(new[] { 201, 409 }, conflictResults.Order());
        var second = await host.Service.CreateAsync(owner, Guid.NewGuid(), url, default);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Id, (await host.Service.CreateAsync(other, key, url, default)).Id);
        Assert.Equal("disabled", (await host.Service.DeactivateAsync(owner, first.Id, default)).Status);
        var disabledAt = await host.SqlAsync("SELECT owner_disabled_at FROM links WHERE id = $1", parameters: [first.Id]);
        host.App.Clock.Advance(TimeSpan.FromHours(23));
        Assert.Equal("disabled", (await host.Service.CreateAsync(owner, key, url, default)).Status);
        await host.Service.DeactivateAsync(owner, first.Id, default);
        Assert.Equal(disabledAt, await host.SqlAsync("SELECT owner_disabled_at FROM links WHERE id = $1", parameters: [first.Id]));
        host.App.Clock.Advance(TimeSpan.FromMinutes(60) - TimeSpan.FromSeconds(1));
        Assert.Equal(first.Id, (await host.Service.CreateAsync(owner, key, url, default)).Id);
        host.App.Clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => host.Service.CreateAsync(owner, key, url + "new", default)));
        Assert.Single(expired.Select(x => x.Id).Distinct());
        Assert.NotEqual(first.Id, expired[0].Id);
        Assert.Equal(url, (await host.Service.GetAsync(owner, first.Id, default)).DestinationUrl);
    }

    [Fact]
    [Trait("Requirement", "T17,RF-08,AF-02")]
    public async Task Stable_paging_ownership_and_state_precedence()
    {
        await using var host = await environment.CreateAsync();
        var (owner, _) = await host.UserAsync();
        var (other, _) = await host.UserAsync();
        Assert.Empty((await host.Service.ListAsync(owner, null, null, default)).Items);
        var links = new List<LinkResult>();
        for (var i = 0; i < 6; i++) links.Add(await host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test/" + i, default));
        Assert.Empty((await host.Service.ListAsync(other, null, null, default)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<LinkFailure>(() => host.Service.GetAsync(other, links[0].Id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<LinkFailure>(() => host.Service.DeactivateAsync(other, links[0].Id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<LinkFailure>(() => host.Service.GetAsync(owner, long.MaxValue, default))).Status);
        await host.SqlAsync("UPDATE links SET admin_blocked_at = now() WHERE id = $1", parameters: [links[0].Id]);
        Assert.Equal("blocked", (await host.Service.GetAsync(owner, links[0].Id, default)).Status);
        await host.Service.DeactivateAsync(owner, links[0].Id, default);
        await host.SqlAsync("UPDATE links SET admin_blocked_at = NULL WHERE id = $1", parameters: [links[0].Id]);
        Assert.Equal("disabled", (await host.Service.GetAsync(owner, links[0].Id, default)).Status);
        await host.SqlAsync("UPDATE users SET blocked_at = now() WHERE id = $1", parameters: [owner]);
        Assert.Equal("blocked", (await host.Service.GetAsync(owner, links[1].Id, default)).Status);
        Assert.Equal("disabled", (await host.Service.GetAsync(owner, links[0].Id, default)).Status);
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test", default));
        await host.SqlAsync("UPDATE users SET blocked_at = NULL WHERE id = $1", parameters: [owner]);
        var page1 = await host.Service.ListAsync(owner, 2, null, default);
        Assert.Equal(links.AsEnumerable().Reverse().Take(2).Select(x => x.Id), page1.Items.Select(x => x.Id));
        Assert.NotNull(page1.NextCursor);
        var newer = await host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test/new", default);
        using var replica = host.App.Replica();
        var replicaService = replica.Services.GetRequiredService<LinkService>();
        var page2 = await replicaService.ListAsync(owner, null, page1.NextCursor, default);
        var page3 = await replicaService.ListAsync(owner, 2, page2.NextCursor, default);
        Assert.Null(page3.NextCursor);
        Assert.Equal(links.AsEnumerable().Reverse().Select(x => x.Id), page1.Items.Concat(page2.Items).Concat(page3.Items).Select(x => x.Id));
        Assert.DoesNotContain(newer.Id, page2.Items.Select(x => x.Id));
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.ListAsync(owner, 0, null, default));
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.ListAsync(owner, 101, null, default));
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.ListAsync(other, null, page1.NextCursor, default));
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.ListAsync(owner, 3, page1.NextCursor, default));
        host.App.Clock.Advance(TimeSpan.FromHours(24));
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.ListAsync(owner, null, page2.NextCursor, default));
    }

    [Fact]
    [Trait("Requirement", "T02,RF-07,RF-18")]
    public async Task Ten_thousand_attempts_with_one_hundred_clients_and_rollbacks()
    {
        await using var host = await environment.CreateAsync();
        var (owner, _) = await host.UserAsync();
        // Deterministic database-side faults after nextval exercise actual transactional rollback.
        await host.SqlAsync("""
            CREATE FUNCTION reject_test_link() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.destination_url LIKE '%/abort' THEN RAISE EXCEPTION 'test rollback'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_test_link BEFORE INSERT ON links FOR EACH ROW EXECUTE FUNCTION reject_test_link();
            """);
        var ids = new System.Collections.Concurrent.ConcurrentBag<long>();
        var aborted = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 10000), new ParallelOptions { MaxDegreeOfParallelism = 100 }, async (i, ct) =>
        {
            try { ids.Add((await host.Service.CreateAsync(owner, Guid.NewGuid(), i % 10 == 0 ? "https://example.test/abort" : "https://example.test/" + i, ct)).Id); }
            catch (PostgresException error) when (error.SqlState == "P0001") { Interlocked.Increment(ref aborted); }
        });
        Assert.Equal(1000, aborted);
        Assert.Equal(9000, ids.Count);
        Assert.Equal(9000, ids.Distinct().Count());
        Assert.Equal(9000L, await host.SqlAsync("SELECT count(DISTINCT code) FROM links"));
        Assert.Equal(9000L, await host.SqlAsync("SELECT count(*) FROM creation_requests"));
        var last = (long)(await host.SqlAsync("SELECT last_value FROM link_ids"))!;
        Assert.True(last >= 10000);
        var after = await host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test/after", default);
        Assert.True(after.Id > last);
        Assert.All(ids, id => Assert.True(id < after.Id));
    }

    [Fact]
    [Trait("Requirement", "T20,F003-T01")]
    public async Task Migration_upgrade_rollback_constraints_and_cancellation()
    {
        await using var host = await environment.CreateAsync();
        var (owner, _) = await host.UserAsync();
        using var scope = host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        // Legacy migration IDs have a twelve-digit timestamp; EF's name resolver skips fifteen characters.
        await migrator.MigrateAsync(db.GetService<IMigrationsIdGenerator>().GetName(applied[^2]));
        Assert.True(await db.Users.AnyAsync(x => x.Id == owner));
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        var link = await host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test", default);
        foreach (var sql in new[] {
            "INSERT INTO links(id, code, owner_id, destination_url, created_at) SELECT 999, code, owner_id, destination_url, created_at FROM links LIMIT 1",
            "UPDATE links SET code = 'bad!'", "UPDATE links SET owner_id = NULL", "UPDATE links SET destination_url = NULL",
            "UPDATE creation_requests SET url_hash = decode('00','hex')" })
            await Assert.ThrowsAsync<PostgresException>(() => host.SqlAsync(sql));
        await host.SqlAsync("INSERT INTO links(id, code, owner_id, destination_url, created_at) SELECT 1001, 'z', owner_id, destination_url, created_at FROM links LIMIT 1; INSERT INTO links(id, code, owner_id, destination_url, created_at) SELECT 1002, 'Z', owner_id, destination_url, created_at FROM links LIMIT 1");
        Assert.Equal(2L, await host.SqlAsync("SELECT count(*) FROM links WHERE code IN ('z','Z')"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Service.CreateAsync(owner, Guid.NewGuid(), "https://example.test", cancelled.Token));
        await Assert.ThrowsAsync<LinkFailure>(() => host.Service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), "https://example.test", default));
        Assert.Equal(link.DestinationUrl, (await host.Service.GetAsync(owner, link.Id, default)).DestinationUrl);
    }
}
