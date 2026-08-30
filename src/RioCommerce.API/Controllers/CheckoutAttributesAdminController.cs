using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/checkout-attributes")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class CheckoutAttributesAdminController : ControllerBase
{
    private readonly IAttributeAdminService _svc;
    public CheckoutAttributesAdminController(IAttributeAdminService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CheckoutAttributeAdminItem>>>> List()
        => Ok(ApiResponse<List<CheckoutAttributeAdminItem>>.Ok(await _svc.ListCheckoutAttributesAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CheckoutAttributeEditModel>>> Get(Guid id)
    {
        var m = await _svc.GetCheckoutAttributeAsync(id);
        return m == null ? NotFound(ApiResponse<CheckoutAttributeEditModel>.Fail("Not found")) : Ok(ApiResponse<CheckoutAttributeEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] CheckoutAttributeEditModel model)
    {
        var (ok, error, id) = await _svc.SaveCheckoutAttributeAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _svc.ToggleCheckoutAttributeAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _svc.DeleteCheckoutAttributeAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
