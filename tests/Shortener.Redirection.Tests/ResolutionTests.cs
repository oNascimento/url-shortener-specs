extern alias Redirector;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shortener.LinkManagement.Tests;
using Xunit;
using IRedirectResolver = Redirector::Shortener.Redirector.IRedirectResolver;
using ResolvedLink = Redirector::Shortener.Redirector.ResolvedLink;

namespace Shortener.Redirection.Tests;

[Collection("redirection")]
[Trait("Category", "Integration")]
public sealed class ResolutionTests(LinkEnvironment environment)
{
    internal static async Task Seed(LinkHost database, string code = "z", long id = 35, string destination = "https://destination.invalid/Case/%2F?a=1&a=2#fragment")
    {
        var (owner, client) = await database.UserAsync();
        client.Dispose();
        await database.SqlAsync("INSERT INTO links(id, code, owner_id, destination_url, created_at) VALUES($1,$2,$3,$4,now())",
            parameters: [id, code, owner, destination]);
    }

    [Fact]
    public async Task Exact_codes_methods_and_original_destination_match_contract()
    {
        await using var database = await environment.CreateAsync();
        await Seed(database);
        await Seed(database, "Z", 61, "https://other.invalid/");
        await Seed(database, "ZZZZZZZZZZZ", long.MaxValue);
        await Seed(database, "health", 100);
        using var host = new RedirectHost(database.App.Settings);
        using var client = host.Client();
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            foreach (var code in new[] { "z", "Z", "ZZZZZZZZZZZ", "health" })
            {
                using var response = await client.SendAsync(new HttpRequestMessage(method, "/" + code + "?ignored=evil"));
                Assert.Equal(HttpStatusCode.Found, response.StatusCode);
                Assert.Equal(code == "Z" ? "https://other.invalid/" : "https://destination.invalid/Case/%2F?a=1&a=2#fragment", response.Headers.Location!.OriginalString);
                Assert.True(response.Headers.CacheControl!.NoStore);
                Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
                Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            }
        }
        foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE" })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/z"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.Equal("GET, HEAD", string.Join(", ", response.Content.Headers.Allow));
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        foreach (var path in new[] { "/", "/a/b", "/z/", "/!", "/abcdefghijkl", "/missing", "/01", "/0" })
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
            {
                using var response = await client.SendAsync(new HttpRequestMessage(method, path));
                await Problem(response, 404, method == HttpMethod.Head);
            }
        }
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Committed_states_and_tombstones_are_generic_410_without_cache()
    {
        await using var database = await environment.CreateAsync();
        await Seed(database);
        using var host = new RedirectHost(database.App.Settings);
        using var client = host.Client();
        foreach (var (table, column) in new[] { ("links", "owner_disabled_at"), ("links", "admin_blocked_at"),
            ("users", "blocked_at"), ("users", "deletion_requested_at") })
        {
            using (var active = await client.GetAsync("/z")) Assert.Equal(HttpStatusCode.Found, active.StatusCode);
            await database.SqlAsync($"UPDATE {table} SET {column} = now()");
            using (var gone = await client.GetAsync("/z")) await Problem(gone, 410);
            using (var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/z"))) await Problem(head, 410, true);
            await database.SqlAsync($"UPDATE {table} SET {column} = NULL");
        }
        await database.SqlAsync("UPDATE links SET deleted_at = now(), owner_id = NULL, destination_url = NULL");
        using var tombstone = await client.GetAsync("/z");
        await Problem(tombstone, 410);
        Assert.DoesNotContain(host.Logs.Messages, line => line.Contains("destination.invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolution_failure_is_503_and_public_requests_do_not_authenticate()
    {
        await using var database = await environment.CreateAsync();
        await Seed(database);
        var (_, authenticated) = await database.UserAsync();
        using var host = new RedirectHost(database.App.Settings);
        using var client = host.Client();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        authenticated.Dispose();
        client.DefaultRequestHeaders.Add("Cookie", "__Secure-refresh=irrelevant");
        await database.SqlAsync("ALTER TABLE auth_sessions RENAME TO unavailable_sessions");
        using (var publicResponse = await client.GetAsync("/z")) Assert.Equal(HttpStatusCode.Found, publicResponse.StatusCode);
        await database.SqlAsync("ALTER TABLE links RENAME TO unavailable_links");
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var failed = await client.SendAsync(new HttpRequestMessage(method, "/z"));
            await Problem(failed, 503, method == HttpMethod.Head);
            Assert.Equal(TimeSpan.FromSeconds(1), failed.Headers.RetryAfter!.Delta);
        }
        using var timeoutHost = new RedirectHost(database.App.Settings, services =>
        {
            services.RemoveAll<IRedirectResolver>();
            services.AddSingleton<IRedirectResolver>(new TimeoutResolver());
        });
        using var timeoutClient = timeoutHost.Client();
        using var timeout = await timeoutClient.GetAsync("/z");
        await Problem(timeout, 503);
    }

    private sealed class TimeoutResolver : IRedirectResolver
    {
        public Task<ResolvedLink?> ResolveAsync(string code, CancellationToken cancellationToken) => throw new OperationCanceledException();
    }

    private static async Task Problem(HttpResponseMessage response, int expected, bool head = false)
    {
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        if (head) { Assert.Empty(await response.Content.ReadAsByteArrayAsync()); return; }
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("destination", problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
