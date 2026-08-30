using System.Text;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Diagnostic DelegatingHandler for Superclass HTTP calls. Mirrors <see cref="RioPlayLoggingHandler"/>:
/// logs to <see cref="ILogger"/> and (credential-redacted) to <c>app_logs</c> for the admin logs page.
///
/// <para>SECURITY: the <c>Authorization</c> and <c>X-API-Key</c> headers are ALWAYS redacted — the API
/// key must never reach a log or the console. Full request/response BODIES are only written to app_logs
/// when the global <see cref="SuperclassSettings.LoggingEnabled"/> flag is on (resolved per call); status
/// lines are always logged. The multipart body carries no credentials (auth is header-only).</para>
/// </summary>
public class SuperclassLoggingHandler : DelegatingHandler
{
    private readonly ILogger<SuperclassLoggingHandler> _log;
    private readonly IServiceScopeFactory _scopeFactory;

    public SuperclassLoggingHandler(ILogger<SuperclassLoggingHandler> log, IServiceScopeFactory scopeFactory)
    {
        _log = log;
        _scopeFactory = scopeFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var reqHeaders = FormatHeaders(req.Headers, req.Content?.Headers);

        // Resolve whether verbose body logging is enabled. Failure to resolve → default to bodies-off
        // (the safer choice). This never touches the API key itself.
        var bodyLogging = await ResolveLoggingEnabledAsync();
        var reqBody = bodyLogging && req.Content != null ? await SafeReadAsync(req.Content, ct) : "(body logging disabled)";

        _log.LogInformation("SUPERCLASS → {Method} {Url}", req.Method, req.RequestUri);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await base.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "SUPERCLASS ✗ {Method} {Url} threw after {Ms}ms", req.Method, req.RequestUri, sw.ElapsedMilliseconds);
            await WriteAppLogAsync(AppLogLevel.Error,
                $"Network error talking to Superclass: {ex.Message}",
                "superclass.network_error",
                new
                {
                    Method = req.Method.Method,
                    Url = req.RequestUri?.ToString(),
                    RequestHeaders = reqHeaders,
                    Exception = ex.ToString(),
                });
            throw;
        }
        sw.Stop();

        var respBody = bodyLogging ? await SafeReadAsync(resp.Content, ct) : "(body logging disabled)";

        _log.LogInformation("SUPERCLASS ← {Status} {Method} {Url} ({Ms}ms)",
            (int)resp.StatusCode, req.Method, req.RequestUri, sw.ElapsedMilliseconds);

        var level = resp.IsSuccessStatusCode ? AppLogLevel.Information : AppLogLevel.Error;
        await WriteAppLogAsync(level,
            $"Superclass HTTP {(int)resp.StatusCode} {req.Method} {req.RequestUri?.AbsolutePath} ({sw.ElapsedMilliseconds}ms)",
            resp.IsSuccessStatusCode ? "superclass.http_ok" : "superclass.http_failed",
            new
            {
                Method = req.Method.Method,
                Url = req.RequestUri?.ToString(),
                StatusCode = (int)resp.StatusCode,
                ElapsedMs = sw.ElapsedMilliseconds,
                RequestHeaders = reqHeaders,
                RequestBody = Truncate(reqBody, 4000),
                ResponseBody = Truncate(respBody, 4000),
            });

        return resp;
    }

    private async Task<bool> ResolveLoggingEnabledAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetService<ISuperclassSettingsService>();
            if (settings == null) return false;
            var creds = await settings.ResolveAsync();
            return creds?.LoggingEnabled ?? false;
        }
        catch { return false; }
    }

    private static async Task<string> SafeReadAsync(HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadAsStringAsync(ct); }
        catch { return "(unreadable)"; }
    }

    private async Task WriteAppLogAsync(AppLogLevel level, string message, string eventCode, object properties)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetService<IAppLogService>();
            if (svc == null) return;
            await svc.LogAsync(level, "SerialKey.Superclass", message, eventCode: eventCode, properties: properties);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SuperclassLoggingHandler failed to persist app log");
        }
    }

    /// <summary>Formats headers, redacting the credential-bearing ones. Auth is header-only for
    /// Superclass, so this is the single place a key could otherwise leak into a log.</summary>
    private static string FormatHeaders(System.Net.Http.Headers.HttpHeaders main, System.Net.Http.Headers.HttpHeaders? content)
    {
        var sb = new StringBuilder();
        foreach (var h in main)
        {
            var val = string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(h.Key, "X-API-Key", StringComparison.OrdinalIgnoreCase)
                ? "<redacted>"
                : string.Join(", ", h.Value);
            sb.AppendLine($"  {h.Key}: {val}");
        }
        if (content != null)
            foreach (var h in content)
                sb.AppendLine($"  {h.Key}: {string.Join(", ", h.Value)}");
        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "… (truncated)";
}
