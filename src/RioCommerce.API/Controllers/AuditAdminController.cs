using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/audit")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
public class AuditAdminController : ControllerBase
{
    private readonly IAuditService _audit;
    public AuditAdminController(IAuditService audit) => _audit = audit;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AuditLogItem>>>> List([FromQuery] int take = 100)
        => Ok(ApiResponse<List<AuditLogItem>>.Ok(await _audit.ListAsync(take)));
}
