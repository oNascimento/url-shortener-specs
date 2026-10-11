extern alias Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortener.Authentication.Tests;
using Shortener.LinkManagement.Tests;
using Shortener.Redirection.Tests;
using Xunit;

namespace Shortener.Events.Tests;

[CollectionDefinition("rabbit-publisher", DisableParallelization = true)]
public sealed class EventsRabbitCollection : ICollectionFixture<RabbitEnvironment>, ICollectionFixture<LinkEnvironment>;

public sealed class WorkerHost(Dictionary<string, string?> settings, Dictionary<string, string?> messaging) : WebApplicationFactory<Worker::Program>
{
    public CaptureLogs Logs { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in settings.Concat(messaging)) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings).AddInMemoryCollection(messaging));
        builder.ConfigureServices(services => services.AddLogging(logging => logging.AddProvider(Logs)));
    }
}

[Collection("rabbit-publisher")]
[Trait("Category", "Integration")]
public sealed class ConsumerTests(RabbitEnvironment rabbit, LinkEnvironment environment)
{
    [Fact]
    public async Task T11_consumer_batches_100_GET_identities_and_1000_redeliveries_without_duplicates()
    {
        await using var host = await environment.CreateAsync();
        var (_, link) = await StoreTests.LinkAsync(host);
        var settings = await rabbit.SettingsAsync();
        await using var publisher = new RabbitHarness(settings);
        await publisher.StartAsync();
        using var worker = new WorkerHost(host.App.Settings, new()
        {
            ["Messaging:Host"] = settings.Host, ["Messaging:Port"] = settings.Port.ToString(),
            ["Messaging:UserName"] = settings.UserName, ["Messaging:Password"] = settings.Password,
            ["Messaging:VirtualHost"] = settings.VirtualHost
        });
        using var client = worker.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var now = DateTimeOffset.UtcNow;
        var bodies = Enumerable.Range(0, 100).Select(i => EnvelopeTests.Body(Guid.NewGuid(), link, now.AddTicks(i * 10))).ToArray();
        foreach (var body in bodies) for (var n = 0; n < 11; n++)
            Assert.Equal(Shortener.Application.PublishOutcome.Confirmed, await publisher.Transport.PublishAsync(body, default));
        await WaitAsync(async () => Equals(100L, await host.SqlAsync("SELECT count(*) FROM access_events")));
        await using var connection = await RabbitEnvironment.Factory(settings).CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await WaitAsync(async () => await channel.MessageCountAsync("access.recorded.v1") == 0);
        Assert.Equal(100m, await host.SqlAsync("SELECT sum(count) FROM daily_totals"));
        await host.SqlAsync("CREATE FUNCTION fail_totals() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'integration fault'; END $$");
        await host.SqlAsync("CREATE TRIGGER fail_totals BEFORE INSERT ON daily_totals FOR EACH STATEMENT EXECUTE FUNCTION fail_totals()");
        var retryBody = EnvelopeTests.Body(Guid.NewGuid(), link, now.AddSeconds(1));
        await publisher.Transport.PublishAsync(retryBody, default);
        await WaitAsync(() => Task.FromResult(worker.Logs.Messages.Any(x => x.Contains("Access consumption paused"))));
        Assert.Equal(100L, await host.SqlAsync("SELECT count(*) FROM access_events"));
        await host.SqlAsync("DROP TRIGGER fail_totals ON daily_totals");
        await WaitAsync(async () => Equals(101L, await host.SqlAsync("SELECT count(*) FROM access_events")));
        Assert.Equal(101m, await host.SqlAsync("SELECT sum(count) FROM daily_totals"));
        // Commit-before-ack replay and a broker-side disconnect both preserve the identity.
        await publisher.Transport.PublishAsync(retryBody, default);
        Assert.Equal(0, (await rabbit.Container.ExecAsync(["rabbitmqctl", "stop_app"])).ExitCode);
        await WaitAsync(() => Task.FromResult(worker.Logs.Messages.Any(x => x.Contains("IOException"))));
        Assert.Equal(0, (await rabbit.Container.ExecAsync(["rabbitmqctl", "start_app"])).ExitCode);
        await WaitAsync(() => Task.FromResult(publisher.Transport.Ready));
        await publisher.Transport.PublishAsync(retryBody, default);
        await Task.Delay(500);
        Assert.Equal(101m, await host.SqlAsync("SELECT sum(count) FROM daily_totals"));
    }

    internal static async Task WaitAsync(Func<Task<bool>> condition, int seconds = 30)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (DateTimeOffset.UtcNow < until)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        Assert.True(await condition(), "Condition did not become true within the bounded wait.");
    }
}
