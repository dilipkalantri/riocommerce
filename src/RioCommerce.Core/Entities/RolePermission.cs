namespace RioCommerce.Core.Entities;

/// <summary>
/// One granted permission for a role. A row's presence === permission granted.
/// Absence === not granted. We store positive grants only so revoking a permission
/// is a row delete — keeps the table small and audits clean.
/// </summary>
public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public Role? Role { get; set; }
}
