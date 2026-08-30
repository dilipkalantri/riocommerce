using RioCommerce.Core.DTOs.Logs;
using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Application-level logging to the persistent <c>app_logs</c> table. Distinct from
/// <c>Microsoft.Extensions.Logging</c> (which writes to console / Serilog / etc.) — this
/// one writes structured rows that the admin can query from /admin/logs.
///
/// Convention: callers should ALSO call <c>_log.LogXxx(...)</c> for the console — both
/// have value (console is faster for live tail; the DB row is searchable forever).
/// </summary>
public interface IAppLogService
{
    /// <summary>Insert one row. Never throws — log writes must not break the calling flow.</summary>
    Task LogAsync(AppLogLevel level, string category, string message,
        string? eventCode = null,
        Exception? exception = null,
        object? properties = null,
        Guid? orderId = null,
        Guid? userId = null,
        string? entityType = null,
        string? entityId = null,
        CancellationToken ct = default);

    // ── Convenience wrappers ──
    Task InfoAsync(string category, string message, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default);
    Task WarnAsync(string category, string message, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default);
    Task ErrorAsync(string category, string message, Exception? ex = null, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default);
    Task CriticalAsync(string category, string message, Exception? ex = null, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default);

    // ── Admin query surface ──
    Task<List<AppLogItem>> ListAsync(AppLogFilter filter, CancellationToken ct = default);
    Task<AppLogItem?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Returns distinct categories observed so far — drives the Category dropdown filter.</summary>
    Task<List<string>> ListCategoriesAsync(CancellationToken ct = default);
    /// <summary>Hard-deletes rows older than the given cutoff. Returns rows removed.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
