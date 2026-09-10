using System.Globalization;
using System.Text.Json;
using Shortener.Application;
using Shortener.Domain;
using Shortener.Infrastructure;
using Shortener.ServiceDefaults;

namespace Shortener.Api;

public static class LinkEndpoints
{
    public static void AddLinkManagement(this IServiceCollection services, IConfiguration configuration)
    {
        var shortOrigin = configuration["Origins:Short"]!;
        var hosts = configuration.GetSection("Origins:ShortAliases").GetChildren().Select(x => new Uri(x.Value!).IdnHost)
            .Append(new Uri(shortOrigin).IdnHost).ToArray();
        services.AddSingleton(new DestinationRules(hosts));
        services.AddScoped<LinkSequence>();
        services.AddSingleton<LinkCursor>();
        services.AddScoped(sp => new LinkService(configuration.GetConnectionString("Primary")!, shortOrigin,
            sp.GetRequiredService<DestinationRules>(), sp.GetRequiredService<LinkSequence>(), sp.GetRequiredService<LinkCursor>(), sp.GetRequiredService<TimeProvider>()));
    }

    public static void MapLinkManagement(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/links").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (LinkFailure error) { return AuthHttp.Problem(context.HttpContext, error.Status, error.Code); }
        });
        group.MapPost("", Create);
        group.MapGet("", List);
        group.MapGet("/{linkId}", async (string linkId, HttpContext http, LinkService links, CancellationToken ct) =>
            Results.Ok(await links.GetAsync(Owner(http), ParseId(linkId), ct)));
        group.MapPost("/{linkId}/deactivate", async (string linkId, HttpContext http, LinkService links, CancellationToken ct) =>
            Results.Ok(await links.DeactivateAsync(Owner(http), ParseId(linkId), ct)));
    }

    private static async Task<IResult> Create(HttpContext http, LinkService links, CancellationToken ct)
    {
        var keys = http.Request.Headers["Idempotency-Key"];
        if (keys.Count != 1 || !Guid.TryParseExact(keys[0], "D", out var key)) throw new LinkFailure(400, "invalid_input");
        if (!http.Request.HasJsonContentType()) throw new LinkFailure(400, "invalid_input");
        JsonDocument document;
        try { document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct); }
        catch (JsonException) { throw new LinkFailure(400, "invalid_input"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new LinkFailure(400, "invalid_input");
            if (root.EnumerateObject().Count() != 1 || !root.TryGetProperty("destinationUrl", out var value)
                || value.ValueKind != JsonValueKind.String) throw new LinkFailure(400, "invalid_input");
            var input = root.Deserialize<CreateLinkInput>(JsonSerializerOptions.Web)!;
            var result = await links.CreateAsync(Owner(http), key, input.DestinationUrl, ct);
            return Results.Created("/api/v1/links/" + result.Id.ToString(CultureInfo.InvariantCulture), result);
        }
    }

    private static async Task<IResult> List(HttpContext http, LinkService links, CancellationToken ct)
    {
        int? limit = null;
        if (http.Request.Query.TryGetValue("limit", out var values))
        {
            if (values.Count != 1 || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                throw new LinkFailure(400, "invalid_input");
            limit = number;
        }
        var cursor = http.Request.Query["cursor"];
        if (cursor.Count > 1) throw new LinkFailure(400, "invalid_cursor");
        return Results.Ok(await links.ListAsync(Owner(http), limit, cursor.Count == 0 ? null : cursor[0], ct));
    }

    private static Guid Owner(HttpContext http) => Guid.Parse(http.User.FindFirst("sub")!.Value);

    private static long ParseId(string value)
    {
        if (value.StartsWith('0') || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new LinkFailure(400, "invalid_input");
        return id;
    }
}
