using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/products")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class ProductsAdminController : ControllerBase
{
    private readonly IProductAdminService _svc;
    public ProductsAdminController(IProductAdminService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductListItem>>>> List([FromQuery] ProductFilterRequest filter)
        => Ok(ApiResponse<PagedResult<ProductListItem>>.Ok(await _svc.ListAsync(filter)));

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AdminProductStats>>> Stats()
        => Ok(ApiResponse<AdminProductStats>.Ok(await _svc.StatsAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProductEditModel>>> Get(Guid id)
    {
        var model = await _svc.GetForEditAsync(id);
        return model == null ? NotFound(ApiResponse<ProductEditModel>.Fail("Not found")) : Ok(ApiResponse<ProductEditModel>.Ok(model));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Create([FromBody] ProductEditModel model)
    {
        model.Id = null;
        return Ok(ApiResponse<Guid>.Ok(await _svc.SaveAsync(model), "Product created"));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<Guid>>> Update(Guid id, [FromBody] ProductEditModel model)
    {
        model.Id = id;
        return Ok(ApiResponse<Guid>.Ok(await _svc.SaveAsync(model), "Product updated"));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _svc.ToggleStatusAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Status toggled"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        await _svc.DeleteAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Product deleted"));
    }
}
