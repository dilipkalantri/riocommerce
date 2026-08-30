using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/product-attributes")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class ProductAttributesAdminController : ControllerBase
{
    private readonly IAttributeAdminService _svc;
    public ProductAttributesAdminController(IAttributeAdminService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProductAttributeAdminItem>>>> List()
        => Ok(ApiResponse<List<ProductAttributeAdminItem>>.Ok(await _svc.ListProductAttributesAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProductAttributeEditModel>>> Get(Guid id)
    {
        var m = await _svc.GetProductAttributeAsync(id);
        return m == null ? NotFound(ApiResponse<ProductAttributeEditModel>.Fail("Not found")) : Ok(ApiResponse<ProductAttributeEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] ProductAttributeEditModel model)
    {
        var (ok, error, id) = await _svc.SaveProductAttributeAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _svc.DeleteProductAttributeAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
