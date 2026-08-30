using System.Text.Json;
using RioCommerce.Core.DTOs.Logs;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Writes one row to <c>app_logs</c> per call. Wrapped in try/catch and a SEPARATE
/// DbContext when used in fire-and-forget contexts — a logging failure must never bring
/// down the caller.
/// </summary>
public sealed class AppLogService : IAppLogService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly string Host = Environment.MachineName;

    private readonly RioCommerceDbContext _db;
    private readonly ILogger<AppLogService> _log;

    public AppLogService(RioCommerceDbContext db, ILogger<AppLogService> log)
    {
        _db = db; _log = log;
    }

    public async Task LogAsync(AppLogLevel level, string category, string message,
        string? eventCode = null, Exception? exception = null, object? properties = null,
        Guid? orderId = null, Guid? userId = null, string? entityType = null, string? entityId = null,
        CancellationToken ct = default)
    {
        try
        {
            var row = new AppLog
            {
                Id = Guid.NewGuid(),
                Level = level,
                Category = category.Length > 120 ? category[..120] : category,
                EventCode = eventCode?.Length > 80 ? eventCode[..80] : eventCode,
                Message = message.Length > 2000 ? message[..2000] : message,
                Exception = exception?.ToString(),
                Properties = properties != null ? JsonSerializer.Serialize(properties, JsonOpts) : null,
                UserId = userId,
                OrderId = orderId,
                EntityType = entityType,
                EntityId = entityId,
                Source = Host,
            };
            _db.Set<AppLog>().Add(row);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Last-resort: write the failure to the console logger so the operator at least
            // sees that the persistent log is unreachable. We swallow because callers depend
            // on log writes being side-effect-only.
            _log.LogError(ex, "AppLog write failed level={Level} category={Category} msg={Msg}",
                level, category, message);
        }
    }

    public Task InfoAsync(string category, string message, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default)
        => LogAsync(AppLogLevel.Information, category, message, eventCode, null, properties, orderId, ct: ct);

    public Task WarnAsync(string category, string message, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default)
        => LogAsync(AppLogLevel.Warning, category, message, eventCode, null, properties, orderId, ct: ct);

    public Task ErrorAsync(string category, string message, Exception? ex = null, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default)
        => LogAsync(AppLogLevel.Error, category, message, eventCode, ex, properties, orderId, ct: ct);

    public Task CriticalAsync(string category, string message, Exception? ex = null, string? eventCode = null, object? properties = null, Guid? orderId = null, CancellationToken ct = default)
        => LogAsync(AppLogLevel.Critical, category, message, eventCode, ex, properties, orderId, ct: ct);

    // ── Admin query surface ──

    public async Task<List<AppLogItem>> ListAsync(AppLogFilter filter, CancellationToken ct = default)
    {
        var q = _db.Set<AppLog>().AsNoTracking().AsQueryable();

        if (filter.MinLevel.HasValue) q = q.Where(l => l.Level >= filter.MinLevel.Value);
        if (!string.IsNullOrWhiteSpace(filter.Category)) q = q.Where(l => l.Category == filter.Category);
        if (!string.IsNullOrWhiteSpace(filter.EventCode)) q = q.Where(l => l.EventCode == filter.EventCode);
        if (filter.OrderId.HasValue) q = q.Where(l => l.OrderId == filter.OrderId);
        if (filter.UserId.HasValue)  q = q.Where(l => l.UserId == filter.UserId);
        if (filter.FromUtc.HasValue) q = q.Where(l => l.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)   q = q.Where(l => l.CreatedAt <= filter.ToUtc.Value);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var s = $"%{filter.Query.Trim()}%";
            q = q.Where(l => EF.Functions.ILike(l.Message, s)
                          || (l.EventCode != null && EF.Functions.ILike(l.EventCode, s))
                          || (l.Properties != null && EF.Functions.ILike(l.Properties, s)));
        }

        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 500);

        return await q.OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(l => Map(l))
            .ToListAsync(ct);
    }

    public async Task<AppLogItem?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var l = await _db.Set<AppLog>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return l == null ? null : Map(l);
    }

    public Task<List<string>> ListCategoriesAsync(CancellationToken ct = default)
        => _db.Set<AppLog>().AsNoTracking()
            .Select(l => l.Category).Distinct().OrderBy(c => c).ToListAsync(ct);

    public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        // ExecuteDelete (EF Core 7+) emits a single DELETE statement — fast for bulk purges.
        return await _db.Set<AppLog>().Where(l => l.CreatedAt < cutoffUtc).ExecuteDeleteAsync(ct);
    }

    private static AppLogItem Map(AppLog l) => new()
    {
        Id = l.Id,
        Level = l.Level,
        Category = l.Category,
        EventCode = l.EventCode,
        Message = l.Message,
        Exception = l.Exception,
        Properties = l.Properties,
        UserId = l.UserId,
        OrderId = l.OrderId,
        EntityType = l.EntityType,
        EntityId = l.EntityId,
        RequestPath = l.RequestPath,
        Source = l.Source,
        CreatedAt = l.CreatedAt,
    };
}
