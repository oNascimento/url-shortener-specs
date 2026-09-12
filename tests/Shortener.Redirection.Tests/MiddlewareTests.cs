extern alias Redirector;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IRedirectResolver = Redirector::Shortener.Redirector.IRedirectResolver;
using ResolvedLink = Redirector::Shortener.Redirector.ResolvedLink;
using RedirectMiddleware = Redirector::Shortener.Redirector.RedirectMiddleware;

namespace Shortener.Redirection.Tests;

public sealed class MiddlewareTests
{
    [Fact]
    public async Task Health_paths_bypass_public_resolution_and_cancelled_visitors_abort()
    {
        var next = 0;
        var middleware = new RedirectMiddleware(_ => { next++; return Task.CompletedTask; });
        var resolver = new Resolver();
        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            await middleware.InvokeAsync(context, resolver);
        }
        Assert.Equal(2, next);
        Assert.Equal(0, resolver.Calls);
        var cancelled = new DefaultHttpContext();
        var lifetime = new Lifetime();
        cancelled.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        cancelled.Request.Path = "/z";
        cancelled.Request.Method = "GET";
        await middleware.InvokeAsync(cancelled, resolver);
        Assert.True(lifetime.Aborted);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task Missing_path_is_404_and_problem_uses_current_trace()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        await new RedirectMiddleware(_ => throw new InvalidOperationException()).InvokeAsync(context, new Resolver());
        Assert.Equal(404, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains(activity.TraceId.ToString(), await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private sealed class Resolver : IRedirectResolver
    {
        public int Calls { get; private set; }
        public Task<ResolvedLink?> ResolveAsync(string code, CancellationToken cancellationToken)
        {
            Calls++;
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class Lifetime : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; } = new(true);
        public bool Aborted { get; private set; }
        public void Abort() => Aborted = true;
    }
}
