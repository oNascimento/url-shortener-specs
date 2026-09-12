using System.Collections.Concurrent;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shortener.Application;

namespace Shortener.Redirector;

public sealed record RabbitSettings(string Host, int Port, string UserName, string Password, string VirtualHost, int PoolSize, long QueueMaxBytes)
{
    public static RabbitSettings From(IConfiguration configuration)
    {
        var size = configuration.GetValue<int>("Messaging:PoolSize", 16);
        if (size is < 1 or > 64) throw new InvalidOperationException("Messaging:PoolSize must be between 1 and 64.");
        var bytes = configuration.GetValue<long>("Messaging:QueueMaxBytes", 4L * 1024 * 1024 * 1024);
        if (bytes < 1) throw new InvalidOperationException("Messaging:QueueMaxBytes must be positive.");
        return new(configuration["Messaging:Host"]!, configuration.GetValue<int>("Messaging:Port", 5672),
            configuration["Messaging:UserName"] ?? "guest", configuration["Messaging:Password"] ?? "guest",
            configuration["Messaging:VirtualHost"] ?? "/", size, bytes);
    }
}

public sealed class RabbitTransport(RabbitSettings settings, ILogger<RabbitTransport> logger) : BackgroundService, IConfirmTransport
{
    public const string Exchange = "access";
    public const string Queue = "access.recorded.v1";
    public const string RoutingKey = "recorded.v1";
    private readonly Channel<IChannel> available = Channel.CreateBounded<IChannel>(settings.PoolSize);
    private readonly ConcurrentDictionary<IChannel, byte> channels = new();
    private readonly ConcurrentQueue<IChannel> retired = new();
    private IConnection? connection;
    private int ready;
    public bool Ready => Volatile.Read(ref ready) == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = settings.Host, Port = settings.Port, UserName = settings.UserName, Password = settings.Password,
            VirtualHost = settings.VirtualHost, AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(2), ContinuationTimeout = TimeSpan.FromSeconds(2),
            ClientProvidedName = "shortener-redirector"
        };
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    while (retired.TryDequeue(out var old)) await old.DisposeAsync();
                    if (connection is null || !connection.IsOpen)
                    {
                        Volatile.Write(ref ready, 0);
                        await ClearAsync();
                        connection = await factory.CreateConnectionAsync(stoppingToken);
                    }
                    while (channels.Count < settings.PoolSize)
                    {
                        var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), stoppingToken);
                        try
                        {
                            await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: stoppingToken);
                            await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
                                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum",
                                    ["x-max-length-bytes"] = settings.QueueMaxBytes, ["x-overflow"] = "reject-publish" }, cancellationToken: stoppingToken);
                            await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: stoppingToken);
                            channels.TryAdd(channel, 0);
                            available.Writer.TryWrite(channel);
                        }
                        catch
                        {
                            await channel.DisposeAsync();
                            throw;
                        }
                    }
                    Volatile.Write(ref ready, 1);
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    Volatile.Write(ref ready, 0);
                    logger.LogWarning("Messaging connection unavailable ({errorType})", error.GetType().Name);
                }
                await Task.Delay(Ready ? TimeSpan.FromMilliseconds(25) : TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            Volatile.Write(ref ready, 0);
            await ClearAsync();
        }
    }

    public async Task<PublishOutcome> PublishAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        if (!Ready) return PublishOutcome.Rejected;
        var channel = await available.Reader.ReadAsync(cancellationToken);
        var reusable = true;
        try
        {
            if (!channel.IsOpen) return PublishOutcome.Rejected;
            // The client tracks confirms and mandatory returns; a return faults the publish even if followed by ack.
            await channel.BasicPublishAsync(Exchange, RoutingKey, mandatory: true,
                basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json", Type = Queue },
                body: body, cancellationToken: cancellationToken);
            return PublishOutcome.Confirmed;
        }
        catch (PublishException)
        {
            return PublishOutcome.Rejected;
        }
        catch (Exception)
        {
            reusable = false;
            return PublishOutcome.Unknown;
        }
        finally
        {
            if (reusable && channel.IsOpen && Ready) available.Writer.TryWrite(channel);
            else if (channels.TryRemove(channel, out _)) retired.Enqueue(channel);
        }
    }

    private async Task ClearAsync()
    {
        while (available.Reader.TryRead(out _)) { }
        foreach (var channel in channels.Keys)
            if (channels.TryRemove(channel, out _)) await channel.DisposeAsync();
        while (retired.TryDequeue(out var old)) await old.DisposeAsync();
        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }
    }
}
