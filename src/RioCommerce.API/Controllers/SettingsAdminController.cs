using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
public class SettingsAdminController : ControllerBase
{
    private readonly ISiteSettingsService _settings;
    public SettingsAdminController(ISiteSettingsService settings) => _settings = settings;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<SiteSettings>>> Get()
        => Ok(ApiResponse<SiteSettings>.Ok(await _settings.GetAsync()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<string>>> Save([FromBody] SiteSettings settings)
    {
        await _settings.SaveAsync(settings);
        return Ok(ApiResponse<string>.Ok("ok", "Settings saved"));
    }
}
