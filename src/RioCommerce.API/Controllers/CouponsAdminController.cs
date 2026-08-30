using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/coupons")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class CouponsAdminController : ControllerBase
{
    private readonly ICouponAdminService _coupons;
    public CouponsAdminController(ICouponAdminService coupons) => _coupons = coupons;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CouponAdminItem>>>> List()
        => Ok(ApiResponse<List<CouponAdminItem>>.Ok(await _coupons.ListAsync()));

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<CouponStats>>> Stats()
        => Ok(ApiResponse<CouponStats>.Ok(await _coupons.StatsAsync()));

    [HttpGet("affiliate-options")]
    public async Task<ActionResult<ApiResponse<List<AffiliateOption>>>> AffiliateOptions()
        => Ok(ApiResponse<List<AffiliateOption>>.Ok(await _coupons.AffiliateOptionsAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CouponEditModel>>> Get(Guid id)
    {
        var m = await _coupons.GetAsync(id);
        return m == null ? NotFound(ApiResponse<CouponEditModel>.Fail("Coupon not found")) : Ok(ApiResponse<CouponEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] CouponEditModel model)
    {
        var (ok, error, id) = await _coupons.SaveAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _coupons.ToggleAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _coupons.DeleteAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
