using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
public class UsersAdminController : ControllerBase
{
    private readonly IAdminUserService _users;
    public UsersAdminController(IAdminUserService users) => _users = users;

    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : Guid.Empty;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "admin";

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AdminUserStats>>> Stats()
        => Ok(ApiResponse<AdminUserStats>.Ok(await _users.StatsAsync()));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AdminUserItem>>>> List([FromQuery] string? search = null)
        => Ok(ApiResponse<List<AdminUserItem>>.Ok(await _users.ListAsync(search)));

    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<List<RoleOption>>>> Roles()
        => Ok(ApiResponse<List<RoleOption>>.Ok(await _users.RolesAsync()));

    [HttpPost("{id:guid}/roles/grant")]
    public async Task<ActionResult<ApiResponse<string>>> Grant(Guid id, [FromQuery] string role)
    {
        var (ok, error) = await _users.GrantRoleAsync(id, role, ActorId, ActorName);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Role granted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("{id:guid}/roles/revoke")]
    public async Task<ActionResult<ApiResponse<string>>> Revoke(Guid id, [FromQuery] string role)
    {
        var (ok, error) = await _users.RevokeRoleAsync(id, role, ActorId, ActorName);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Role revoked")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("{id:guid}/toggle-active")]
    public async Task<ActionResult<ApiResponse<string>>> ToggleActive(Guid id)
    {
        var (ok, error) = await _users.ToggleActiveAsync(id, ActorId, ActorName);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Updated")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("{id:guid}/export")]
    public async Task<ActionResult<ApiResponse<UserDataExport>>> Export(Guid id)
    {
        var data = await _users.ExportUserDataAsync(id);
        return data == null ? NotFound(ApiResponse<UserDataExport>.Fail("User not found")) : Ok(ApiResponse<UserDataExport>.Ok(data));
    }

    [HttpPost("{id:guid}/anonymize")]
    public async Task<ActionResult<ApiResponse<string>>> Anonymize(Guid id)
    {
        var (ok, error) = await _users.AnonymizeUserAsync(id, ActorId, ActorName);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "User anonymised")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
