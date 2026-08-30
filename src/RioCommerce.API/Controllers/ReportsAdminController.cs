using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/reports")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class ReportsAdminController : ControllerBase
{
    private readonly IReportsService _reports;
    public ReportsAdminController(IReportsService reports) => _reports = reports;

    [HttpGet("sales")]
    public async Task<ActionResult<ApiResponse<SalesReport>>> Sales()
        => Ok(ApiResponse<SalesReport>.Ok(await _reports.SalesAsync()));
}
