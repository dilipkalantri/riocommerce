using System.Globalization;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Security;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Read-only audit surface used by /admin/audit. Writes still happen via <see cref="IAuditService"/>.
///
/// <para><b>Per-field display.</b> The page must answer "which field changed and from what to what" —
/// not "PermissionsUserOverridesSaved". To deliver that without polluting the writer-side, the list
/// query MERGES two sources:</para>
/// <list type="number">
///   <item>The general <c>audit_logs</c> table, with the generic permission-bulk rows
///         (<c>PermissionsUserOverridesSaved</c>, <c>PermissionsUserOverridesCleared</c>,
///         <c>PermissionsRowReset</c>) FILTERED OUT — they would otherwise duplicate the per-field rows below.</item>
///   <item>The <c>permission_audit_logs</c> table — one row per permission change — synthesised
///         into <see cref="AuditRow"/>s with friendly labels like "Products Access · OFF → ON".</item>
/// </list>
/// <para>For non-permission rows the writer side often stores a structured JSON in <c>Details</c>;
/// when we can parse a per-field change set out of it the row's <see cref="AuditRow.Changes"/> list
/// fills in, the table shows "<c>N Changes</c>", and the modal renders the matrix.</para>
/// </summary>
public class AuditAdminService : IAuditAdminService
{
    private static readonly string[] BulkPermissionActions =
        { "PermissionsUserOverridesSaved", "PermissionsUserOverridesCleared", "PermissionsRowReset" };

    private readonly RioCommerceDbContext _db;
    public AuditAdminService(RioCommerceDbContext db) => _db = db;

    private IQueryable<RioCommerce.Core.Entities.AuditLog> BuildRegular(AuditFilter f)
    {
        // EXCLUDE the bulk permission rows — they're surfaced via permission_audit_logs at the
        // granular per-field level so the table doesn't double up.
        var q = _db.AuditLogs.AsNoTracking()
            .Where(a => !BulkPermissionActions.Contains(a.Action));

        if (f.FromUtc.HasValue)   q = q.Where(a => a.CreatedAt >= f.FromUtc);
        if (f.ToUtc.HasValue)     q = q.Where(a => a.CreatedAt <= f.ToUtc);
        if (f.ActorUserId.HasValue) q = q.Where(a => a.ActorUserId == f.ActorUserId);
        if (!string.IsNullOrWhiteSpace(f.ActorName)) { var s = f.ActorName.Trim(); q = q.Where(a => EF.Functions.ILike(a.ActorName, $"%{s}%")); }
        if (!string.IsNullOrWhiteSpace(f.Module))  q = q.Where(a => a.Module == f.Module);
        if (!string.IsNullOrWhiteSpace(f.Action))  q = q.Where(a => a.Action == f.Action);
        if (!string.IsNullOrWhiteSpace(f.Status))  q = q.Where(a => a.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(a =>
                EF.Functions.ILike(a.ActorName, $"%{s}%") ||
                EF.Functions.ILike(a.Action, $"%{s}%") ||
                (a.EntityName != null && EF.Functions.ILike(a.EntityName, $"%{s}%")) ||
                (a.EntityId != null && EF.Functions.ILike(a.EntityId, $"%{s}%")) ||
                (a.IpAddress != null && EF.Functions.ILike(a.IpAddress, $"%{s}%")));
        }
        return q;
    }

    private IQueryable<RioCommerce.Core.Entities.PermissionAuditLog> BuildPermission(AuditFilter f)
    {
        var q = _db.PermissionAuditLogs.AsNoTracking()
            .Include(p => p.User)
            .AsQueryable();
        if (f.FromUtc.HasValue) q = q.Where(p => p.CreatedAt >= f.FromUtc);
        if (f.ToUtc.HasValue)   q = q.Where(p => p.CreatedAt <= f.ToUtc);
        if (f.ActorUserId.HasValue) q = q.Where(p => p.ChangedById == f.ActorUserId);
        if (!string.IsNullOrWhiteSpace(f.ActorName)) { var s = f.ActorName.Trim(); q = q.Where(p => EF.Functions.ILike(p.ChangedByName, $"%{s}%")); }
        // Module filter: "AdminAccess" → permission rows. Anything else → exclude permission rows.
        if (!string.IsNullOrWhiteSpace(f.Module) && f.Module != "AdminAccess")
            q = q.Where(_ => false);
        if (!string.IsNullOrWhiteSpace(f.Status))
            q = q.Where(_ => f.Status == "Success");   // permission rows are always success
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(p =>
                EF.Functions.ILike(p.ChangedByName, $"%{s}%") ||
                EF.Functions.ILike(p.PermissionKey, $"%{s}%") ||
                (p.User != null && EF.Functions.ILike(p.User.FullName, $"%{s}%")));
        }
        return q;
    }

    public async Task<PagedResult<AuditRow>> ListAsync(AuditFilter f)
    {
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 500);

        // Pull a generous slice from each source (the merge happens in memory). Most audit pages
        // settle into a steady stream of ≤200 events / minute so 2× the page-size from each side
        // gives plenty of headroom for ordering correctness while keeping the round-trips small.
        var slice = Math.Max(size * 4, 200);

        var regularRowsTask = BuildRegular(f).OrderByDescending(a => a.CreatedAt).Take(slice).ToListAsync();
        var permRowsTask    = BuildPermission(f).OrderByDescending(p => p.CreatedAt).Take(slice).ToListAsync();
        await Task.WhenAll(regularRowsTask, permRowsTask);

        var rowKeyToLabel = PermissionCatalog.AllRows.ToDictionary(r => r.Key, r => r.Label);

        var merged = regularRowsTask.Result.Select(MapRegular)
            .Concat(permRowsTask.Result.Select(p => MapPermission(p, rowKeyToLabel)))
            .Where(MatchesActionFilter(f.Action))
            .OrderByDescending(r => r.At)
            .ToList();

        var total = merged.Count;
        var paged = merged.Skip((page - 1) * size).Take(size).ToList();
        return new PagedResult<AuditRow> { Items = paged, TotalCount = total, Page = page, PageSize = size };
    }

    // After merge, apply the action filter — friendly labels make this work end-to-end (the
    // filter dropdown is populated from distinct friendly actions).
    private static Func<AuditRow, bool> MatchesActionFilter(string? action)
        => row => string.IsNullOrWhiteSpace(action) || row.Action == action;

    private static AuditRow MapRegular(RioCommerce.Core.Entities.AuditLog a)
    {
        // Try to extract a per-field change-set from Details JSON. Falls back gracefully when
        // the JSON is free-form text or null.
        var changes = TryExtractChangesFromJson(a.Details);
        var (oldVal, newVal, changedField) = SqueezeForTable(a.OldValue, a.NewValue, changes);

        return new AuditRow(
            a.Id, a.CreatedAt, a.ActorUserId, a.ActorName,
            a.Module, FriendlyAction(a.Action), a.EntityType, a.EntityId, a.EntityName,
            changedField, oldVal, newVal, a.Status,
            a.IpAddress, a.Browser, a.Device, a.UserAgent, a.Details,
            changes);
    }

    private static AuditRow MapPermission(RioCommerce.Core.Entities.PermissionAuditLog p,
        IReadOnlyDictionary<string, string> rowKeyToLabel)
    {
        var label = FriendlyPermissionLabel(p.PermissionKey, rowKeyToLabel);
        string action = p.Action == "Reset" ? "Permission Reset"
                      : p.NewValue == true ? "Permission Granted"
                      : p.NewValue == false ? "Permission Revoked"
                      : "Permission Updated";

        // Synthesise a deterministic Id off the underlying row id so the modal can reopen by id.
        return new AuditRow(
            p.Id, p.CreatedAt, p.ChangedById, p.ChangedByName,
            "AdminAccess", action, "User", p.UserId.ToString(),
            p.User?.FullName ?? p.UserId.ToString(),
            label, FmtTri(p.OldValue), FmtTri(p.NewValue),
            "Success",
            p.IpAddress, null, null, null,
            null,
            new List<AuditChange> { new(label, FmtTri(p.OldValue), FmtTri(p.NewValue)) });
    }

    // tri-state bool? → ON / OFF / —
    private static string FmtTri(bool? v) => v == true ? "ON" : v == false ? "OFF" : "—";

    // ── Friendly permission label ──
    // catalog.products.view → "Products Access" — uses the catalog's row Label so the user
    // sees the same name they toggled in /admin/access-control.
    private static string FriendlyPermissionLabel(string permissionKey, IReadOnlyDictionary<string, string> rowKeyToLabel)
    {
        var dot = permissionKey.LastIndexOf('.');
        var rowKey = dot > 0 ? permissionKey[..dot] : permissionKey;
        if (rowKeyToLabel.TryGetValue(rowKey, out var label)) return $"{label} Access";
        return permissionKey;
    }

    // ── Friendly action ──
    // "ProductPriceChanged" → "Updated" · "CustomerCreated" → "Created" · "OrderStatusUpdated" → "Updated"
    private static string FriendlyAction(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        var a = raw.ToLowerInvariant();
        if (a.Contains("created"))                  return "Created";
        if (a.Contains("updated") || a.Contains("changed") || a.Contains("edited") || a.Contains("saved")) return "Updated";
        if (a.Contains("deleted") || a.Contains("removed") || a.Contains("cancelled")) return "Deleted";
        if (a.Contains("loginsuccess"))             return "Login";
        if (a.Contains("loginfailed"))              return "Login Failed";
        if (a.Contains("logout"))                   return "Logout";
        if (a.Contains("granted"))                  return "Granted";
        if (a.Contains("revoked"))                  return "Revoked";
        if (a.Contains("reset"))                    return "Reset";
        if (a.Contains("blocked"))                  return "Blocked";
        if (a.Contains("anonymized"))               return "Anonymised";
        if (a.Contains("exported"))                 return "Exported";
        if (a.Contains("cloned"))                   return "Cloned";
        if (a.Contains("imported"))                 return "Imported";
        // Strip "Permissions" / "User" prefixes from anything still unmapped + insert spaces.
        var pretty = raw
            .Replace("Permissions", "")
            .Replace("UserOverrides", "")
            .Replace("RoleSaved", "Updated")
            .Replace("RoleCreated", "Created")
            .Replace("RoleCloned", "Cloned")
            .Replace("RoleDeleted", "Deleted");
        var sb = new StringBuilder(pretty.Length + 4);
        for (var i = 0; i < pretty.Length; i++)
        {
            if (i > 0 && char.IsUpper(pretty[i]) && !char.IsUpper(pretty[i - 1])) sb.Append(' ');
            sb.Append(pretty[i]);
        }
        return sb.ToString().Trim();
    }

    // Compress arbitrary Details JSON into a list of {Field, Old, New} triples. Tolerant — if
    // the JSON shape isn't one we know, returns an empty list so the row still renders.
    private static List<AuditChange> TryExtractChangesFromJson(string? json)
    {
        var result = new List<AuditChange>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;

            // Pattern A: {"added": [...keys], "removed": [...keys]}  ← role permission save
            if (root.TryGetProperty("added", out var added) && added.ValueKind == JsonValueKind.Array)
                foreach (var k in added.EnumerateArray())
                    result.Add(new AuditChange(k.GetString() ?? "", "OFF", "ON"));
            if (root.TryGetProperty("removed", out var removed) && removed.ValueKind == JsonValueKind.Array)
                foreach (var k in removed.EnumerateArray())
                    result.Add(new AuditChange(k.GetString() ?? "", "ON", "OFF"));

            // Pattern B: {"changes": [{"field": "Price", "old": "4500", "new": "2500"}, ...]}
            if (root.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                foreach (var c in changes.EnumerateArray())
                {
                    var field = c.TryGetProperty("field", out var fv) ? fv.GetString() ?? "" : "";
                    var oldV  = c.TryGetProperty("old", out var ov) ? ov.ToString() : null;
                    var newV  = c.TryGetProperty("new", out var nv) ? nv.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(field)) result.Add(new AuditChange(field, oldV, newV));
                }
        }
        catch { /* malformed JSON → no synthesised changes */ }
        return result;
    }

    // If the audit row has an explicit Old/New pair, use those. Otherwise compress the changes
    // list into a table-friendly single-line summary ("N Changes" when there are multiple).
    private static (string? oldVal, string? newVal, string? changedField) SqueezeForTable(
        string? rowOld, string? rowNew, List<AuditChange> changes)
    {
        if (!string.IsNullOrWhiteSpace(rowOld) || !string.IsNullOrWhiteSpace(rowNew))
            return (rowOld, rowNew, null);
        if (changes.Count == 0) return (null, null, null);
        if (changes.Count == 1) return (changes[0].OldValue, changes[0].NewValue, changes[0].Field);
        return (null, null, $"{changes.Count} Changes");
    }

    public async Task<AuditSummary> SummaryAsync()
    {
        var todayUtcStart = DateTime.UtcNow.Date;
        var totalAudit  = await _db.AuditLogs.AsNoTracking().Where(a => !BulkPermissionActions.Contains(a.Action)).CountAsync();
        var totalPerms  = await _db.PermissionAuditLogs.AsNoTracking().CountAsync();
        var todayAudit  = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= todayUtcStart && !BulkPermissionActions.Contains(a.Action)).CountAsync();
        var todayPerms  = await _db.PermissionAuditLogs.AsNoTracking().Where(p => p.CreatedAt >= todayUtcStart).CountAsync();
        var staffToday  = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= todayUtcStart && a.ActorUserId != null)
            .Select(a => a.ActorUserId).Distinct().CountAsync();
        var failedLogin = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= todayUtcStart && a.Action == "UserLoginFailed").CountAsync();
        var critical = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= todayUtcStart &&
                       (a.Module == "AdminAccess" || a.Module == "Payments" ||
                        a.Action.Contains("Deleted") || a.Action.Contains("Refund")))
            .CountAsync();
        return new AuditSummary(totalAudit + totalPerms, todayAudit + todayPerms, staffToday, failedLogin, critical + todayPerms);
    }

    public async Task<AuditDistinct> DistinctsAsync()
    {
        var modules = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Module != null && !BulkPermissionActions.Contains(a.Action))
            .Select(a => a.Module!).Distinct().OrderBy(x => x).ToListAsync();
        if (!modules.Contains("AdminAccess")) modules.Add("AdminAccess");   // permission_audit_logs source
        modules.Sort();

        // Distinct friendly actions across both sources — using the same mapping as the table.
        var rawAuditActions = await _db.AuditLogs.AsNoTracking()
            .Where(a => !BulkPermissionActions.Contains(a.Action))
            .Select(a => a.Action).Distinct().ToListAsync();
        var actions = rawAuditActions.Select(FriendlyAction).Distinct().OrderBy(x => x).ToList();
        var permActionsPresent = await _db.PermissionAuditLogs.AsNoTracking()
            .Select(p => p.Action).Distinct().ToListAsync();
        if (permActionsPresent.Any(a => a == "Updated"))                                  actions.Add("Permission Updated");
        if (await _db.PermissionAuditLogs.AsNoTracking().AnyAsync(p => p.NewValue == true))  actions.Add("Permission Granted");
        if (await _db.PermissionAuditLogs.AsNoTracking().AnyAsync(p => p.NewValue == false)) actions.Add("Permission Revoked");
        if (permActionsPresent.Any(a => a == "Reset"))                                    actions.Add("Permission Reset");
        actions = actions.Distinct().OrderBy(x => x).ToList();

        var actors  = await _db.AuditLogs.AsNoTracking()
            .Select(a => a.ActorName).Distinct().OrderBy(x => x).Take(120).ToListAsync();
        return new AuditDistinct(modules, actions, actors);
    }

    public async Task<AuditRow?> GetAsync(Guid id)
    {
        // Try the regular audit_logs table first.
        var a = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a != null && !BulkPermissionActions.Contains(a.Action)) return MapRegular(a);

        // Fall back to permission_audit_logs.
        var p = await _db.PermissionAuditLogs.AsNoTracking().Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id);
        if (p != null)
        {
            var rowKeyToLabel = PermissionCatalog.AllRows.ToDictionary(r => r.Key, r => r.Label);
            return MapPermission(p, rowKeyToLabel);
        }
        return null;
    }

    public async Task<string> ExportCsvAsync(AuditFilter f)
    {
        // Reuse the same merge logic so the CSV is the same data the admin sees on screen.
        var view = await ListAsync(new AuditFilter
        {
            Search = f.Search, FromUtc = f.FromUtc, ToUtc = f.ToUtc,
            ActorUserId = f.ActorUserId, ActorName = f.ActorName,
            Module = f.Module, Action = f.Action, Status = f.Status,
            Page = 1, PageSize = 10_000
        });
        var sb = new StringBuilder();
        sb.AppendLine("Date,Actor,Module,Action,Entity,Changed Field,Old Value,New Value,Status,IP,Browser,Device");
        foreach (var r in view.Items)
        {
            sb.Append(Csv(r.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
              .Append(Csv(r.ActorName)).Append(',')
              .Append(Csv(r.Module)).Append(',')
              .Append(Csv(r.Action)).Append(',')
              .Append(Csv(r.EntityName ?? r.EntityType)).Append(',')
              .Append(Csv(r.ChangedField)).Append(',')
              .Append(Csv(r.OldValue)).Append(',')
              .Append(Csv(r.NewValue)).Append(',')
              .Append(Csv(r.Status)).Append(',')
              .Append(Csv(r.IpAddress)).Append(',')
              .Append(Csv(r.Browser)).Append(',')
              .Append(Csv(r.Device)).AppendLine();
        }
        return sb.ToString();
    }
    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
