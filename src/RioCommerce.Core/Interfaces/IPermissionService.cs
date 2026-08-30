using RioCommerce.Core.DTOs.Access;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Central authority for RBAC. Every page, button, menu item and API endpoint that needs to
/// gate access does it through this single interface — keeping the logic auditable and uniform.
/// <para><b>Super Admin rule.</b> Users carrying the <c>super_admin</c> role always evaluate to
/// <see langword="true"/>. They cannot be locked out, cannot remove their own super-admin role,
/// and cannot have their permissions edited from the Access Control page.</para>
/// <para><b>Effective permission resolution.</b>
///   <list type="number">
///     <item>If the user has the <c>super_admin</c> role → granted.</item>
///     <item>If a per-user override exists → its <c>Granted</c> bit wins.</item>
///     <item>Otherwise, the union of every active role's grants decides.</item>
///   </list></para>
/// </summary>
public interface IPermissionService
{
    /// <summary>Is the given user a Super Admin? Cached per-request via the DbContext.</summary>
    Task<bool> IsSuperAdminAsync(Guid userId);

    /// <summary>True if the user carries any "legacy" full-access staff role
    /// (<c>super_admin</c>, <c>admin</c>, <c>operations</c>, <c>faculty</c>, <c>franchise_admin</c>).
    /// These roles **bypass** per-permission gates — they continue to see the full admin area.
    /// Only customers whose access was granted via <c>backend_user</c>-only get the granular check.</summary>
    Task<bool> IsLegacyStaffAsync(Guid userId);

    /// <summary>Final access decision used by the menu + page gates. Returns true if the user is a
    /// legacy staff role OR holds the specific <paramref name="permissionKey"/>. Super-admin is
    /// considered legacy staff and always returns true.</summary>
    Task<bool> CanAccessAsync(Guid userId, string permissionKey);

    /// <summary>Resolve a single key for a user — the canonical permission check.</summary>
    Task<bool> HasPermissionAsync(Guid userId, string permissionKey);

    /// <summary>Bulk resolve every key for a user — used by the menu/page to render at once.</summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId);

    /// <summary>List every role (system + custom) with metadata for the role-picker column.</summary>
    Task<List<AccessRoleRow>> ListRolesAsync();

    /// <summary>Load the role's permission map for the editor. Includes ALL catalogue keys with their
    /// granted boolean so the UI doesn't have to merge against the catalogue manually.</summary>
    Task<RolePermissionState?> GetRoleStateAsync(Guid roleId);

    /// <summary>Save a role's permission set. Super Admin role is immutable — always rejected.</summary>
    Task<(bool ok, string? error)> SaveRolePermissionsAsync(SaveRolePermissionsRequest req, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Load the per-user override map for the editor.</summary>
    Task<UserPermissionOverridesState?> GetUserOverridesAsync(Guid userId);

    /// <summary>Save per-user overrides. Cannot be used to escalate the actor's own permissions.</summary>
    Task<(bool ok, string? error)> SaveUserOverridesAsync(SaveUserOverridesRequest req, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Fast delta save — touches only the rows the admin actually toggled. Returns the
    /// number of rows inserted/updated; never deletes (use <see cref="RemoveUserOverridesAsync"/>
    /// to clear the whole override).</summary>
    Task<(bool ok, string? error, int affected)> ApplyUserOverridesDeltaAsync(SaveUserOverridesDeltaRequest req, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Remove every override row for a user — flips them back to "Inherit from Role".</summary>
    Task<(bool ok, string? error)> RemoveUserOverridesAsync(Guid userId, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Return the audit-log trail of override + role changes for a given user.</summary>
    Task<List<RioCommerce.Core.DTOs.Admin.AuditLogItem>> GetUserPermissionHistoryAsync(Guid userId, int take = 25);

    /// <summary>Filterable, paginated permission-change timeline used by the history table.</summary>
    Task<(List<PermissionAuditRow> Rows, int Total)> GetUserPermissionAuditAsync(PermissionHistoryFilter filter);

    /// <summary>Most-recent change for the user — drives "Last Updated / Updated By" chips on the profile card.</summary>
    Task<LastPermissionChange?> GetUserLastChangeAsync(Guid userId);

    /// <summary>Cross-user audit table — used by the global history panel. Returns rows for ALL users
    /// who currently have any custom permission. Supports the same filters as the per-user query.</summary>
    Task<(List<PermissionAuditRow> Rows, int Total)> GetGlobalPermissionAuditAsync(PermissionHistoryFilter filter);

    /// <summary>Remove every override row belonging to <paramref name="rowKey"/> (one module-row) for the
    /// given user. Cheap idempotent revert for the 🗑 button on the history table.</summary>
    Task<(bool ok, string? error)> RemoveUserOverrideForRowAsync(Guid userId, string rowKey, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Returns every user with at least one override row — drives the "Users With Backend Access"
    /// table. Supports server-side fuzzy search on name/email/phone/customer-code.</summary>
    Task<List<BackendAccessUser>> ListBackendAccessUsersAsync(string? search, int take = 100);

    /// <summary>Clone a role + its permissions into a brand new one. Cannot clone the Super Admin role
    /// onto a non-super_admin name (single-identity rule).</summary>
    Task<(bool ok, string? error, Guid newRoleId)> CloneRoleAsync(CloneRoleRequest req, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Create a brand-new custom role (with zero permissions to start).</summary>
    Task<(bool ok, string? error, Guid newRoleId)> CreateRoleAsync(CreateRoleRequest req, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Rename a custom role's display name. System roles are immutable.</summary>
    Task<(bool ok, string? error)> RenameRoleAsync(RenameRoleRequest req, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Delete a custom role + its permissions + its user assignments. System roles are protected.</summary>
    Task<(bool ok, string? error)> DeleteRoleAsync(Guid roleId, Guid actorId, string actorName, string? ipAddress);

    /// <summary>Snapshot every role + its permissions into a portable bundle (JSON).</summary>
    Task<PermissionExportBundle> ExportAllAsync();

    /// <summary>Import a previously-exported bundle. Existing roles are matched by SystemName and
    /// their permissions REPLACED with the bundle's set; missing roles are CREATED.</summary>
    Task<(bool ok, string? error, int rolesAffected)> ImportAsync(PermissionExportBundle bundle, Guid actorId, string actorName, string? ipAddress);
}
