using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

// Admin-only real-time global-URL availability check (debounced from the slug fields).
[ApiController]
[Route("api/seo")]
[Authorize(Roles = "super_admin,admin,operations,backend_user")]
public class SeoController : ControllerBase
{
    private readonly ISeoUrlService _seo;
    public SeoController(ISeoUrlService seo) => _seo = seo;

    /// <summary>GET /api/seo/check?slug=x&amp;type=Category&amp;id=... → is this public URL available (excluding self)?</summary>
    [HttpGet("check")]
    public async Task<ActionResult<ApiResponse<GlobalUrlLookupResult>>> Check(
        [FromQuery] string slug, [FromQuery] string? type = null, [FromQuery] Guid? id = null, CancellationToken ct = default)
        => Ok(ApiResponse<GlobalUrlLookupResult>.Ok(await _seo.CheckAsync(slug, type, id, ct)));
}
