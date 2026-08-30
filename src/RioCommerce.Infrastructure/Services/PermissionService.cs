using System.Text.Json;
using RioCommerce.Core.DTOs.Access;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Security;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IPermissionService"/>. See the interface doc-comment for
/// the resolution rules. Audit-logs every mutation through the existing <see cref="IAuditService"/>
/// so a Super Admin can replay who-changed-what-when from /admin/audit.
/// <para><b>Performance characteristics</b>
///   <list type="bullet">
///     <item>Role permission lookups are memoised for 15 minutes via <see cref="IMemoryCache"/>;
///           every mutation invalidates the affected role's entry, so reads see updates
///           immediately while skipping the round-trip on the hot path.</item>
///     <item>Delta saves (<see cref="ApplyUserOverridesDeltaAsync"/>) touch only the rows the
///           admin toggled — typically a single-digit count — instead of the
///           DELETE-then-INSERT-everything pattern of the legacy <see cref="SaveUserOverridesAsync"/>.</item>
///     <item>Composite unique indexes <c>(RoleId, PermissionKey)</c> and <c>(UserId, PermissionKey)</c>
///           live on the table since the original RBAC migration, so every per-key lookup is
///           an index seek not a sequential scan.</item>
///   </list></para>
/// </summary>
public class PermissionService : IPermissionService
{
    public const string SuperAdminRoleName = "super_admin";
    /// <summary>Sentinel role auto-attached to any user with at least one custom override. Lets the
    /// existing role-gated header (the orange "Admin" button) + AdminLayout entry gate let them in,
    /// without requiring the admin to also assign them a full staff role like <c>operations</c>.</summary>
    public const string BackendUserRoleName = "backend_user";
    private static readonly TimeSpan RoleCacheTtl = TimeSpan.FromMinutes(15);

    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    private readonly IMemoryCache _cache;
    public PermissionService(RioCommerceDbContext db, IAuditService audit, IMemoryCache cache)
    { _db = db; _audit = audit; _cache = cache; }

    private static string RoleCacheKey(Guid roleId) => $"rbac:role:{roleId:N}";
    private void InvalidateRoleCache(Guid roleId) => _cache.Remove(RoleCacheKey(roleId));

    /// <summary>Keep the user's <see cref="BackendUserRoleName"/> assignment in sync with whether
    /// they currently have any override. Idempotent — safe to call after every mutation.</summary>
    private async Task SyncBackendUserRoleAsync(Guid userId)
    {
        var hasOverrides = await _db.UserPermissionOverrides.AsNoTracking().AnyAsync(x => x.UserId == userId);
        var sentinelRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == BackendUserRoleName);
        if (sentinelRole == null) return;   // role hasn't been seeded yet; boot-time seeder handles it
        var assignment = await _db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == sentinelRole.Id);

        if (hasOverrides)
        {
            if (assignment == null) _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = sentinelRole.Id, IsActive = true });
            else if (!assignment.IsActive) assignment.IsActive = true;
        }
        else if (assignment != null)
        {
            // Last override gone — strip the sentinel role so they revert to a regular customer.
            _db.UserRoles.Remove(assignment);
        }
        await _db.SaveChangesAsync();
    }

    private async Task<HashSet<string>> GetRolePermissionKeysCachedAsync(Guid roleId)
    {
        if (_cache.TryGetValue(RoleCacheKey(roleId), out HashSet<string>? cached) && cached != null)
            return cached;
        var keys = await _db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionKey)
            .ToListAsync();
        var set = new HashSet<string>(keys, StringComparer.Ordinal);
        _cache.Set(RoleCacheKey(roleId), set, RoleCacheTtl);
        return set;
    }

    public Task<bool> IsSuperAdminAsync(Guid userId) =>
        _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.IsActive && ur.Role.Name == SuperAdminRoleName);

    private static readonly string[] LegacyStaffRoles =
        { SuperAdminRoleName, "admin", "operations", "faculty", "franchise_admin" };

    public Task<bool> IsLegacyStaffAsync(Guid userId) =>
        _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.IsActive && LegacyStaffRoles.Contains(ur.Role.Name));

    public async Task<bool> CanAccessAsync(Guid userId, string permissionKey)
    {
        if (await IsLegacyStaffAsync(userId)) return true;
        return await HasPermissionAsync(userId, permissionKey);
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permissionKey)
    {
        if (string.IsNullOrWhiteSpace(permissionKey)) return false;
        if (await IsSuperAdminAsync(userId)) return true;

        // User overrides win over role grants.
        var ov = await _db.UserPermissionOverrides.AsNoTracking()
            .Where(x => x.UserId == userId && x.PermissionKey == permissionKey)
            .Select(x => (bool?)x.Granted)
            .FirstOrDefaultAsync();
        if (ov.HasValue) return ov.Value;

        // Role-permission lookup hits the 15-minute cache; a cold lookup is a single index seek.
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.IsActive)
            .Select(ur => ur.RoleId).ToListAsync();
        foreach (var roleId in roleIds)
            if ((await GetRolePermissionKeysCachedAsync(roleId)).Contains(permissionKey)) return true;
        return false;
    }

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId)
    {
        if (await IsSuperAdminAsync(userId)) return PermissionCatalog.AllKeys;

        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.IsActive)
            .Select(ur => ur.RoleId).ToListAsync();

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleId in roleIds)
            foreach (var key in await GetRolePermissionKeysCachedAsync(roleId)) set.Add(key);

        var ovs = await _db.UserPermissionOverrides.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.PermissionKey, x.Granted }).ToListAsync();
        foreach (var ov in ovs)
        {
            if (ov.Granted) set.Add(ov.PermissionKey);
            else set.Remove(ov.PermissionKey);
        }
        return set;
    }

    public async Task<List<AccessRoleRow>> ListRolesAsync()
    {
        var rows = await _db.Roles
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.DisplayName)
            .Select(r => new
            {
                r.Id, r.Name, r.DisplayName, r.IsSystem, r.IsActive,
                Users = _db.UserRoles.Count(ur => ur.RoleId == r.Id && ur.IsActive)
            }).ToListAsync();
        return rows.Select(r => new AccessRoleRow(r.Id, r.Name, r.DisplayName, r.IsSystem, r.IsActive,
            r.Name == SuperAdminRoleName, r.Users)).ToList();
    }

    public async Task<RolePermissionState?> GetRoleStateAsync(Guid roleId)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (role == null) return null;
        // Super Admin renders as all-granted irrespective of any rows. We don't persist them.
        var isSuper = role.Name == SuperAdminRoleName;
        var granted = isSuper
            ? PermissionCatalog.AllKeys.ToHashSet()
            : (await _db.RolePermissions.Where(rp => rp.RoleId == roleId).Select(rp => rp.PermissionKey).ToListAsync()).ToHashSet();

        // Project ALL catalogue keys so the UI never has to merge.
        var map = PermissionCatalog.AllKeys.ToDictionary(k => k, k => isSuper || granted.Contains(k));
        return new RolePermissionState(role.Id, role.Name, role.DisplayName, isSuper, map);
    }

    public async Task<(bool ok, string? error)> SaveRolePermissionsAsync(
        SaveRolePermissionsRequest req, Guid actorId, string actorName, string? ipAddress)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.RoleId);
        if (role == null) return (false, "Role not found.");
        // ── Super Admin protection ── ALWAYS full access. Cannot be edited. ──
        if (role.Name == SuperAdminRoleName) return (false, "Super Admin permissions cannot be modified.");

        var requested = (req.Permissions ?? new())
            .Where(kv => kv.Value && PermissionCatalog.AllKeys.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet();
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync();
        var existingKeys = existing.Select(x => x.PermissionKey).ToHashSet();

        var toAdd    = requested.Except(existingKeys).ToList();
        var toRemove = existing.Where(x => !requested.Contains(x.PermissionKey)).ToList();

        foreach (var key in toAdd)
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
        if (toRemove.Count > 0) _db.RolePermissions.RemoveRange(toRemove);

        await _db.SaveChangesAsync();
        InvalidateRoleCache(role.Id);
        await _audit.LogAsync(actorId, actorName, "PermissionsRoleSaved", "Role", role.Id.ToString(),
            JsonSerializer.Serialize(new { role = role.Name, added = toAdd, removed = toRemove.Select(r => r.PermissionKey), ip = ipAddress }));
        return (true, null);
    }

    public async Task<(bool ok, string? error, int affected)> ApplyUserOverridesDeltaAsync(
        SaveUserOverridesDeltaRequest req, Guid actorId, string actorName, string? ipAddress)
    {
        if (req == null) return (false, "Empty request.", 0);
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UserId);
        if (user == null) return (false, "User not found.", 0);
        if (user.Id == actorId) return (false, "You cannot edit your own permission overrides.", 0);
        if (await IsSuperAdminAsync(user.Id)) return (false, "Super Admin permissions cannot be overridden.", 0);

        // Validate + dedupe — last write wins for the same key in the payload.
        var clean = (req.ChangedPermissions ?? new())
            .Where(c => !string.IsNullOrWhiteSpace(c.Permission) && PermissionCatalog.AllKeys.Contains(c.Permission))
            .GroupBy(c => c.Permission)
            .ToDictionary(g => g.Key, g => g.Last().Value);
        if (clean.Count == 0) return (true, null, 0);

        // Single round-trip to read just the existing override rows for these keys.
        var keysList = clean.Keys.ToList();
        var existing = await _db.UserPermissionOverrides
            .Where(x => x.UserId == user.Id && keysList.Contains(x.PermissionKey))
            .ToListAsync();
        var existingByKey = existing.ToDictionary(x => x.PermissionKey, x => x);

        var inserted = 0; var updated = 0;
        var now = DateTime.UtcNow;
        foreach (var (key, value) in clean)
        {
            if (existingByKey.TryGetValue(key, out var row))
            {
                if (row.Granted != value)
                {
                    // Audit row: previous Granted → new value. Action = Updated when value→true; Removed when value→false.
                    _db.PermissionAuditLogs.Add(new PermissionAuditLog
                    {
                        UserId = user.Id, PermissionKey = key,
                        OldValue = row.Granted, NewValue = value,
                        ChangedById = actorId, ChangedByName = actorName,
                        Action = value ? "Updated" : "Removed",
                        IpAddress = ipAddress, CreatedAt = now, UpdatedAt = now
                    });
                    row.Granted = value; updated++;
                }
            }
            else
            {
                _db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, PermissionKey = key, Granted = value });
                _db.PermissionAuditLogs.Add(new PermissionAuditLog
                {
                    UserId = user.Id, PermissionKey = key,
                    OldValue = null, NewValue = value,        // null = was inheriting / no row before
                    ChangedById = actorId, ChangedByName = actorName,
                    Action = value ? "Updated" : "Removed",
                    IpAddress = ipAddress, CreatedAt = now, UpdatedAt = now
                });
                inserted++;
            }
        }

        var affected = inserted + updated;
        if (affected > 0) await _db.SaveChangesAsync();
        await SyncBackendUserRoleAsync(user.Id);

        // Bulk-level audit row in the general AuditLogs table for the broader Audit page.
        await _audit.LogAsync(actorId, actorName, "PermissionsUserOverridesSaved", "User", user.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                user = user.FullName,
                changedCount = clean.Count, inserted, updated,
                sample = clean.Take(20).Select(kv => new { key = kv.Key, value = kv.Value }),
                ip = ipAddress
            }));
        return (true, null, affected);
    }

    // ── 📋 Permission Change History queries ──
    public async Task<(List<PermissionAuditRow> Rows, int Total)> GetUserPermissionAuditAsync(PermissionHistoryFilter f)
    {
        var q = _db.PermissionAuditLogs.AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.UserId == f.UserId);

        if (!string.IsNullOrWhiteSpace(f.PermissionKey)) q = q.Where(x => x.PermissionKey == f.PermissionKey);
        if (!string.IsNullOrWhiteSpace(f.ChangedByName))
        {
            var s = f.ChangedByName.Trim();
            q = q.Where(x => EF.Functions.ILike(x.ChangedByName, $"%{s}%"));
        }
        if (!string.IsNullOrWhiteSpace(f.Action)) q = q.Where(x => x.Action == f.Action);
        if (f.FromUtc.HasValue) q = q.Where(x => x.CreatedAt >= f.FromUtc);
        if (f.ToUtc.HasValue) q = q.Where(x => x.CreatedAt <= f.ToUtc);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(x =>
                EF.Functions.ILike(x.PermissionKey, $"%{s}%") ||
                EF.Functions.ILike(x.ChangedByName, $"%{s}%"));
        }

        var total = await q.CountAsync();
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 200);
        var rows = await q.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new PermissionAuditRow(
                x.Id, x.CreatedAt, x.ChangedByName,
                x.UserId, x.User != null ? x.User.FullName : "",
                x.PermissionKey, x.PermissionKey,
                x.OldValue, x.NewValue, x.Action))
            .ToListAsync();
        // Decorate with friendly labels from the catalogue (compile-time, no extra round-trip).
        var byKey = PermissionCatalog.AllRows.ToDictionary(r => r.Key);
        rows = rows.Select(r =>
        {
            var dot = r.PermissionKey.LastIndexOf('.');
            var rowKey = dot > 0 ? r.PermissionKey[..dot] : r.PermissionKey;
            var action = dot > 0 ? r.PermissionKey[(dot + 1)..] : "";
            var label = byKey.TryGetValue(rowKey, out var pr)
                ? $"{pr.Label}.{Capitalize(action)}"
                : r.PermissionKey;
            return r with { PermissionLabel = label };
        }).ToList();
        return (rows, total);
    }

    public async Task<LastPermissionChange?> GetUserLastChangeAsync(Guid userId)
    {
        return await _db.PermissionAuditLogs.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LastPermissionChange(x.CreatedAt, x.ChangedByName, x.Action))
            .FirstOrDefaultAsync();
    }

    public async Task<(List<PermissionAuditRow> Rows, int Total)> GetGlobalPermissionAuditAsync(PermissionHistoryFilter f)
    {
        // Restrict to users who actually have at least one override — keeps the panel focused on
        // customer-managed access only, per spec ("Only users having custom access should appear").
        var customisedUserIds = _db.UserPermissionOverrides.Select(x => x.UserId);

        var q = _db.PermissionAuditLogs.AsNoTracking()
            .Include(x => x.User)
            .Where(x => customisedUserIds.Contains(x.UserId));

        if (!string.IsNullOrWhiteSpace(f.PermissionKey)) q = q.Where(x => x.PermissionKey == f.PermissionKey);
        if (!string.IsNullOrWhiteSpace(f.ChangedByName))
        {
            var s = f.ChangedByName.Trim();
            q = q.Where(x => EF.Functions.ILike(x.ChangedByName, $"%{s}%"));
        }
        if (!string.IsNullOrWhiteSpace(f.Action)) q = q.Where(x => x.Action == f.Action);
        if (f.FromUtc.HasValue) q = q.Where(x => x.CreatedAt >= f.FromUtc);
        if (f.ToUtc.HasValue) q = q.Where(x => x.CreatedAt <= f.ToUtc);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(x =>
                EF.Functions.ILike(x.PermissionKey, $"%{s}%") ||
                EF.Functions.ILike(x.ChangedByName, $"%{s}%") ||
                (x.User != null && EF.Functions.ILike(x.User.FullName, $"%{s}%")));
        }

        var total = await q.CountAsync();
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 200);
        var rows = await q.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(x => new PermissionAuditRow(
                x.Id, x.CreatedAt, x.ChangedByName,
                x.UserId, x.User != null ? x.User.FullName : "",
                x.PermissionKey, x.PermissionKey,
                x.OldValue, x.NewValue, x.Action))
            .ToListAsync();
        var byKey = PermissionCatalog.AllRows.ToDictionary(r => r.Key);
        rows = rows.Select(r =>
        {
            var dot = r.PermissionKey.LastIndexOf('.');
            var rowKey = dot > 0 ? r.PermissionKey[..dot] : r.PermissionKey;
            var label = byKey.TryGetValue(rowKey, out var pr) ? pr.Label : r.PermissionKey;
            return r with { PermissionLabel = label };
        }).ToList();
        return (rows, total);
    }

    public async Task<List<BackendAccessUser>> ListBackendAccessUsersAsync(string? search, int take = 100)
    {
        // Restrict to users with at least one override row — the table's defining condition.
        var userIdsWithOverrides = _db.UserPermissionOverrides
            .Select(x => x.UserId).Distinct();

        var q = _db.Users.AsNoTracking()
            .Where(u => userIdsWithOverrides.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u =>
                EF.Functions.ILike(u.FullName, $"%{s}%") ||
                (u.Email != null && EF.Functions.ILike(u.Email, $"%{s}%")) ||
                (u.Phone != null && EF.Functions.ILike(u.Phone, $"%{s}%")));
        }

        // Project everything in ONE round-trip — override keys + last-updated timestamp.
        var raw = await q.OrderBy(u => u.FullName)
            .Take(Math.Clamp(take, 1, 500))
            .Select(u => new
            {
                u.Id, u.FullName, u.Email, u.Phone, u.AvatarUrl, u.IsActive,
                GrantedKeys = _db.UserPermissionOverrides
                    .Where(x => x.UserId == u.Id && x.Granted)
                    .Select(x => x.PermissionKey).ToList(),
                LastUpdated = _db.PermissionAuditLogs
                    .Where(a => a.UserId == u.Id)
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => (DateTime?)a.CreatedAt).FirstOrDefault()
            })
            .ToListAsync();

        return raw.Select(r =>
        {
            // "Modules Granted" = distinct module-row keys (strip the trailing .{action} segment).
            var modules = r.GrantedKeys
                .Select(k => { var dot = k.LastIndexOf('.'); return dot > 0 ? k[..dot] : k; })
                .Distinct(StringComparer.Ordinal)
                .Count();
            return new BackendAccessUser(
                r.Id, r.FullName, InitialsOfName(r.FullName, r.Email),
                r.AvatarUrl, r.Email, r.Phone,
                FormatCustomerCode(r.Id),
                modules, r.LastUpdated, r.IsActive);
        }).ToList();
    }

    // Two-letter initials chip for the avatar fallback.
    private static string InitialsOfName(string? fullName, string? email)
    {
        var name = string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(email)
            ? email.Split('@')[0]
            : (fullName ?? "");
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        return name.Trim().Length >= 2 ? name.Trim()[..2].ToUpperInvariant() : name.Trim().ToUpperInvariant();
    }

    private static string FormatCustomerCode(Guid id) => "RIO" + id.ToString("N").Substring(0, 6).ToUpperInvariant();

    public async Task<(bool ok, string? error)> RemoveUserOverrideForRowAsync(
        Guid userId, string rowKey, Guid actorId, string actorName, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(rowKey)) return (false, "Permission row required.");
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        if (userId == actorId) return (false, "You cannot reset your own permissions.");

        // Delete every override that targets this row's action keys (view/create/edit/delete/approve/export).
        var prefix = rowKey + ".";
        var overrides = await _db.UserPermissionOverrides
            .Where(x => x.UserId == userId && (x.PermissionKey == rowKey || x.PermissionKey.StartsWith(prefix)))
            .ToListAsync();
        if (overrides.Count == 0) return (true, null);    // already inheriting

        _db.UserPermissionOverrides.RemoveRange(overrides);
        var now = DateTime.UtcNow;
        foreach (var ov in overrides)
        {
            _db.PermissionAuditLogs.Add(new PermissionAuditLog
            {
                UserId = userId, PermissionKey = ov.PermissionKey,
                OldValue = ov.Granted, NewValue = null,
                ChangedById = actorId, ChangedByName = actorName,
                Action = "Reset", IpAddress = ipAddress,
                CreatedAt = now, UpdatedAt = now
            });
        }
        await _db.SaveChangesAsync();
        await SyncBackendUserRoleAsync(userId);
        await _audit.LogAsync(actorId, actorName, "PermissionsRowReset", "User", userId.ToString(),
            JsonSerializer.Serialize(new { user = user.FullName, rowKey, removed = overrides.Count, ip = ipAddress }));
        return (true, null);
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    public async Task<UserPermissionOverridesState?> GetUserOverridesAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;
        var rows = await _db.UserPermissionOverrides
            .Where(x => x.UserId == userId)
            .Select(x => new { x.PermissionKey, x.Granted }).ToListAsync();
        var map = rows.ToDictionary(r => r.PermissionKey, r => r.Granted);
        return new UserPermissionOverridesState(user.Id, user.FullName, map);
    }

    public async Task<(bool ok, string? error)> SaveUserOverridesAsync(
        SaveUserOverridesRequest req, Guid actorId, string actorName, string? ipAddress)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId);
        if (user == null) return (false, "User not found.");
        // ── Self-protection: staff cannot edit their own overrides. ──
        if (user.Id == actorId) return (false, "You cannot edit your own permission overrides.");
        // Super Admin is immutable.
        var isUserSuper = await IsSuperAdminAsync(user.Id);
        if (isUserSuper) return (false, "Super Admin permissions cannot be overridden.");

        var clean = (req.Overrides ?? new())
            .Where(kv => PermissionCatalog.AllKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var existing = await _db.UserPermissionOverrides.Where(x => x.UserId == user.Id).ToListAsync();
        _db.UserPermissionOverrides.RemoveRange(existing);
        foreach (var (key, granted) in clean)
            _db.UserPermissionOverrides.Add(new UserPermissionOverride { UserId = user.Id, PermissionKey = key, Granted = granted });

        await _db.SaveChangesAsync();
        await SyncBackendUserRoleAsync(user.Id);
        await _audit.LogAsync(actorId, actorName, "PermissionsUserOverridesSaved", "User", user.Id.ToString(),
            JsonSerializer.Serialize(new { user = user.FullName, overrides = clean, ip = ipAddress }));
        return (true, null);
    }

    public async Task<(bool ok, string? error)> RemoveUserOverridesAsync(
        Guid userId, Guid actorId, string actorName, string? ipAddress)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        if (userId == actorId) return (false, "You cannot reset your own permissions.");

        var existing = await _db.UserPermissionOverrides.Where(x => x.UserId == userId).ToListAsync();
        if (existing.Count == 0) return (true, null);   // already inheriting; idempotent
        _db.UserPermissionOverrides.RemoveRange(existing);
        // One Reset row per cleared key so the timeline carries the full revert.
        var now = DateTime.UtcNow;
        foreach (var ov in existing)
        {
            _db.PermissionAuditLogs.Add(new PermissionAuditLog
            {
                UserId = userId, PermissionKey = ov.PermissionKey,
                OldValue = ov.Granted, NewValue = null,                 // null = back to inherit
                ChangedById = actorId, ChangedByName = actorName,
                Action = "Reset",
                IpAddress = ipAddress, CreatedAt = now, UpdatedAt = now
            });
        }
        await _db.SaveChangesAsync();
        await SyncBackendUserRoleAsync(userId);
        await _audit.LogAsync(actorId, actorName, "PermissionsUserOverridesCleared", "User", userId.ToString(),
            JsonSerializer.Serialize(new { user = user.FullName, removed = existing.Count, ip = ipAddress }));
        return (true, null);
    }

    public async Task<List<RioCommerce.Core.DTOs.Admin.AuditLogItem>> GetUserPermissionHistoryAsync(Guid userId, int take = 25)
    {
        var idStr = userId.ToString();
        return await _db.AuditLogs
            .Where(a => a.EntityType == "User" && a.EntityId == idStr
                     && (a.Action == "PermissionsUserOverridesSaved" || a.Action == "PermissionsUserOverridesCleared"))
            .OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 100))
            .Select(a => new RioCommerce.Core.DTOs.Admin.AuditLogItem(
                a.Id, a.ActorName, a.Action, a.EntityType, a.EntityId, a.Details, a.CreatedAt))
            .ToListAsync();
    }

    public async Task<(bool ok, string? error, Guid newRoleId)> CreateRoleAsync(
        CreateRoleRequest req, Guid actorId, string actorName, string? ipAddress)
    {
        var displayName = (req.DisplayName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(displayName)) return (false, "Provide a role name.", Guid.Empty);
        var sysName = SystemNameOf(displayName);
        if (sysName == SuperAdminRoleName) return (false, "Cannot create a role named Super Admin.", Guid.Empty);
        if (await _db.Roles.AnyAsync(r => r.Name == sysName)) return (false, "A role with that name already exists.", Guid.Empty);

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = sysName,
            DisplayName = displayName,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            IsSystem = false,
            IsActive = true
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "PermissionsRoleCreated", "Role", role.Id.ToString(),
            JsonSerializer.Serialize(new { name = sysName, displayName, ip = ipAddress }));
        return (true, null, role.Id);
    }

    public async Task<(bool ok, string? error)> RenameRoleAsync(
        RenameRoleRequest req, Guid actorId, string actorName, string? ipAddress)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.RoleId);
        if (role == null) return (false, "Role not found.");
        if (role.IsSystem) return (false, "System roles cannot be renamed.");
        var newName = (req.NewDisplayName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(newName)) return (false, "Provide a name.");
        var oldName = role.DisplayName;
        role.DisplayName = newName;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "PermissionsRoleRenamed", "Role", role.Id.ToString(),
            JsonSerializer.Serialize(new { from = oldName, to = newName, ip = ipAddress }));
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteRoleAsync(
        Guid roleId, Guid actorId, string actorName, string? ipAddress)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (role == null) return (false, "Role not found.");
        if (role.IsSystem) return (false, "System roles cannot be deleted.");
        if (role.Name == SuperAdminRoleName) return (false, "Super Admin cannot be deleted.");

        // Cascade — remove role permissions + user-role assignments first.
        _db.RolePermissions.RemoveRange(_db.RolePermissions.Where(rp => rp.RoleId == role.Id));
        _db.UserRoles.RemoveRange(_db.UserRoles.Where(ur => ur.RoleId == role.Id));
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "PermissionsRoleDeleted", "Role", roleId.ToString(),
            JsonSerializer.Serialize(new { name = role.Name, displayName = role.DisplayName, ip = ipAddress }));
        return (true, null);
    }

    public async Task<(bool ok, string? error, Guid newRoleId)> CloneRoleAsync(
        CloneRoleRequest req, Guid actorId, string actorName, string? ipAddress)
    {
        var src = await _db.Roles.FirstOrDefaultAsync(r => r.Id == req.SourceRoleId);
        if (src == null) return (false, "Source role not found.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(req.NewDisplayName)) return (false, "Provide a name for the cloned role.", Guid.Empty);

        var displayName = req.NewDisplayName.Trim();
        var systemName = string.IsNullOrWhiteSpace(req.NewSystemName)
            ? SystemNameOf(displayName)
            : SystemNameOf(req.NewSystemName);
        if (systemName == SuperAdminRoleName) return (false, "Cannot clone into the Super Admin role.", Guid.Empty);
        if (await _db.Roles.AnyAsync(r => r.Name == systemName)) return (false, "A role with that system name already exists.", Guid.Empty);

        var newRole = new Role
        {
            Id = Guid.NewGuid(),
            Name = systemName,
            DisplayName = displayName,
            Description = string.IsNullOrWhiteSpace(req.Description) ? src.Description : req.Description.Trim(),
            IsSystem = false,
            IsActive = true
        };
        _db.Roles.Add(newRole);

        // Clone permissions — Super Admin source clones to "all catalogue keys" since it's not persisted.
        var sourceKeys = src.Name == SuperAdminRoleName
            ? PermissionCatalog.AllKeys.ToList()
            : await _db.RolePermissions.Where(rp => rp.RoleId == src.Id).Select(rp => rp.PermissionKey).ToListAsync();
        foreach (var key in sourceKeys)
            _db.RolePermissions.Add(new RolePermission { RoleId = newRole.Id, PermissionKey = key });

        await _db.SaveChangesAsync();
        InvalidateRoleCache(newRole.Id);
        await _audit.LogAsync(actorId, actorName, "PermissionsRoleCloned", "Role", newRole.Id.ToString(),
            JsonSerializer.Serialize(new { source = src.Name, target = systemName, permissionsCopied = sourceKeys.Count, ip = ipAddress }));
        return (true, null, newRole.Id);
    }

    public async Task<PermissionExportBundle> ExportAllAsync()
    {
        var roles = await _db.Roles.OrderBy(r => r.Name).ToListAsync();
        var grants = await _db.RolePermissions.Select(rp => new { rp.RoleId, rp.PermissionKey }).ToListAsync();
        var byRole = grants.GroupBy(g => g.RoleId).ToDictionary(g => g.Key, g => g.Select(x => x.PermissionKey).ToList());
        var exported = roles.Select(r => new ExportedRole(
            r.Name, r.DisplayName, r.Description, r.IsActive,
            r.Name == SuperAdminRoleName
                ? PermissionCatalog.AllKeys.ToList()
                : (byRole.TryGetValue(r.Id, out var keys) ? keys : new())
        )).ToList();
        return new PermissionExportBundle(DateTime.UtcNow, "1.0", exported);
    }

    public async Task<(bool ok, string? error, int rolesAffected)> ImportAsync(
        PermissionExportBundle bundle, Guid actorId, string actorName, string? ipAddress)
    {
        if (bundle?.Roles == null || bundle.Roles.Count == 0) return (false, "Empty bundle.", 0);
        var affected = 0;
        foreach (var er in bundle.Roles)
        {
            if (er.SystemName == SuperAdminRoleName) continue;        // never mutate Super Admin from import
            var sysName = SystemNameOf(er.SystemName);
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == sysName);
            if (role == null)
            {
                role = new Role
                {
                    Id = Guid.NewGuid(), Name = sysName, DisplayName = er.DisplayName,
                    Description = er.Description, IsSystem = false, IsActive = er.IsActive
                };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync();   // flush so the FK below is valid
            }

            var existing = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).ToListAsync();
            _db.RolePermissions.RemoveRange(existing);
            foreach (var k in er.Permissions.Where(PermissionCatalog.AllKeys.Contains))
                _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = k });
            InvalidateRoleCache(role.Id);
            affected++;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "PermissionsBundleImported", "Role", null,
            JsonSerializer.Serialize(new { rolesAffected = affected, version = bundle.Version, ip = ipAddress }));
        return (true, null, affected);
    }

    // Normalize "Custom Staff" → "custom_staff" so system names stay url-/JSON-friendly.
    private static string SystemNameOf(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var chars = s.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var n = new string(chars);
        while (n.Contains("__")) n = n.Replace("__", "_");
        return n.Trim('_');
    }
}
