using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shortener.Application;
using Xunit;

namespace Shortener.LinkManagement.Tests;

[Collection("links")]
[Trait("Category", "Integration")]
public sealed class HttpTests(LinkEnvironment environment)
{
    [Fact]
    [Trait("Requirement", "T03,RF-07")]
    public async Task Response_loss_retries_share_the_committed_link()
    {
        await using var host = await environment.CreateAsync();
        var (_, authenticated) = await host.UserAsync();
        using var client = new HttpClient(new LoseFirstResponse(host.App.Server.CreateHandler())) { BaseAddress = authenticated.BaseAddress };
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        var key = Guid.NewGuid().ToString();
        const string body = "{\"destinationUrl\":\"https://example.test/response-lost\"}";
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(client, body, key));
        var committed = (long)(await host.SqlAsync("SELECT id FROM links"))!;
        using var retry = await Create(client, body, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(committed.ToString(CultureInfo.InvariantCulture), (await Contract(retry, "Link")).GetProperty("id").GetString());
        Assert.Equal(1L, await host.SqlAsync("SELECT count(*) FROM links"));
    }

    private sealed class LoseFirstResponse(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private bool lose = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (lose)
            {
                lose = false;
                response.Dispose();
                throw new HttpRequestException("Simulated lost response after server commit");
            }
            return response;
        }
    }

    [Fact]
    [Trait("Requirement", "T20,T08,AF-02,AF-03,AF-12")]
    public async Task Contract_precision_owner_isolation_and_idempotent_http_results()
    {
        await using var host = await environment.CreateAsync();
        var (owner, client) = await host.UserAsync();
        var (_, other) = await host.UserAsync();
        await host.SqlAsync("UPDATE id_reservations SET upper_bound = 9007199254740992", true);
        var key = Guid.NewGuid().ToString();
        const string url = "https://example.test/Case/%2F?a=1&a=2#fragment";
        using var created = await Create(client, "{\"destinationUrl\":\"" + url + "\"}", key);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = await Contract(created, "Link");
        Assert.Equal("9007199254740993", link.GetProperty("id").GetString());
        Assert.Equal(url, link.GetProperty("destinationUrl").GetString());
        Assert.Equal("https://s.test/" + link.GetProperty("code").GetString(), link.GetProperty("shortUrl").GetString());
        var path = "/api/v1/links/" + link.GetProperty("id").GetString();
        Assert.Equal(path, created.Headers.Location!.OriginalString);
        using var read = await client.GetAsync(path);
        Assert.Equal(link.GetRawText(), (await Contract(read, "Link")).GetRawText());
        using var list = await client.GetAsync("/api/v1/links?limit=1");
        var page = await Contract(list, "LinkPage");
        Assert.Single(page.GetProperty("items").EnumerateArray());
        using var replay = await Create(client, "{\"destinationUrl\":\" " + url + " \"}", key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(link.GetRawText(), (await Contract(replay, "Link")).GetRawText());
        using var conflict = await Create(client, "{\"destinationUrl\":\"https://other.test\"}", key);
        await Problem(conflict, 409, "idempotency_conflict");
        using var otherGet = await other.GetAsync(path);
        await Problem(otherGet, 404, "not_found");
        using var otherDisable = await other.PostAsync(path + "/deactivate", null);
        await Problem(otherDisable, 404, "not_found");
        using var otherList = await other.GetAsync("/api/v1/links");
        Assert.Empty((await Contract(otherList, "LinkPage")).GetProperty("items").EnumerateArray());
        using var disable = await client.PostAsync(path + "/deactivate", null);
        Assert.Equal("disabled", (await Contract(disable, "Link")).GetProperty("status").GetString());
        using var replayDisabled = await Create(client, "{\"destinationUrl\":\"" + url + "\"}", key);
        Assert.Equal("disabled", (await Contract(replayDisabled, "Link")).GetProperty("status").GetString());
        using var fresh = await Create(client, "{\"destinationUrl\":\"" + url + "\"}", Guid.NewGuid().ToString());
        Assert.NotEqual(link.GetProperty("id").GetString(), (await Contract(fresh, "Link")).GetProperty("id").GetString());
        using var edit = await client.PutAsJsonAsync(path, new { destinationUrl = "https://other.test" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, edit.StatusCode);
        Assert.DoesNotContain(host.App.Logs.Messages, message => message.Contains(url, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Requirement", "T20,T17,RF-06,RF-08")]
    public async Task Malformed_requests_are_contract_problems_without_mutations()
    {
        await using var host = await environment.CreateAsync();
        var (_, client) = await host.UserAsync();
        var key = Guid.NewGuid().ToString();
        foreach (var json in new[] { "null", "[]", "{", "{}", "{\"unknown\":\"x\"}", "{\"destinationUrl\":null}",
            "{\"destinationUrl\":1}", "{\"destinationUrl\":\"https://example.test\",\"extra\":1}",
            "{\"destinationUrl\":\"https://example.test\",\"destinationUrl\":\"https://other.test\"}",
            "{\"destinationUrl\":\"https://alias.test\"}", "{\"destinationUrl\":\"relative\"}" })
        {
            using var response = await Create(client, json, key);
            await Problem(response, 400, "invalid_input");
        }
        foreach (var invalid in new string?[] { null, "bad", Guid.NewGuid().ToString("N"), key + "," + key })
        {
            using var response = await Create(client, "{\"destinationUrl\":\"https://example.test\"}", invalid);
            await Problem(response, 400, "invalid_input");
        }
        using (var nonJson = await Create(client, "https://example.test", key, "text/plain")) await Problem(nonJson, 400, "invalid_input");
        foreach (var id in new[] { "0", "01", "-1", "bad", "9223372036854775808" })
        {
            using var response = await client.GetAsync("/api/v1/links/" + id);
            await Problem(response, 400, "invalid_input");
        }
        foreach (var query in new[] { "limit=0", "limit=101", "limit=x", "limit=1&limit=2", "limit=" })
        {
            using var response = await client.GetAsync("/api/v1/links?" + query);
            await Problem(response, 400, "invalid_input");
        }
        foreach (var query in new[] { "cursor=", "cursor=bad", "cursor=a&cursor=b" })
        {
            using var response = await client.GetAsync("/api/v1/links?" + query);
            await Problem(response, 400, "invalid_cursor");
        }
        Assert.Equal(0L, await host.SqlAsync("SELECT count(*) FROM links"));
        Assert.Equal(0L, await host.SqlAsync("SELECT count(*) FROM creation_requests"));
    }

    [Fact]
    [Trait("Requirement", "T20,AF-02,F003-T05")]
    public async Task Auth_revocation_cookie_rejection_and_shared_rate_limits()
    {
        await using var host = await environment.CreateAsync();
        var (owner, client) = await host.UserAsync();
        using var anonymous = host.App.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://management.test") });
        anonymous.DefaultRequestHeaders.Add("Cookie", "__Secure-refresh=not-an-identity");
        using (var response = await anonymous.GetAsync("/api/v1/links")) await Problem(response, 401, "unauthorized");
        using (var response = await anonymous.PostAsJsonAsync("/api/v1/links", new { destinationUrl = "https://example.test" })) await Problem(response, 401, "unauthorized");
        using var replica = host.App.Replica();
        using var second = replica.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://management.test") });
        second.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        for (var i = 0; i < 60; i++)
        {
            using var response = await Create(i % 2 == 0 ? client : second, "{\"destinationUrl\":\"https://example.test\"}", Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        using (var limited = await Create(second, "{\"destinationUrl\":\"https://example.test\"}", Guid.NewGuid().ToString()))
        {
            await Problem(limited, 429, "rate_limited");
            Assert.NotNull(limited.Headers.RetryAfter);
        }
        using (var read = await client.GetAsync("/api/v1/links"))
        {
            var first = await Contract(read, "LinkPage");
            Assert.Equal(50, first.GetProperty("items").GetArrayLength());
            using var next = await second.GetAsync("/api/v1/links?cursor=" + Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!));
            Assert.Equal(10, (await Contract(next, "LinkPage")).GetProperty("items").GetArrayLength());
        }
        host.App.Clock.Advance(TimeSpan.FromMinutes(1));
        using (var reset = await Create(client, "{\"destinationUrl\":\"https://example.test\"}", Guid.NewGuid().ToString())) Assert.Equal(HttpStatusCode.Created, reset.StatusCode);
        using (var renewed = await client.GetAsync("/api/v1/links")) renewed.EnsureSuccessStatusCode();
        await host.SqlAsync("UPDATE auth_rate_limits SET count = 300, expires_at = now() + interval '10 minutes' WHERE key LIKE 'user:%'");
        using (var exhausted = await second.GetAsync("/api/v1/links")) await Problem(exhausted, 429, "rate_limited");
        foreach (var column in new[] { "blocked_at", "deletion_requested_at" })
        {
            await host.SqlAsync($"UPDATE users SET {column} = now() WHERE id = $1", parameters: [owner]);
            using var rejected = await client.GetAsync("/api/v1/links");
            await Problem(rejected, 401, "unauthorized");
            await host.SqlAsync($"UPDATE users SET {column} = NULL WHERE id = $1", parameters: [owner]);
        }
        await host.SqlAsync("UPDATE auth_sessions SET revoked_at = now() WHERE user_id = $1", parameters: [owner]);
        using var revoked = await client.GetAsync("/api/v1/links");
        await Problem(revoked, 401, "unauthorized");
    }

    [Fact]
    [Trait("Requirement", "T20,T21,T22")]
    public async Task Allocation_and_database_failures_are_explicit_503()
    {
        await using var host = await environment.CreateAsync();
        var (_, client) = await host.UserAsync();
        await host.SqlAsync("UPDATE link_allocation SET suspended = true");
        using (var suspended = await Create(client, "{\"destinationUrl\":\"https://example.test\"}", Guid.NewGuid().ToString())) await Problem(suspended, 503, "creation_suspended");
        await host.SqlAsync("UPDATE link_allocation SET suspended = false");
        await host.SqlAsync("DROP TABLE id_reservations", true);
        using (var unavailable = await Create(client, "{\"destinationUrl\":\"https://example.test\"}", Guid.NewGuid().ToString())) await Problem(unavailable, 503, "reservation_unavailable");
        await host.SqlAsync("ALTER TABLE links RENAME TO unavailable_links");
        using var database = await client.GetAsync("/api/v1/links");
        await Problem(database, 503, "service_unavailable");
    }

    private static Task<HttpResponseMessage> Create(HttpClient client, string body, string? key, string contentType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/links") { Content = new StringContent(body, Encoding.UTF8, contentType) };
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await Contract(response, "Problem");
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("traceId").GetString()));
    }

    private static async Task<JsonElement> Contract(HttpResponseMessage response, string model)
    {
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../.specs/contracts/openapi.json")));
        AssertSchema(document.RootElement, contract.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(model), contract.RootElement);
        return document.RootElement.Clone();
    }

    private static void AssertSchema(JsonElement value, JsonElement schema, JsonElement contract)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            AssertSchema(value, contract.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]), contract);
            return;
        }
        if (schema.TryGetProperty("anyOf", out var variants))
        {
            foreach (var variant in variants.EnumerateArray())
            {
                try { AssertSchema(value, variant, contract); return; } catch (Xunit.Sdk.XunitException) { }
            }
            Assert.Fail("No schema alternative matched " + value.GetRawText());
        }
        var type = schema.GetProperty("type");
        if (type.ValueKind == JsonValueKind.Array)
        {
            if (value.ValueKind == JsonValueKind.Null) { Assert.Contains(type.EnumerateArray(), x => x.GetString() == "null"); return; }
        }
        var name = type.ValueKind == JsonValueKind.Array ? type[0].GetString() : type.GetString();
        switch (name)
        {
            case "null": Assert.Equal(JsonValueKind.Null, value.ValueKind); break;
            case "object":
                Assert.Equal(JsonValueKind.Object, value.ValueKind);
                foreach (var required in schema.GetProperty("required").EnumerateArray()) Assert.True(value.TryGetProperty(required.GetString()!, out _));
                foreach (var property in value.EnumerateObject())
                {
                    Assert.True(schema.GetProperty("properties").TryGetProperty(property.Name, out var child));
                    AssertSchema(property.Value, child, contract);
                }
                break;
            case "array":
                Assert.Equal(JsonValueKind.Array, value.ValueKind);
                foreach (var item in value.EnumerateArray()) AssertSchema(item, schema.GetProperty("items"), contract);
                break;
            case "integer": Assert.True(value.TryGetInt32(out _)); break;
            case "string":
                Assert.Equal(JsonValueKind.String, value.ValueKind);
                var text = value.GetString()!;
                if (schema.TryGetProperty("maxLength", out var maximum)) Assert.True(text.Length <= maximum.GetInt32());
                if (schema.TryGetProperty("pattern", out var pattern)) Assert.Matches(pattern.GetString()!, text);
                if (schema.TryGetProperty("enum", out var values)) Assert.Contains(values.EnumerateArray(), x => x.GetString() == text);
                if (schema.TryGetProperty("format", out var format) && format.GetString() == "date-time") Assert.True(DateTimeOffset.TryParse(text, out _));
                break;
            default: Assert.Fail("Unsupported schema type: " + name); break;
        }
    }
}
