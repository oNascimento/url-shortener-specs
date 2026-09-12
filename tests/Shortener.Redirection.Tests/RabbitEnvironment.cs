extern alias Redirector;
using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Shortener.LinkManagement.Tests;
using Xunit;
using RabbitSettings = Redirector::Shortener.Redirector.RabbitSettings;
using RabbitTransport = Redirector::Shortener.Redirector.RabbitTransport;

namespace Shortener.Redirection.Tests;

[CollectionDefinition("rabbit-publisher")]
public sealed class RabbitCollection : ICollectionFixture<RabbitEnvironment>, ICollectionFixture<LinkEnvironment>;

public sealed class RabbitEnvironment : IAsyncLifetime
{
    public IContainer Container { get; } = new ContainerBuilder("rabbitmq:4.1.4-management")
        .WithEnvironment("RABBITMQ_DEFAULT_USER", "test")
        .WithEnvironment("RABBITMQ_DEFAULT_PASS", "integration-only")
        .WithEnvironment("RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS", "+S 2:2")
        .WithPortBinding(5672, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672)
            .UntilCommandIsCompleted("rabbitmq-diagnostics", "-q", "ping")).Build();
    public Task InitializeAsync() => Container.StartAsync();
    public async Task DisposeAsync() => await Container.DisposeAsync();

    public async Task<RabbitSettings> SettingsAsync(long bytes = 4L * 1024 * 1024 * 1024)
    {
        var vhost = "test_" + Guid.NewGuid().ToString("N");
        Assert.Equal(0, (await Container.ExecAsync(["rabbitmqctl", "add_vhost", vhost])).ExitCode);
        Assert.Equal(0, (await Container.ExecAsync(["rabbitmqctl", "set_permissions", "-p", vhost, "test", ".*", ".*", ".*"])).ExitCode);
        return new(Container.Hostname, Container.GetMappedPublicPort(5672), "test", "integration-only", vhost, 2, bytes);
    }

    public static ConnectionFactory Factory(RabbitSettings settings) => new()
    {
        HostName = settings.Host, Port = settings.Port, UserName = settings.UserName,
        Password = settings.Password, VirtualHost = settings.VirtualHost, AutomaticRecoveryEnabled = false
    };

    public static async Task WaitUntil(Func<bool> condition, int seconds = 20)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(seconds)) await Task.Delay(25);
        Assert.True(condition(), "Condition did not become true within the bounded wait.");
    }
}

public sealed class RabbitHarness(RabbitSettings settings) : IAsyncDisposable
{
    public RabbitTransport Transport { get; } = new(settings, NullLogger<RabbitTransport>.Instance);
    public async Task StartAsync()
    {
        await Transport.StartAsync(default);
        await RabbitEnvironment.WaitUntil(() => Transport.Ready);
    }
    public async ValueTask DisposeAsync()
    {
        await Transport.StopAsync(default);
        Transport.Dispose();
    }
}
