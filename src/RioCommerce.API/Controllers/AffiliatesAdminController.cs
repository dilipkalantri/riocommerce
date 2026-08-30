using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/affiliates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class AffiliatesAdminController : ControllerBase
{
    private readonly IAffiliateService _affiliates;
    public AffiliatesAdminController(IAffiliateService affiliates) => _affiliates = affiliates;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AffiliateAdminItem>>>> List()
        => Ok(ApiResponse<List<AffiliateAdminItem>>.Ok(await _affiliates.ListAsync()));

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AffiliateStats>>> Stats()
        => Ok(ApiResponse<AffiliateStats>.Ok(await _affiliates.StatsAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<AffiliateEditModel>>> Get(Guid id)
    {
        var m = await _affiliates.GetAsync(id);
        return m == null ? NotFound(ApiResponse<AffiliateEditModel>.Fail("Affiliate not found")) : Ok(ApiResponse<AffiliateEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] AffiliateEditModel model)
    {
        var (ok, error, id) = await _affiliates.SaveAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _affiliates.ToggleAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _affiliates.DeleteAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("referrals")]
    public async Task<ActionResult<ApiResponse<List<AffiliateReferralItem>>>> Referrals([FromQuery] Guid? affiliateId = null)
        => Ok(ApiResponse<List<AffiliateReferralItem>>.Ok(await _affiliates.ReferralsAsync(affiliateId)));

    [HttpPost("referrals/{id:guid}/pay")]
    public async Task<ActionResult<ApiResponse<string>>> MarkPaid(Guid id)
    {
        await _affiliates.MarkPaidAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Marked paid"));
    }
}
