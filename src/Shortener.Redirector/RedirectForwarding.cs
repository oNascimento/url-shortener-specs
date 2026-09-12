using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Shortener.Redirector;

public static class RedirectForwarding
{
    public static void ConfigureRedirectForwarding(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            var hops = builder.Configuration.GetValue<int>("ForwardedHeaders:ForwardLimit", 1);
            if (hops is < 1 or > 10) throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be between 1 and 10.");
            options.ForwardLimit = hops;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in builder.Configuration.GetSection("TrustedProxies").GetChildren())
                options.KnownProxies.Add(IPAddress.Parse(proxy.Value!));
            foreach (var network in builder.Configuration.GetSection("TrustedNetworks").GetChildren())
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network.Value!));
            // ASP.NET treats two empty lists as trust-all. Keep deny-by-default instead.
            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
                options.KnownProxies.Add(IPAddress.None);
        });
    }

    public static void UseRedirectForwarding(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Connection.RemoteIpAddress is null)
            {
                context.Request.Headers.Remove("X-Forwarded-For");
                context.Request.Headers.Remove("X-Forwarded-Proto");
            }
            await next(context);
        });
        app.UseForwardedHeaders();
    }
}
