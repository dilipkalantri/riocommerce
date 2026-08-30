using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/specification-attributes")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class SpecificationAttributesAdminController : ControllerBase
{
    private readonly IAttributeAdminService _svc;
    public SpecificationAttributesAdminController(IAttributeAdminService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SpecificationAttributeAdminItem>>>> List()
        => Ok(ApiResponse<List<SpecificationAttributeAdminItem>>.Ok(await _svc.ListSpecificationAttributesAsync()));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SpecificationAttributeEditModel>>> Get(Guid id)
    {
        var m = await _svc.GetSpecificationAttributeAsync(id);
        return m == null ? NotFound(ApiResponse<SpecificationAttributeEditModel>.Fail("Not found")) : Ok(ApiResponse<SpecificationAttributeEditModel>.Ok(m));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<Guid>>> Save([FromBody] SpecificationAttributeEditModel model)
    {
        var (ok, error, id) = await _svc.SaveSpecificationAttributeAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _svc.DeleteSpecificationAttributeAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    // ── Groups ──
    [HttpGet("groups")]
    public async Task<ActionResult<ApiResponse<List<SpecAttributeGroupItem>>>> ListGroups()
        => Ok(ApiResponse<List<SpecAttributeGroupItem>>.Ok(await _svc.ListSpecGroupsAsync()));

    [HttpPost("groups")]
    public async Task<ActionResult<ApiResponse<Guid>>> SaveGroup([FromBody] SpecAttributeGroupEditModel model)
    {
        var (ok, error, id) = await _svc.SaveSpecGroupAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpDelete("groups/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteGroup(Guid id)
    {
        var (ok, error) = await _svc.DeleteSpecGroupAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
