extern alias Redirector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Shortener.Application;
using Xunit;
using BoundedAccessPublisher = Redirector::Shortener.Redirector.BoundedAccessPublisher;
using IConfirmTransport = Redirector::Shortener.Redirector.IConfirmTransport;
using RabbitSettings = Redirector::Shortener.Redirector.RabbitSettings;

namespace Shortener.Redirection.Tests;

public sealed class PublisherTests
{
    internal static readonly AccessRecorded Message = new(1, Guid.NewGuid(), "9007199254740993",
        new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), "192.0.2.1");
    private static readonly RabbitSettings Settings = new("localhost", 5672, "test", "test", "/", 1, 1024);

    [Fact]
    public async Task Single_deadline_includes_capacity_and_late_confirm_never_releases_an_occupied_slot_early()
    {
        var clock = new FakeTimeProvider();
        var completion = new TaskCompletionSource<PublishOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new DelegateTransport(() => completion.Task);
        var publisher = new BoundedAccessPublisher(transport, clock, Settings);
        var first = publisher.PublishAsync(Message, default);
        var waiting = publisher.PublishAsync(Message, default);
        Assert.Equal(1, transport.Calls);
        clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.False(first.IsCompleted);
        Assert.False(waiting.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(PublishOutcome.Unknown, await first);
        Assert.Equal(PublishOutcome.Unknown, await waiting);
        var queued = Enumerable.Range(0, 4).Select(_ => publisher.PublishAsync(Message, default)).ToArray();
        Assert.Equal(PublishOutcome.Rejected, await publisher.PublishAsync(Message, default));
        Assert.Equal(1, transport.Calls);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.All(await Task.WhenAll(queued), result => Assert.Equal(PublishOutcome.Unknown, result));
        completion.SetResult(PublishOutcome.Confirmed);
        Assert.Equal(PublishOutcome.Confirmed, await publisher.PublishAsync(Message, default).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(PublishOutcome.Unknown, await first);
        Assert.Equal(2, transport.Calls);
    }

    [Fact]
    public async Task Late_failure_is_observed_and_transport_failures_are_unknown()
    {
        var clock = new FakeTimeProvider();
        var completion = new TaskCompletionSource<PublishOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new DelegateTransport(() => completion.Task);
        var publisher = new BoundedAccessPublisher(transport, clock, Settings);
        var first = publisher.PublishAsync(Message, default);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(PublishOutcome.Unknown, await first);
        completion.SetException(new IOException("simulated failure"));
        Assert.Equal(PublishOutcome.Unknown, await publisher.PublishAsync(Message, default).WaitAsync(TimeSpan.FromSeconds(2)));
        var synchronous = new BoundedAccessPublisher(new DelegateTransport(() => throw new IOException()), clock, Settings);
        Assert.Equal(PublishOutcome.Unknown, await synchronous.PublishAsync(Message, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(PublishOutcome.Unknown, await publisher.PublishAsync(Message, cancelled.Token));
    }

    [Theory]
    [InlineData(PublishOutcome.Confirmed)]
    [InlineData(PublishOutcome.Rejected)]
    [InlineData(PublishOutcome.Unknown)]
    public async Task Completed_transport_outcomes_are_preserved(PublishOutcome outcome)
    {
        var publisher = new BoundedAccessPublisher(new DelegateTransport(() => Task.FromResult(outcome)), new FakeTimeProvider(), Settings);
        Assert.Equal(outcome, await publisher.PublishAsync(Message, default));
    }

    [Theory]
    [InlineData("Messaging:PoolSize", "0")]
    [InlineData("Messaging:PoolSize", "65")]
    [InlineData("Messaging:QueueMaxBytes", "0")]
    public void Invalid_capacity_is_rejected(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => RabbitSettings.From(configuration));
    }

    private sealed class DelegateTransport(Func<Task<PublishOutcome>> action) : IConfirmTransport
    {
        public int Calls { get; private set; }
        public Task<PublishOutcome> PublishAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            Calls++;
            return action();
        }
    }
}
