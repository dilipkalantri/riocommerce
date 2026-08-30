using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.API.Middleware;

/// <summary>
/// Intercepts incoming GET requests, looks up the request path in the
/// url_redirects table, and emits a 301 (or 302) when an active match exists.
///
/// Bypasses admin / api / SignalR / static-content paths so the middleware
/// never short-circuits framework infrastructure. Hit counting is
/// fire-and-forget so the redirect itself stays sub-millisecond.
///
/// Registered BEFORE UseStaticFiles + UseRouting in Program.cs so legacy URLs
/// like /ca-foundation can be redirected before the routing system even gets
/// a chance to 404 them.
/// </summary>
public sealed class UrlRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UrlRedirectMiddleware> _log;

    public UrlRedirectMiddleware(RequestDelegate next, ILogger<UrlRedirectMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, IUrlRedirectService svc)
    {
        // Only act on GET requests — never redirect a POST (e.g. SignalR form posts).
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? string.Empty;
        if (ShouldSkip(path))
        {
            await _next(ctx);
            return;
        }

        // ── Legacy entity-type-prefixed storefront URLs → 301 to the clean root slug ──
        // /category/{slug}, /course/{slug}, /product/{slug} → /{slug} (preserving query string).
        // Runs before routing so old Google-indexed links keep working with a permanent redirect.
        foreach (var prefix in LegacyPrefixes)
        {
            if (path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var clean = "/" + path[prefix.Length..].TrimStart('/');
                ctx.Response.StatusCode = 301;
                ctx.Response.Headers.Location = clean + ctx.Request.QueryString;
                return;
            }
        }

        var match = await svc.FindActiveAsync(path, ctx.RequestAborted);
        if (match != null)
        {
            // Defensive: never redirect to the same path. A rule like '/'→'/' or
            // '/foo'→'/foo' would otherwise produce ERR_TOO_MANY_REDIRECTS. The
            // service-layer Normalise() already rejects new rows like this, but
            // older rows or absolute-URL destinations are caught here too.
            var destPath = match.NewUrl;
            if (Uri.TryCreate(destPath, UriKind.Absolute, out var abs)) destPath = abs.AbsolutePath;
            if (string.Equals(destPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Skipping self-redirect for {Path} (rule {Id}) to prevent loop", path, match.Id);
                await _next(ctx);
                return;
            }

            // Fire-and-forget hit increment — failures don't block the redirect.
            _ = Task.Run(async () =>
            {
                try { await svc.IncrementHitAsync(match.Id); }
                catch (Exception ex) { _log.LogWarning(ex, "Failed to increment hit count for redirect {Id}", match.Id); }
            });

            ctx.Response.StatusCode = match.RedirectType == 302 ? 302 : 301;
            ctx.Response.Headers.Location = match.NewUrl;
            return;
        }

        await _next(ctx);
    }

    private static readonly string[] LegacyPrefixes = { "/category/", "/course/", "/product/" };

    private static bool ShouldSkip(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return false;
        // Framework / app reserved paths — never redirect these.
        if (path.StartsWith("/admin",     StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api",       StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_blazor",   StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_framework",StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content",  StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/account",   StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/bookpreview", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/signalr",   StringComparison.OrdinalIgnoreCase))
            return true;
        // Static assets — anything with a file extension in the last segment.
        var lastSlash = path.LastIndexOf('/');
        var lastSeg = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        return lastSeg.Contains('.');
    }
}
