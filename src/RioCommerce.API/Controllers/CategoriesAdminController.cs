using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/categories")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class CategoriesAdminController : ControllerBase
{
    private readonly ICatalogAdminService _catalog;
    public CategoriesAdminController(ICatalogAdminService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CategoryAdminItem>>>> List()
        => Ok(ApiResponse<List<CategoryAdminItem>>.Ok(await _catalog.ListCategoriesAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CategoryEditModel>>> Get(Guid id)
    {
        var m = await _catalog.GetCategoryAsync(id);
        return m == null ? NotFound(ApiResponse<CategoryEditModel>.Fail("Category not found")) : Ok(ApiResponse<CategoryEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] CategoryEditModel model)
    {
        var (ok, error, id) = await _catalog.SaveCategoryAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _catalog.ToggleCategoryAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _catalog.DeleteCategoryAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
