namespace RioCommerce.Core.DTOs.Admin;

/// <summary>One row in the audit table. When a single audit event covers multiple field changes
/// (e.g. a product save that touched price + faculty + batch status) the row carries the full
/// <see cref="Changes"/> list and the table renders an "N Changes" pill.</summary>
public record AuditRow(
    Guid Id, DateTime At, Guid? ActorUserId, string ActorName,
    string? Module, string Action, string EntityType, string? EntityId, string? EntityName,
    string? ChangedField, string? OldValue, string? NewValue, string Status,
    string? IpAddress, string? Browser, string? Device, string? UserAgent, string? Details,
    List<AuditChange> Changes);

/// <summary>One row in the per-field change matrix inside the audit details modal.</summary>
public record AuditChange(string Field, string? OldValue, string? NewValue);

/// <summary>Filter posted to the audit list endpoint — every WHERE clause is pushed to PostgreSQL.</summary>
public class AuditFilter
{
    /// <summary>Free-text — matches actor name, entity name, entity id, action, IP.</summary>
    public string? Search { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Module { get; set; }
    public string? Action { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>Top-of-page summary tiles.</summary>
public record AuditSummary(
    int TotalActivities,
    int TodayActivities,
    int ActiveStaffToday,
    int FailedLoginsToday,
    int CriticalChangesToday);

public record AuditDistinct(List<string> Modules, List<string> Actions, List<string> Actors);
