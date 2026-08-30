using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly RioCommerceDbContext _db;
    private readonly IRequestContext _req;
    public AuditService(RioCommerceDbContext db, IRequestContext req) { _db = db; _req = req; }

    // ── Legacy short overload — every existing call site keeps compiling. ──
    public Task LogAsync(Guid? actorId, string actorName, string action, string entityType, string? entityId, string? details)
        => WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = actorName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            Module = InferModuleFromAction(action),
            Status = "Success"
        });

    public async Task WriteAsync(AuditEntry e)
    {
        var ip = _req.IpAddress;
        var ua = _req.UserAgent;
        var (browser, device) = ParseUserAgent(ua);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = e.ActorUserId,
            ActorName = string.IsNullOrWhiteSpace(e.ActorName) ? "system" : e.ActorName,
            Module = string.IsNullOrWhiteSpace(e.Module) ? "Other" : e.Module,
            Action = e.Action,
            EntityType = e.EntityType,
            EntityId = e.EntityId,
            EntityName = e.EntityName,
            OldValue = e.OldValue,
            NewValue = e.NewValue,
            Status = string.IsNullOrWhiteSpace(e.Status) ? "Success" : e.Status,
            IpAddress = ip,
            UserAgent = string.IsNullOrWhiteSpace(ua) ? null : ua,
            Browser = browser,
            Device = device,
            Details = e.Details
        });
        await _db.SaveChangesAsync();
    }

    // Tiny built-in UA parser — keeps Browser + Device columns usable for the table without
    // pulling in a heavy library. Falls back to "Other" so display always reads something.
    private static (string browser, string device) ParseUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return ("Unknown", "Unknown");
        string browser =
              ua.Contains("Edg/")        ? "Edge"
            : ua.Contains("OPR/")        ? "Opera"
            : ua.Contains("Firefox/")    ? "Firefox"
            : ua.Contains("Chrome/")     ? "Chrome"
            : ua.Contains("Safari/")     ? "Safari"
            : "Other";
        string device =
              ua.Contains("Android")     ? "Android"
            : ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS"
            : ua.Contains("Windows")     ? "Windows"
            : ua.Contains("Mac OS X")    ? "macOS"
            : ua.Contains("Linux")       ? "Linux"
            : "Other";
        return (browser, device);
    }

    // Best-effort module inference for legacy callers — most action labels already carry the module
    // prefix (e.g. "ProductPriceChanged", "CustomerCreated", "OrderStatusUpdated").
    private static string InferModuleFromAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "Other";
        var a = action.ToLowerInvariant();
        if (a.StartsWith("product"))     return "Products";
        if (a.StartsWith("customer"))    return "Customers";
        if (a.StartsWith("order"))       return "Orders";
        if (a.StartsWith("payment") || a.StartsWith("refund")) return "Payments";
        if (a.StartsWith("faculty"))     return "Faculty";
        if (a.StartsWith("coupon") || a.StartsWith("promotion") || a.StartsWith("offer")) return "Promotions";
        if (a.StartsWith("permission") || a.StartsWith("role") || a.StartsWith("user")) return "AdminAccess";
        if (a.StartsWith("login") || a.StartsWith("logout") || a.StartsWith("auth")) return "Auth";
        if (a.StartsWith("setting") || a.StartsWith("config")) return "Settings";
        return "Other";
    }

    public async Task<List<AuditLogItem>> ListAsync(int take = 100) =>
        await _db.AuditLogs.OrderByDescending(a => a.CreatedAt).Take(take)
            .Select(a => new AuditLogItem(a.Id, a.ActorName, a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt))
            .ToListAsync();

    public async Task<List<AuditLogItem>> ListForEntityAsync(string entityType, string entityId, int take = 100) =>
        await _db.AuditLogs.Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt).Take(take)
            .Select(a => new AuditLogItem(a.Id, a.ActorName, a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt))
            .ToListAsync();
}
