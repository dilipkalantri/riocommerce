namespace RioCommerce.Core.Entities;

/// <summary>
/// Single, immutable audit row. Captures who did what to which entity, with full before/after
/// values plus request fingerprint (IP, browser, device) so a Super Admin can replay the
/// activity timeline from <c>/admin/audit</c>.
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    /// <summary>High-level grouping shown on the audit page filter (Products / Customers / Orders / …).</summary>
    public string? Module { get; set; }
    /// <summary>e.g. <c>ProductPriceChanged</c>, <c>UserLoginSuccess</c>, <c>OrderStatusUpdated</c>.</summary>
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    /// <summary>Human-readable entity label (product title, order number, customer name).</summary>
    public string? EntityName { get; set; }
    /// <summary>Previous value when the action changed something — null for create/delete/login.</summary>
    public string? OldValue { get; set; }
    /// <summary>New value when the action changed something — null for delete/logout.</summary>
    public string? NewValue { get; set; }
    /// <summary>Success | Failed | Warning — drives the colour pill on the audit table.</summary>
    public string Status { get; set; } = "Success";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>Browser family parsed from the UA (Chrome / Edge / Firefox / Safari / Other).</summary>
    public string? Browser { get; set; }
    /// <summary>OS family parsed from the UA (Windows / macOS / Linux / Android / iOS / Other).</summary>
    public string? Device { get; set; }
    /// <summary>Free-form JSON payload — survives schema drift, used by the View Details modal.</summary>
    public string? Details { get; set; }
}
