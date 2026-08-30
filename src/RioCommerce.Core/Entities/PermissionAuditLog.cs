namespace RioCommerce.Core.Entities;

/// <summary>
/// One row per permission change — the timeline the admin reads on the Permission Change History
/// panel under the matrix. Writes happen synchronously inside the save transaction so the row is
/// always consistent with the override table.
/// <para>Compared to the general <see cref="AuditLog"/> which holds one row per save event with
/// JSON details, this table flattens to one row per <c>(user, permissionKey)</c> change so we can
/// filter by Permission / Date / Changed By / Action via index seeks.</para>
/// </summary>
public class PermissionAuditLog : BaseEntity
{
    public Guid UserId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    /// <summary>Tri-state semantics — null means "no override row existed" (was inheriting).</summary>
    public bool? OldValue { get; set; }
    /// <summary>Null means "row removed" (reverted to inherit).</summary>
    public bool? NewValue { get; set; }
    public Guid? ChangedById { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    /// <summary>"Updated" | "Removed" | "Reset" — Updated == granted; Removed == revoked; Reset == bulk reset to role defaults.</summary>
    public string Action { get; set; } = "Updated";
    public string? IpAddress { get; set; }
    public User? User { get; set; }
}
