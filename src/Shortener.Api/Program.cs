using Microsoft.AspNetCore.Antiforgery;
using Shortener.Application;
using Shortener.Infrastructure;
using Shortener.ServiceDefaults;
using Shortener.Api;

var builder = WebApplication.CreateBuilder(args);
builder.AddFoundation("shortener-api");
builder.Services.AddLinkManagement(builder.Configuration);
var app = builder.Build();
app.UseFoundation();
app.MapLinkManagement();

app.MapGet("/api/v1/auth/csrf", (IAntiforgery antiforgery, HttpContext http) =>
    Results.Ok(new { requestToken = antiforgery.GetAndStoreTokens(http).RequestToken }));

app.MapPost("/api/v1/auth/register", async (RegisterInput input, AuthService auth, IEmailSender email, IConfiguration config, CancellationToken ct) =>
{
    var created = await auth.CreateUserAsync(input, ct);
    if (created is { } result) await SendAction(email, config, result.User.Email!, result.Token, "verify-email", ct);
    return GenericMessage();
});

app.MapPost("/api/v1/auth/verify-email", async (ActionTokenInput input, AuthService auth, HttpContext http, CancellationToken ct) =>
    await auth.ConsumeActionAsync(input.Token, "verify_email", null, ct) ? Results.NoContent() : AuthHttp.Problem(http, 400, "invalid_token"));

app.MapPost("/api/v1/auth/resend-verification", async (EmailInput input, AuthService auth, IEmailSender email, IConfiguration config, CancellationToken ct) =>
{
    if (await auth.ResendVerificationAsync(input.Email, ct) is { } result)
        await SendAction(email, config, result.User.Email!, result.Token, "verify-email", ct);
    return GenericMessage();
});

app.MapPost("/api/v1/auth/login", async (LoginInput input, AuthService auth, JwtIssuer issuer, HttpContext http, CancellationToken ct) =>
{
    var result = await auth.LoginAsync(input, ct);
    if (result is null) return AuthHttp.Problem(http, 401, "unauthorized");
    http.Response.Cookies.Append("__Secure-refresh", result.Value.Refresh, RefreshCookie(result.Value.Session.ExpiresAt));
    return Results.Ok(issuer.Issue(result.Value.Session, result.Value.User));
});

app.MapPost("/api/v1/auth/forgot-password", async (EmailInput input, AuthService auth, IEmailSender email, IConfiguration config, CancellationToken ct) =>
{
    if (await auth.CreateResetAsync(input.Email, ct) is { } result)
        await SendAction(email, config, result.User.Email!, result.Token, "reset-password", ct);
    return GenericMessage();
});

app.MapPost("/api/v1/auth/reset-password", async (ResetPasswordInput input, AuthService auth, HttpContext http, CancellationToken ct) =>
    await auth.ConsumeActionAsync(input.Token, "reset_password", input.NewPassword, ct) ? Results.NoContent() : AuthHttp.Problem(http, 400, "invalid_token"));

app.MapPost("/api/v1/auth/refresh", async (HttpContext http, AuthService auth, JwtIssuer issuer, CancellationToken ct) =>
{
    if (!http.Request.Cookies.TryGetValue("__Secure-refresh", out var raw)) return AuthHttp.Problem(http, 401, "unauthorized");
    var result = await auth.RefreshAsync(raw, ct);
    if (result is null)
    {
        http.Response.Cookies.Delete("__Secure-refresh", RefreshCookie());
        return AuthHttp.Problem(http, 401, "unauthorized");
    }
    http.Response.Cookies.Append("__Secure-refresh", result.Value.Refresh, RefreshCookie(result.Value.Session.ExpiresAt));
    return Results.Ok(issuer.Issue(result.Value.Session, result.Value.User));
});

app.MapPost("/api/v1/auth/logout", async (HttpContext http, AuthService auth, CancellationToken ct) =>
{
    if (http.Request.Cookies.TryGetValue("__Secure-refresh", out var raw)) await auth.LogoutAsync(raw, ct);
    http.Response.Cookies.Delete("__Secure-refresh", RefreshCookie());
    return Results.NoContent();
});

app.MapGet("/api/v1/me", async (HttpContext http, AuthService auth, CancellationToken ct) =>
    await auth.GetUserAsync(Guid.Parse(http.User.FindFirst("sub")!.Value), Guid.Parse(http.User.FindFirst("sid")!.Value), ct) is { } user
        ? Results.Ok(user) : AuthHttp.Problem(http, 401, "unauthorized")).RequireAuthorization();

app.Run();

static IResult GenericMessage() => Results.Accepted(value: new { message = "Se os dados forem válidos, enviaremos instruções." });
static CookieOptions RefreshCookie(DateTimeOffset? expires = null) => new()
{ HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/api/v1/auth", Expires = expires };

static Task SendAction(IEmailSender email, IConfiguration config, string recipient, string token, string action, CancellationToken ct)
{
    var link = config["Origins:Management"]!.TrimEnd('/') + "/" + action + "#token=" + Uri.EscapeDataString(token);
    return email.SendAsync(recipient, action == "verify-email" ? "Confirme seu e-mail" : "Redefinição de senha",
        $"Abra {link} e confirme a ação na página. Abrir o endereço não consome o token.", ct);
}

public partial class Program;
