using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Shortener.Redirection.Tests;

// A real TCP relay: pausing broker->client traffic loses confirms without faking broker acceptance.
public sealed class AmqpProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentBag<Task> connections = [];
    private readonly object sync = new();
    private TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task accept;

    public AmqpProxy(string host, int port)
    {
        resume.SetResult();
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accept = AcceptAsync(host, port);
    }
    public int Port { get; }
    public void Pause() { lock (sync) resume = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void Resume() { lock (sync) resume.TrySetResult(); }

    private async Task AcceptAsync(string host, int port)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                var server = new TcpClient();
                await server.ConnectAsync(host, port, stopping.Token);
                connections.Add(RelayAsync(client, server));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    private async Task RelayAsync(TcpClient client, TcpClient server)
    {
        using (client)
        using (server)
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            var upstream = client.GetStream().CopyToAsync(server.GetStream(), stop.Token);
            var downstream = ReadBrokerAsync(server.GetStream(), client.GetStream(), stop.Token);
            await Task.WhenAny(upstream, downstream);
            await stop.CancelAsync();
            try { await Task.WhenAll(upstream, downstream); }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }

    private async Task ReadBrokerAsync(NetworkStream source, NetworkStream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) return;
            Task gate;
            lock (sync) gate = resume.Task;
            await gate.WaitAsync(cancellationToken);
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        Resume();
        await accept;
        await Task.WhenAll(connections);
        stopping.Dispose();
    }
}
