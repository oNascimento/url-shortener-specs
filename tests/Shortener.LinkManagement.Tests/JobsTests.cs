extern alias Jobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Shortener.Application;
using Shortener.Infrastructure;
using Shortener.ServiceDefaults;
using Xunit;
using JobCommands = Jobs::Shortener.Jobs.JobCommands;
using JobsProgram = Jobs::Program;

namespace Shortener.LinkManagement.Tests;

[Collection("links")]
[Trait("Category", "Integration")]
public sealed class JobsTests(LinkEnvironment environment)
{
    [Fact]
    [Trait("Requirement", "T21,T20,F003-T01")]
    public async Task Jobs_migrate_suspend_verify_and_prepare_restore()
    {
        await using var host = await environment.CreateAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(host.App.Settings);
        builder.AddFoundation("shortener-jobs");
        await using var app = builder.Build();
        await JobCommands.RunAsync(app, ["--migrate"]);
        await JobCommands.RunAsync(app, ["--suspend-link-creation"]);
        Assert.Equal(true, await host.SqlAsync("SELECT suspended FROM link_allocation"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => JobCommands.RunAsync(app, ["--prepare-link-restore"]));
        app.Configuration["ConnectionStrings:OldPrimary"] = host.Registry;
        await Assert.ThrowsAsync<InvalidOperationException>(() => JobCommands.RunAsync(app, ["--prepare-link-restore"]));
        var role = "writer_" + Guid.NewGuid().ToString("N");
        app.Configuration["Recovery:WriterRole"] = role;
        await host.SqlAsync("CREATE ROLE " + role + " LOGIN", true);
        await Assert.ThrowsAsync<LinkFailure>(() => JobCommands.RunAsync(app, ["--prepare-link-restore"]));
        await host.SqlAsync("ALTER ROLE " + role + " NOLOGIN", true);
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = 7000000", true);
        await JobCommands.RunAsync(app, ["--prepare-link-restore"]);
        Assert.Equal(false, await host.SqlAsync("SELECT suspended FROM link_allocation"));
        var sequence = new LinkSequence(new PostgresRecoveryRegistry(host.Registry));
        Assert.Equal(7000001, await host.TransactionAsync(c => sequence.NextAsync(c, default)));
        await JobCommands.RunAsync(app, ["--suspend-link-creation"]);
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = 0", true);
        Assert.Equal("registry_integrity", (await Assert.ThrowsAsync<LinkFailure>(() => JobCommands.RunAsync(app, ["--prepare-link-restore"]))).Code);
        using var factory = new WebApplicationFactory<JobsProgram>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            foreach (var pair in host.App.Settings) web.UseSetting(pair.Key, pair.Value);
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(host.App.Settings));
        });
        using var client = factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();
    }
}
