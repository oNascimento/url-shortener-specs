using System.Diagnostics;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Shortener.Infrastructure;

namespace Shortener.ServiceDefaults;

public sealed class AuthHttp(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery, IConfiguration config, PostgresRateLimiter limiter)
    {
        if (context.Request.Method == "POST" && context.Request.Path.StartsWithSegments("/api/v1/auth", out var remaining))
        {
            // Authentication endpoints always use the same anonymous antiforgery identity.
            if (!string.Equals(context.Request.Headers.Origin, config["Origins:Management"], StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(context.Request.Headers.Origin))
            { await WriteProblemAsync(context, 403, "forbidden"); return; }
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { await WriteProblemAsync(context, 403, "forbidden"); return; }
            var operation = remaining.Value!.Trim('/');
            string? email = null;
            if (operation is "register" or "verify-email" or "resend-verification" or "login" or "forgot-password" or "reset-password")
            {
                if (!context.Request.HasJsonContentType()) { await WriteProblemAsync(context, 400, "invalid_input"); return; }
                context.Request.EnableBuffering();
                try
                {
                    using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                    var root = document.RootElement;
                    if (!ValidInput(operation, root)) { await WriteProblemAsync(context, 400, "invalid_input"); return; }
                    if (root.TryGetProperty("email", out var property)) email = property.GetString();
                }
                catch (JsonException) { await WriteProblemAsync(context, 400, "invalid_input"); return; }
                finally { context.Request.Body.Position = 0; }
            }
            var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unavailable";
            if (await RejectLimitedAsync(context, limiter, AuthRateLimits.ForAuth(operation, ip, email))) return;
        }
        else if (context.Request.Path.StartsWithSegments("/api/v1") && context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            if (await RejectLimitedAsync(context, limiter, [AuthRateLimits.ForUser(userId,
                context.Request.Method == "POST" && context.Request.Path.Value!.TrimEnd('/').Equals("/api/v1/links", StringComparison.OrdinalIgnoreCase))])) return;
        }
        await next(context);
    }

    private static bool ValidInput(string operation, JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return false;
        static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        var allowed = operation switch
        {
            "register" or "login" => new[] { "email", "password" },
            "resend-verification" or "forgot-password" => ["email"],
            "reset-password" => ["token", "newPassword"],
            _ => ["token"]
        };
        if (input.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal))
            || input.EnumerateObject().Count() != allowed.Length
            || input.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != allowed.Length) return false;
        if (allowed.Contains("email"))
        {
            var email = Text(input, "email")?.Trim();
            if (email is null || email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || !email.Contains('@')) return false;
        }
        if (allowed.Contains("password"))
        {
            var password = Text(input, "password");
            if (password is null || password.Length > 128 || password.Length < (operation == "login" ? 1 : 12)) return false;
        }
        if (allowed.Contains("newPassword") && Text(input, "newPassword") is not { Length: >= 12 and <= 128 }) return false;
        if (allowed.Contains("token") && Text(input, "token") is not { Length: >= 1 and <= 2048 }) return false;
        return true;
    }

    private static async Task<bool> RejectLimitedAsync(HttpContext context, PostgresRateLimiter limiter, RateLimitRule[] rules)
    {
        var retryAfter = await limiter.AcquireAsync(rules, context.RequestAborted);
        if (retryAfter == 0) return false;
        context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await WriteProblemAsync(context, 429, "rate_limited");
        return true;
    }

    public static IResult Problem(HttpContext context, int status, string code) => Results.Problem(statusCode: status, type: "about:blank",
        title: status switch { 400 => "Dados inválidos", 401 => "Autenticação inválida", 403 => "Acesso negado", 429 => "Aguarde antes de tentar novamente", 503 => "Serviço indisponível", _ => "Falha na solicitação" },
        extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier });

    public static Task WriteProblemAsync(HttpContext context, int status, string code) => Problem(context, status, code).ExecuteAsync(context);
}
