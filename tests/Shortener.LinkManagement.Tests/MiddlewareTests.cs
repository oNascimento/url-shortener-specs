using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shortener.Infrastructure;
using Shortener.ServiceDefaults;
using Xunit;

namespace Shortener.LinkManagement.Tests;

public sealed class MiddlewareUnitTests
{
    [Fact]
    public async Task Missing_identity_and_invalid_subject_are_not_used_as_rate_limit_keys()
    {
        var calls = 0;
        var middleware = new AuthHttp(_ => { calls++; return Task.CompletedTask; });
        foreach (var principal in new[] { new ClaimsPrincipal(), new ClaimsPrincipal(new ClaimsIdentity()),
            new ClaimsPrincipal(new ClaimsIdentity([], "test")),
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "invalid")], "test")) })
        {
            var context = new DefaultHttpContext { User = principal };
            context.Request.Path = "/api/v1/links";
            context.Request.Method = "GET";
            await middleware.InvokeAsync(context, null!, new ConfigurationBuilder().Build(), null!);
        }
        Assert.Equal(4, calls);
    }
}

[Collection("links")]
[Trait("Category", "Integration")]
public sealed class MiddlewareTests(LinkEnvironment environment)
{
    [Fact]
    public async Task Malformed_json_content_type_and_peer_address_paths()
    {
        await using var host = await environment.CreateAsync();
        using var scope = host.App.Services.CreateScope();
        var calls = 0;
        var middleware = new AuthHttp(_ => { calls++; return Task.CompletedTask; });
        foreach (var (contentType, json, ip, expected) in new[] {
            ("text/plain", "invalid", (IPAddress?)null, 400),
            ("application/json", "{", (IPAddress?)null, 400),
            ("application/json", "{\"email\":\"nobody@example.test\",\"password\":\"long password\"}", IPAddress.Loopback, 200) })
        {
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = "POST";
            context.Request.Path = "/api/v1/auth/login";
            context.Request.Headers.Origin = "https://management.test";
            context.Request.ContentType = contentType;
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            context.Response.Body = new MemoryStream();
            context.Connection.RemoteIpAddress = ip;
            await middleware.InvokeAsync(context, new ValidAntiforgery(), scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                scope.ServiceProvider.GetRequiredService<PostgresRateLimiter>());
            Assert.Equal(expected, context.Response.StatusCode);
        }
        Assert.Equal(1, calls);
    }

    private sealed class ValidAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext context) => throw new NotSupportedException();
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(true);
        public void SetCookieTokenAndHeader(HttpContext context) { }
        public Task ValidateRequestAsync(HttpContext context) => Task.CompletedTask;
    }
}
