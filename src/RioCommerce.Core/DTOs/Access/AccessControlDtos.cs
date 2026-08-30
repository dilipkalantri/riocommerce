using RioCommerce.Core.Security;

namespace RioCommerce.Core.DTOs.Access;

/// <summary>Returned by the catalogue endpoint — feeds the permission tree on the admin page.</summary>
public record PermissionCatalogResponse(IReadOnlyList<PermissionGroup> Groups, IReadOnlyList<string> Actions);

/// <summary>Posted to create a brand-new (custom) role from the left-panel + button.</summary>
public class CreateRoleRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>Posted to rename an existing custom role. System roles cannot be renamed.</summary>
public class RenameRoleRequest
{
    public Guid RoleId { get; set; }
    public string NewDisplayName { get; set; } = string.Empty;
}

/// <summary>Lightweight role row for the role-selector column.</summary>
public record AccessRoleRow(Guid Id, string Name, string DisplayName, bool IsSystem, bool IsActive, bool IsSuperAdmin, int UserCount);

/// <summary>Role permissions snapshot used by both load and save.</summary>
public record RolePermissionState(Guid RoleId, string RoleName, string DisplayName, bool IsSuperAdmin, Dictionary<string, bool> Permissions);

/// <summary>Per-user override snapshot.</summary>
public record UserPermissionOverridesState(Guid UserId, string FullName, Dictionary<string, bool> Overrides);

/// <summary>Payload posted to save a role's permissions in one shot.</summary>
public class SaveRolePermissionsRequest
{
    public Guid RoleId { get; set; }
    /// <summary>Map of <c>permissionKey → granted</c>. Keys not in the map are treated as REVOKED.</summary>
    public Dictionary<string, bool> Permissions { get; set; } = new();
}

/// <summary>Payload posted to save per-user overrides in one shot.</summary>
public class SaveUserOverridesRequest
{
    public Guid UserId { get; set; }
    /// <summary>Map of <c>permissionKey → granted</c>. Override layer sits ABOVE the role's grants.</summary>
    public Dictionary<string, bool> Overrides { get; set; } = new();
}

/// <summary>Delta payload — only the permissions the admin actually toggled. Wire-efficient and
/// avoids the DELETE-then-INSERT pattern on the override table.</summary>
public class SaveUserOverridesDeltaRequest
{
    public Guid UserId { get; set; }
    public List<ChangedPermission> ChangedPermissions { get; set; } = new();
}

public record ChangedPermission(string Permission, bool Value);

// ── 📋 Permission Change History ──
/// <summary>One row in the timeline shown under the user matrix.</summary>
public record PermissionAuditRow(
    Guid Id, DateTime ChangedAt, string ChangedByName, Guid UserId, string UserFullName,
    string PermissionKey, string PermissionLabel,
    bool? OldValue, bool? NewValue, string Action);

/// <summary>Filter posted to the history endpoint — keeps the wire payload small and pushes
/// every WHERE clause down to PostgreSQL.</summary>
public class PermissionHistoryFilter
{
    public Guid UserId { get; set; }
    /// <summary>Free-text — matches permission key + label + actor name.</summary>
    public string? Search { get; set; }
    /// <summary>Optional permission-key filter (exact match).</summary>
    public string? PermissionKey { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? ChangedByName { get; set; }
    /// <summary>Updated | Removed | Reset</summary>
    public string? Action { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

/// <summary>Last-change snapshot — drives the "Last Updated / Updated By" chips on the profile card.</summary>
public record LastPermissionChange(DateTime ChangedAt, string ChangedByName, string Action);

/// <summary>One row in the "Users With Backend Access" table — only users with at least one override.</summary>
public record BackendAccessUser(
    Guid Id, string FullName, string Initials,
    string? AvatarUrl, string? Email, string? Phone,
    string CustomerCode,
    int ModulesGranted,
    DateTime? LastUpdated,
    bool IsActive);

/// <summary>Payload posted to clone an existing role.</summary>
public class CloneRoleRequest
{
    public Guid SourceRoleId { get; set; }
    public string NewDisplayName { get; set; } = string.Empty;
    public string? NewSystemName { get; set; }              // optional; auto-derived from display name when blank
    public string? Description { get; set; }
}

/// <summary>Portable export bundle — usable by the import endpoint to seed a new branch.</summary>
public record PermissionExportBundle(
    DateTime ExportedAt,
    string Version,
    IReadOnlyList<ExportedRole> Roles);

public record ExportedRole(
    string SystemName, string DisplayName, string? Description, bool IsActive,
    IReadOnlyList<string> Permissions);
