using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Leads;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeadsController : ControllerBase
{
    private readonly ILeadService _svc;
    public LeadsController(ILeadService svc) => _svc = svc;

    // Public enquiry capture (no auth — used by the storefront enquiry popup / forms).
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> Capture([FromBody] CaptureLeadRequest request)
    {
        await _svc.CaptureAsync(request);
        return Ok(ApiResponse<string>.Ok("ok", "Enquiry received"));
    }

    [HttpGet]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
    public async Task<ActionResult<ApiResponse<PagedResult<LeadRow>>>> List([FromQuery] LeadFilter filter)
        => Ok(ApiResponse<PagedResult<LeadRow>>.Ok(await _svc.ListAsync(filter)));

    [HttpPost("{id:guid}/status")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
    public async Task<ActionResult<ApiResponse<string>>> SetStatus(Guid id, [FromQuery] LeadStatus status)
    {
        await _svc.UpdateStatusAsync(id, status);
        return Ok(ApiResponse<string>.Ok("ok", "Lead updated"));
    }
}
