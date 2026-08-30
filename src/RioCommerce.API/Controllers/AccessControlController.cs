using RioCommerce.Core.DTOs.Access;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace RioCommerce.API.Controllers;

/// <summary>
/// REST surface for the Admin Access Control feature. Every route is Super-Admin only — the
/// platform's single guardrail against staff escalating their own permissions.
/// </summary>
[ApiController]
[Route("api/admin/access-control")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin")]
public class AccessControlController : ControllerBase
{
    private readonly IPermissionService _perms;
    public AccessControlController(IPermissionService perms) => _perms = perms;

    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : Guid.Empty;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "admin";
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Returns the static permission tree the admin page renders.</summary>
    [HttpGet("catalog")]
    public ActionResult<ApiResponse<PermissionCatalogResponse>> Catalog()
        => Ok(ApiResponse<PermissionCatalogResponse>.Ok(new PermissionCatalogResponse(PermissionCatalog.Groups, PermissionCatalog.Actions)));

    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<List<AccessRoleRow>>>> Roles()
        => Ok(ApiResponse<List<AccessRoleRow>>.Ok(await _perms.ListRolesAsync()));

    [HttpGet("roles/{id:guid}")]
    public async Task<ActionResult<ApiResponse<RolePermissionState>>> RoleState(Guid id)
    {
        var s = await _perms.GetRoleStateAsync(id);
        return s == null ? NotFound(ApiResponse<RolePermissionState>.Fail("Role not found")) : Ok(ApiResponse<RolePermissionState>.Ok(s));
    }

    [HttpPost("roles/save")]
    public async Task<ActionResult<ApiResponse<string>>> SaveRole([FromBody] SaveRolePermissionsRequest req)
    {
        var (ok, error) = await _perms.SaveRolePermissionsAsync(req, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Saved")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("users/{id:guid}/overrides")]
    public async Task<ActionResult<ApiResponse<UserPermissionOverridesState>>> UserOverrides(Guid id)
    {
        var s = await _perms.GetUserOverridesAsync(id);
        return s == null ? NotFound(ApiResponse<UserPermissionOverridesState>.Fail("User not found")) : Ok(ApiResponse<UserPermissionOverridesState>.Ok(s));
    }

    [HttpPost("users/save")]
    public async Task<ActionResult<ApiResponse<string>>> SaveUserOverrides([FromBody] SaveUserOverridesRequest req)
    {
        var (ok, error) = await _perms.SaveUserOverridesAsync(req, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Saved")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    /// <summary>Delta save — only the toggles the admin actually changed are written. Used by the
    /// optimistic auto-save path on the Individual User Permissions tab; sub-500ms in practice.</summary>
    [HttpPatch("users/overrides")]
    public async Task<ActionResult<ApiResponse<int>>> ApplyUserOverridesDelta([FromBody] SaveUserOverridesDeltaRequest req)
    {
        var (ok, error, affected) = await _perms.ApplyUserOverridesDeltaAsync(req, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<int>.Ok(affected, "Saved")) : BadRequest(ApiResponse<int>.Fail(error!));
    }

    /// <summary>Remove every override row for a user — sends them back to "Inherit from Role".</summary>
    [HttpDelete("users/{id:guid}/overrides")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveUserOverrides(Guid id)
    {
        var (ok, error) = await _perms.RemoveUserOverridesAsync(id, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Reset")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    /// <summary>Per-user permission change history — drives the "Audit Log" panel on the Individual tab.</summary>
    [HttpGet("users/{id:guid}/history")]
    public async Task<ActionResult<ApiResponse<List<RioCommerce.Core.DTOs.Admin.AuditLogItem>>>> UserHistory(Guid id, [FromQuery] int take = 25)
        => Ok(ApiResponse<List<RioCommerce.Core.DTOs.Admin.AuditLogItem>>.Ok(await _perms.GetUserPermissionHistoryAsync(id, take)));

    /// <summary>Filterable Permission Change History for the timeline table.</summary>
    [HttpGet("users/{id:guid}/audit")]
    public async Task<ActionResult<ApiResponse<PagedAuditResult>>> UserAudit(Guid id, [FromQuery] PermissionHistoryFilter filter)
    {
        filter.UserId = id;
        var (rows, total) = await _perms.GetUserPermissionAuditAsync(filter);
        return Ok(ApiResponse<PagedAuditResult>.Ok(new PagedAuditResult(rows, total, filter.Page, filter.PageSize)));
    }

    /// <summary>Last permission change for the user — feeds "Last Updated / Updated By" on the profile.</summary>
    [HttpGet("users/{id:guid}/last-change")]
    public async Task<ActionResult<ApiResponse<LastPermissionChange?>>> UserLastChange(Guid id)
        => Ok(ApiResponse<LastPermissionChange?>.Ok(await _perms.GetUserLastChangeAsync(id)));

    /// <summary>Cross-user history table — only users with at least one custom override appear.</summary>
    [HttpGet("audit")]
    public async Task<ActionResult<ApiResponse<PagedAuditResult>>> GlobalAudit([FromQuery] PermissionHistoryFilter filter)
    {
        var (rows, total) = await _perms.GetGlobalPermissionAuditAsync(filter);
        return Ok(ApiResponse<PagedAuditResult>.Ok(new PagedAuditResult(rows, total, filter.Page, filter.PageSize)));
    }

    /// <summary>🗑 button on a history row — clear every override under a single module-row for the user.</summary>
    [HttpDelete("users/{id:guid}/overrides/row/{rowKey}")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveRow(Guid id, string rowKey)
    {
        var (ok, error) = await _perms.RemoveUserOverrideForRowAsync(id, rowKey, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Removed")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    /// <summary>"Users With Backend Access" list — drives the table on the simplified page.</summary>
    [HttpGet("backend-users")]
    public async Task<ActionResult<ApiResponse<List<BackendAccessUser>>>> BackendUsers([FromQuery] string? search = null, [FromQuery] int take = 100)
        => Ok(ApiResponse<List<BackendAccessUser>>.Ok(await _perms.ListBackendAccessUsersAsync(search, take)));

    public record PagedAuditResult(List<PermissionAuditRow> Rows, int Total, int Page, int PageSize);

    [HttpPost("roles/clone")]
    public async Task<ActionResult<ApiResponse<Guid>>> Clone([FromBody] CloneRoleRequest req)
    {
        var (ok, error, id) = await _perms.CloneRoleAsync(req, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Cloned")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("roles/create")]
    public async Task<ActionResult<ApiResponse<Guid>>> Create([FromBody] CreateRoleRequest req)
    {
        var (ok, error, id) = await _perms.CreateRoleAsync(req, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Created")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("roles/rename")]
    public async Task<ActionResult<ApiResponse<string>>> Rename([FromBody] RenameRoleRequest req)
    {
        var (ok, error) = await _perms.RenameRoleAsync(req, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Renamed")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpDelete("roles/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _perms.DeleteRoleAsync(id, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var bundle = await _perms.ExportAllAsync();
        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true });
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json",
            $"riocommerce-rbac-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    [HttpPost("import")]
    public async Task<ActionResult<ApiResponse<int>>> Import([FromBody] PermissionExportBundle bundle)
    {
        var (ok, error, n) = await _perms.ImportAsync(bundle, ActorId, ActorName, Ip);
        return ok ? Ok(ApiResponse<int>.Ok(n, $"Imported {n} role(s)")) : BadRequest(ApiResponse<int>.Fail(error!));
    }
}
