extern alias Redirector;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shortener.Authentication.Tests;
using Xunit;

namespace Shortener.Redirection.Tests;

[CollectionDefinition("redirection")]
public sealed class RedirectCollection : ICollectionFixture<Shortener.LinkManagement.Tests.LinkEnvironment>;

public sealed class RedirectHost(Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    : WebApplicationFactory<Redirector::Program>
{
    public CaptureLogs Logs { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var pair in settings) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.AddLogging(logging => logging.AddProvider(Logs));
            configure?.Invoke(services);
        });
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://s.test"), AllowAutoRedirect = false, HandleCookies = false });
}
