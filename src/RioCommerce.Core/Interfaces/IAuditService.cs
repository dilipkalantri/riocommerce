using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Central audit-log writer. Every important backend action funnels through here so the audit
/// timeline on <c>/admin/audit</c> stays the single immutable source of truth.
/// </summary>
public interface IAuditService
{
    /// <summary>Legacy short overload — kept for compile compatibility with existing call sites.
    /// New code should prefer <see cref="WriteAsync"/>.</summary>
    Task LogAsync(Guid? actorId, string actorName, string action, string entityType, string? entityId, string? details);

    /// <summary>Rich audit write. Auto-captures HTTP fingerprint (IP / browser / device) from the
    /// ambient HttpContext when present. Any field on the entry can be left null.</summary>
    Task WriteAsync(AuditEntry entry);

    Task<List<AuditLogItem>> ListAsync(int take = 100);
    Task<List<AuditLogItem>> ListForEntityAsync(string entityType, string entityId, int take = 100);
}

/// <summary>Payload for <see cref="IAuditService.WriteAsync"/>.</summary>
public class AuditEntry
{
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    /// <summary>Products / Customers / Orders / Payments / Faculty / Promotions / AdminAccess / Auth / Settings / Other.</summary>
    public string Module { get; set; } = "Other";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Status { get; set; } = "Success";
    public string? Details { get; set; }
}
