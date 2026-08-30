namespace RioCommerce.Core.Entities;

/// <summary>
/// Per-user override that sits above the role's permissions. <see cref="Granted"/> is
/// tri-state via column nullability:
///   <list type="bullet">
///     <item>row present, Granted=true  → explicitly granted (even if role doesn't)</item>
///     <item>row present, Granted=false → explicitly revoked (even if role does)</item>
///     <item>row absent                  → inherit from role</item>
///   </list>
/// </summary>
public class UserPermissionOverride : BaseEntity
{
    public Guid UserId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public bool Granted { get; set; }
    public User? User { get; set; }
}
