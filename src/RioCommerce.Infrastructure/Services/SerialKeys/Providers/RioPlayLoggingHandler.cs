using System.Text;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Diagnostic-only DelegatingHandler that logs the full HTTP request and response for every
/// Rio call. Output goes to two places:
///   • <see cref="ILogger"/> at Information level (Visual Studio Output, console)
///   • <c>app_logs</c> table at Information (success) or Error (non-2xx response) — visible
///     from the admin /admin/logs page so problems can be diagnosed without console access
///
/// Secrets ARE redacted (secretKey, Authorization). Everything else is verbatim.
/// </summary>
public class RioPlayLoggingHandler : DelegatingHandler
{
    private readonly ILogger<RioPlayLoggingHandler> _log;
    private readonly IServiceScopeFactory _scopeFactory;

    public RioPlayLoggingHandler(ILogger<RioPlayLoggingHandler> log, IServiceScopeFactory scopeFactory)
    {
        _log = log;
        _scopeFactory = scopeFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var reqBody = req.Content != null ? await req.Content.ReadAsStringAsync(ct) : "";
        var reqHeaders = FormatHeaders(req.Headers, req.Content?.Headers);

        _log.LogInformation(
            "RIO → {Method} {Url}\n--- Request headers ---\n{Headers}\n--- Request body ---\n{Body}",
            req.Method, req.RequestUri, reqHeaders, Truncate(reqBody, 4000));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await base.SendAsync(req, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "RIO ✗ {Method} {Url} threw after {Ms}ms", req.Method, req.RequestUri, sw.ElapsedMilliseconds);
            // Network-level failure — surface to admin /admin/logs.
            await WriteAppLogAsync(AppLogLevel.Error,
                $"Network error talking to Rio: {ex.Message}",
                "rio.network_error",
                new
                {
                    Method = req.Method.Method,
                    Url = req.RequestUri?.ToString(),
                    RequestHeaders = reqHeaders,
                    RequestBody = Truncate(reqBody, 4000),
                    Exception = ex.ToString(),
                });
            throw;
        }
        sw.Stop();

        var respBody = await resp.Content.ReadAsStringAsync(ct);
        var respHeaders = FormatHeaders(resp.Headers, resp.Content?.Headers);

        _log.LogInformation(
            "RIO ← {Status} {Method} {Url} ({Ms}ms)\n--- Response headers ---\n{Headers}\n--- Response body ---\n{Body}",
            (int)resp.StatusCode, req.Method, req.RequestUri, sw.ElapsedMilliseconds, respHeaders, Truncate(respBody, 4000));

        // Persist a structured record per call so the admin can inspect from /admin/logs.
        // Success calls go in at Information; non-success (4xx/5xx) go in at Error so they
        // surface in the default filter ("Information +" still shows them; "Error +" shows
        // only these). The full request and response are stored so the admin can compare
        // against a working Postman call without needing console access.
        var level = resp.IsSuccessStatusCode ? AppLogLevel.Information : AppLogLevel.Error;
        await WriteAppLogAsync(level,
            $"Rio HTTP {(int)resp.StatusCode} {req.Method} {req.RequestUri?.AbsolutePath} ({sw.ElapsedMilliseconds}ms)",
            resp.IsSuccessStatusCode ? "rio.http_ok" : "rio.http_failed",
            new
            {
                Method = req.Method.Method,
                Url = req.RequestUri?.ToString(),
                StatusCode = (int)resp.StatusCode,
                ElapsedMs = sw.ElapsedMilliseconds,
                RequestHeaders = reqHeaders,
                RequestBody = Truncate(reqBody, 4000),
                ResponseHeaders = respHeaders,
                ResponseBody = Truncate(respBody, 4000),
            });

        return resp;
    }

    /// <summary>Writes to app_logs in a fresh scope so the parent caller's DbContext doesn't get
    /// tangled with this handler's writes. Swallows any failure — diagnostic logging must never
    /// derail the call it's observing.</summary>
    private async Task WriteAppLogAsync(AppLogLevel level, string message, string eventCode, object properties)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetService<IAppLogService>();
            if (svc == null) return;
            await svc.LogAsync(level, "SerialKey.Rio", message, eventCode: eventCode, properties: properties);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RioPlayLoggingHandler failed to persist app log");
        }
    }

    private static string FormatHeaders(System.Net.Http.Headers.HttpHeaders main, System.Net.Http.Headers.HttpHeaders? content)
    {
        var sb = new StringBuilder();
        foreach (var h in main)
        {
            var val = string.Equals(h.Key, "secretKey", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
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
