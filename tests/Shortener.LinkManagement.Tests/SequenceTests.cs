using Npgsql;
using Shortener.Application;
using Shortener.Infrastructure;
using Xunit;

namespace Shortener.LinkManagement.Tests;

[Collection("links")]
[Trait("Category", "Integration")]
public sealed class SequenceTests(LinkEnvironment environment)
{
    [Fact]
    [Trait("Requirement", "T21,T02")]
    public async Task Independent_coordinators_share_one_confirmed_range()
    {
        await using var host = await environment.CreateAsync();
        var ids = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => host.TransactionAsync(c =>
            new LinkSequence(new PostgresRecoveryRegistry(host.Registry)).NextAsync(c, default))));
        Assert.Equal(Enumerable.Range(1, 100).Select(x => (long)x), ids.Order());
        Assert.Equal(1_000_000L, await host.SqlAsync("SELECT upper_bound FROM id_reservations", true));
        Assert.Equal(1_000_000L, await host.SqlAsync("SELECT upper_bound FROM link_allocation"));
    }

    [Fact]
    [Trait("Requirement", "T21,T22,RF-18")]
    public async Task Reservations_fail_closed_reconcile_and_extend_without_reusing_ids()
    {
        await using var host = await environment.CreateAsync();
        var registry = new FaultRegistry(new PostgresRecoveryRegistry(host.Registry));
        var sequence = new LinkSequence(registry);
        Task<long> Next() => host.TransactionAsync(c => sequence.NextAsync(c, default));
        registry.FailRead = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<LinkFailure>(Next)).Status);
        registry.FailRead = false;
        registry.RejectWrite = true;
        await Assert.ThrowsAsync<LinkFailure>(Next);
        registry.RejectWrite = false;
        registry.FailBeforeWrite = true;
        await Assert.ThrowsAsync<LinkFailure>(Next);
        registry.FailBeforeWrite = false;
        registry.LoseConfirmation = true;
        Assert.Equal(1, await Next());
        Assert.Equal(1_000_000L, await host.SqlAsync("SELECT upper_bound FROM id_reservations", true));
        registry.FailRead = true;
        Assert.Equal(2, await Next()); // No registry access while safely inside the range.
        await host.SqlAsync("SELECT setval('link_ids', 800000, true)");
        Assert.Equal(800001, await Next()); // Prefetch failure leaves the existing range available.
        registry.CancelRead = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Next);
        registry.CancelRead = false;
        registry.FailRead = false;
        registry.CancelWrite = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(Next);
        registry.CancelWrite = false;
        registry.LoseConfirmation = false;
        Assert.Equal(800002, await Next());
        Assert.Equal(2_000_000L, await host.SqlAsync("SELECT upper_bound FROM link_allocation"));
        await host.SqlAsync("SELECT setval('link_ids', 1800000, true)");
        // Simulate an externally committed extension whose local transaction rolled back.
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = 3000000", true);
        Assert.Equal(1800001, await Next());
        Assert.Equal(3_000_000L, await host.SqlAsync("SELECT upper_bound FROM link_allocation"));
        await host.SqlAsync("SELECT setval('link_ids', 3000000, true)");
        registry.FailRead = true;
        await Assert.ThrowsAsync<LinkFailure>(Next);
        Assert.Equal(3_000_000L, await host.SqlAsync("SELECT last_value FROM link_ids"));
        registry.FailRead = false;
        registry.CompareLost = true;
        Assert.Equal(3000001, await Next());
        await host.SqlAsync("SELECT setval('link_ids', 3800000, true)");
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = 0", true);
        Assert.Equal("registry_integrity", (await Assert.ThrowsAsync<LinkFailure>(Next)).Code);
    }

    [Fact]
    [Trait("Requirement", "T22,T01")]
    public async Task Final_partial_range_and_exhaustion_never_overflow()
    {
        await using var host = await environment.CreateAsync();
        var sequence = new LinkSequence(new PostgresRecoveryRegistry(host.Registry));
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = $1", true, long.MaxValue - 2);
        Assert.Equal(long.MaxValue - 1, await host.TransactionAsync(c => sequence.NextAsync(c, default)));
        Assert.Equal(long.MaxValue, await host.TransactionAsync(c => sequence.NextAsync(c, default)));
        Assert.Equal(long.MaxValue, await host.SqlAsync("SELECT upper_bound FROM id_reservations", true));
        Assert.Equal("capacity_exhausted", (await Assert.ThrowsAsync<LinkFailure>(() => host.TransactionAsync(c => sequence.NextAsync(c, default)))).Code);
        await host.SqlAsync("UPDATE link_allocation SET upper_bound = 0");
        Assert.Equal("reservation_unavailable", (await Assert.ThrowsAsync<LinkFailure>(() => host.TransactionAsync(c => sequence.NextAsync(c, default)))).Code);
    }

    [Fact]
    [Trait("Requirement", "T21,RF-18")]
    public async Task Restore_requires_suspension_and_positive_fencing_and_skips_old_reservations()
    {
        await using var host = await environment.CreateAsync();
        var sequence = new LinkSequence(new PostgresRecoveryRegistry(host.Registry));
        var role = "writer_" + Guid.NewGuid().ToString("N");
        await host.SqlAsync("CREATE ROLE " + role + " LOGIN PASSWORD 'test-old-writer'", true);
        var isolation = new PostgresWriterIsolation(host.Registry, role);
        async Task<bool> Prepare(NpgsqlConnection c) { await sequence.PrepareRestoreAsync(c, isolation, default); return true; }
        Assert.Equal("creation_not_suspended", (await Assert.ThrowsAsync<LinkFailure>(() => host.TransactionAsync(Prepare))).Code);
        await host.TransactionAsync(async c => { await sequence.SuspendAsync(c, default); return true; });
        Assert.Equal("creation_suspended", (await Assert.ThrowsAsync<LinkFailure>(() => host.TransactionAsync(c => sequence.NextAsync(c, default)))).Code);
        Assert.Equal("writer_not_isolated", (await Assert.ThrowsAsync<LinkFailure>(() => host.TransactionAsync(Prepare))).Code);
        Assert.False(await new PostgresWriterIsolation(host.Registry, "missing_writer").IsConfirmedAsync(default));
        await using var oldWriter = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(host.Registry)
            { Username = role, Password = "test-old-writer", Pooling = false }.ConnectionString);
        await oldWriter.OpenAsync();
        await host.SqlAsync("ALTER ROLE " + role + " NOLOGIN", true);
        Assert.False(await isolation.IsConfirmedAsync(default)); // NOLOGIN alone does not terminate existing writers.
        await oldWriter.CloseAsync();
        Assert.True(await isolation.IsConfirmedAsync(default));
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = 5000000", true);
        Assert.True(await host.TransactionAsync(Prepare));
        Assert.Equal(5000001, await host.TransactionAsync(c => sequence.NextAsync(c, default)));
        // A normal process restart does not reset the sequence or reserve a replacement first range.
        var restarted = new LinkSequence(new PostgresRecoveryRegistry(host.Registry));
        Assert.Equal(5000002, await host.TransactionAsync(c => restarted.NextAsync(c, default)));
        await host.TransactionAsync(async c => { await sequence.SuspendAsync(c, default); return true; });
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = $1", true, long.MaxValue);
        Assert.Equal("capacity_exhausted", (await Assert.ThrowsAsync<LinkFailure>(() => host.TransactionAsync(Prepare))).Code);
        Assert.Equal(true, await host.SqlAsync("SELECT suspended FROM link_allocation"));
    }

    private sealed class FaultRegistry(IRecoveryRegistry inner) : IRecoveryRegistry
    {
        public bool FailRead, RejectWrite, FailBeforeWrite, LoseConfirmation, CancelRead, CancelWrite, CompareLost;
        public Task<long> ReadUpperBoundAsync(CancellationToken cancellationToken)
        {
            if (CancelRead) throw new OperationCanceledException();
            if (FailRead) throw new IOException("registry unavailable");
            return inner.ReadUpperBoundAsync(cancellationToken);
        }
        public async Task<bool> CompareExchangeUpperBoundAsync(long expected, long next, CancellationToken cancellationToken)
        {
            if (CancelWrite) throw new OperationCanceledException();
            if (FailBeforeWrite) throw new IOException("not committed");
            if (RejectWrite) return false;
            var result = await inner.CompareExchangeUpperBoundAsync(expected, next, cancellationToken);
            if (LoseConfirmation) throw new IOException("response lost after commit");
            return CompareLost ? false : result;
        }
        public Task<DeletionMarker> RecordDeletionAsync(DeletionMarker marker, CancellationToken cancellationToken) => inner.RecordDeletionAsync(marker, cancellationToken);
    }
}
