using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shortener.Infrastructure;

namespace Shortener.Worker;

public sealed class AccessConsumer(IConfiguration configuration, AccessBatchStore store, TimeProvider clock,
    ILogger<AccessConsumer> logger) : BackgroundService
{
    private int backoff = 1;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Access consumption paused ({errorClass})", error.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(backoff), clock, stoppingToken);
                backoff = Math.Min(30, backoff * 2);
            }
        }
    }

    public async Task ConsumeAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration["Messaging:Host"]!, Port = configuration.GetValue("Messaging:Port", 5672),
            UserName = configuration["Messaging:UserName"] ?? "guest", Password = configuration["Messaging:Password"] ?? "guest",
            VirtualHost = configuration["Messaging:VirtualHost"] ?? "/", AutomaticRecoveryEnabled = false,
            ClientProvidedName = "shortener-worker"
        };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.BasicQosAsync(0, 500, false, ct);
        var buffer = Channel.CreateBounded<(ulong Tag, byte[] Body)>(500);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
            await buffer.Writer.WriteAsync((delivery.DeliveryTag, delivery.Body.ToArray()), ct);
        channel.ChannelShutdownAsync += (_, _) => { buffer.Writer.TryComplete(); return Task.CompletedTask; };
        connection.ConnectionShutdownAsync += (_, _) => { buffer.Writer.TryComplete(); return Task.CompletedTask; };
        await channel.BasicConsumeAsync(configuration["Messaging:Queue"] ?? "access.recorded.v1", false, consumer, ct);
        while (await buffer.Reader.WaitToReadAsync(ct))
        {
            var batch = new List<(ulong Tag, byte[] Body)> { await buffer.Reader.ReadAsync(ct) };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100), clock);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                while (batch.Count < 500 && await buffer.Reader.WaitToReadAsync(deadline.Token))
                    while (batch.Count < 500 && buffer.Reader.TryRead(out var item)) batch.Add(item);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            var events = batch.Select(x => AccessEnvelope.Parse(x.Body, clock.GetUtcNow())).ToArray();
            await store.PersistAsync(events, ct);
            backoff = 1;
            foreach (var item in batch) await channel.BasicAckAsync(item.Tag, false, ct);
        }
        ct.ThrowIfCancellationRequested();
        throw new IOException("Consumer connection closed.");
    }
}
