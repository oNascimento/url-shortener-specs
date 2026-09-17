using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Shortener.Redirector;

public static class RedirectHealth
{
    public static void AddRedirectReadiness(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<RedirectPrimaryHealth>("redirect-primary", tags: ["ready"]);
        services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (var registration in options.Registrations.Where(check => check.Tags.Contains("ready")
                         && check.Name != "redirect-primary").ToArray())
                options.Registrations.Remove(registration);
        });
    }
}

public sealed class RedirectPrimaryHealth(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            // Exercise the same primary query, including link/account schema; absence is healthy.
            await scope.ServiceProvider.GetRequiredService<IRedirectResolver>().ResolveAsync("0", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) { return HealthCheckResult.Unhealthy("primary resolution unavailable"); }
    }
}
