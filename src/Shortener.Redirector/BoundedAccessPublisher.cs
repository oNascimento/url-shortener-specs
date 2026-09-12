using Shortener.Application;

namespace Shortener.Redirector;

public interface IConfirmTransport
{
    Task<PublishOutcome> PublishAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);
}

public sealed class BoundedAccessPublisher(IConfirmTransport transport, TimeProvider clock, RabbitSettings settings) : IAccessPublisher
{
    public static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(100);
    private readonly SemaphoreSlim slots = new(settings.PoolSize, settings.PoolSize);
    private int pending;

    public async Task<PublishOutcome> PublishAsync(AccessRecorded message, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref pending) > settings.PoolSize * 4)
        {
            Interlocked.Decrement(ref pending);
            return PublishOutcome.Rejected;
        }
        using var deadline = new CancellationTokenSource(Budget, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var acquired = false;
        Task<PublishOutcome>? attempt = null;
        try
        {
            await slots.WaitAsync(linked.Token);
            acquired = true;
            var body = AccessEnvelopeSerializer.Serialize(message);
            attempt = transport.PublishAsync(body, linked.Token);
            return await attempt.WaitAsync(linked.Token);
        }
        catch (Exception)
        {
            // Cancellation or transport failure cannot prove that the broker did not accept it.
            return PublishOutcome.Unknown;
        }
        finally
        {
            Interlocked.Decrement(ref pending);
            if (acquired)
            {
                if (attempt is { IsCompleted: false }) _ = ReleaseAfterCompletionAsync(attempt);
                else slots.Release();
            }
        }
    }

    private async Task ReleaseAfterCompletionAsync(Task attempt)
    {
        // At most PoolSize continuations exist, even if a transport ignores cancellation.
        // A timed-out operation keeps its slot; a later confirm never changes its reported outcome.
        try { await attempt; }
        catch (Exception) { }
        finally { slots.Release(); }
    }
}
