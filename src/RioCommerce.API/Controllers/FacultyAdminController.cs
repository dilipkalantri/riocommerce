using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/faculty")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class FacultyAdminController : ControllerBase
{
    private readonly ICatalogAdminService _catalog;
    public FacultyAdminController(ICatalogAdminService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<FacultyAdminItem>>>> List()
        => Ok(ApiResponse<List<FacultyAdminItem>>.Ok(await _catalog.ListFacultyAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<FacultyEditModel>>> Get(Guid id)
    {
        var m = await _catalog.GetFacultyAsync(id);
        return m == null ? NotFound(ApiResponse<FacultyEditModel>.Fail("Faculty not found")) : Ok(ApiResponse<FacultyEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] FacultyEditModel model)
    {
        var (ok, error, id) = await _catalog.SaveFacultyAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _catalog.ToggleFacultyAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _catalog.DeleteFacultyAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
