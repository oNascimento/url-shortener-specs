using System.Diagnostics;

namespace Shortener.Redirector;

public sealed class RedirectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IRedirectResolver resolver)
    {
        var path = context.Request.Path.Value ?? "";
        if (path is "/health/live" or "/health/ready")
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        if (path.Length < 2 || path.AsSpan(1).Contains('/'))
        {
            await ProblemAsync(context, 404, "not_found");
            return;
        }
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.Headers.Allow = "GET, HEAD";
            context.Response.StatusCode = 405;
            return;
        }
        var code = path[1..];
        if (code.Length > 11 || !code.All(char.IsAsciiLetterOrDigit))
        {
            await ProblemAsync(context, 404, "not_found");
            return;
        }
        ResolvedLink? link;
        try
        {
            link = await resolver.ResolveAsync(code, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
            return;
        }
        catch (Exception)
        {
            context.Response.Headers.RetryAfter = "1";
            await ProblemAsync(context, 503, "service_unavailable");
            return;
        }
        if (link is null)
        {
            await ProblemAsync(context, 404, "not_found");
            return;
        }
        if (link.Unavailable)
        {
            await ProblemAsync(context, 410, "gone");
            return;
        }
        context.Response.StatusCode = 302;
        context.Response.Headers.Location = link.Destination;
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static async Task ProblemAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        if (HttpMethods.IsHead(context.Request.Method)) return;
        await Results.Problem(statusCode: status, type: "about:blank", title: "Link unavailable",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
            }).ExecuteAsync(context);
    }
}
