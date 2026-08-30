using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/content")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class ContentAdminController : ControllerBase
{
    private readonly IContentService _content;
    public ContentAdminController(IContentService content) => _content = content;

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<ContentStats>>> Stats()
        => Ok(ApiResponse<ContentStats>.Ok(await _content.StatsAsync()));

    // ── pages ──
    [HttpGet("pages")]
    public async Task<ActionResult<ApiResponse<List<CmsPageItem>>>> Pages()
        => Ok(ApiResponse<List<CmsPageItem>>.Ok(await _content.ListPagesAsync()));

    [HttpGet("pages/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CmsPageEditModel>>> Page(Guid id)
    {
        var m = await _content.GetPageForEditAsync(id);
        return m == null ? NotFound(ApiResponse<CmsPageEditModel>.Fail("Page not found")) : Ok(ApiResponse<CmsPageEditModel>.Ok(m));
    }

    [HttpPost("pages")]
    public async Task<ActionResult<ApiResponse<Guid>>> SavePage([FromBody] CmsPageEditModel model)
    {
        var (ok, error, id) = await _content.SavePageAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("pages/{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> TogglePage(Guid id)
    {
        await _content.TogglePageAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("pages/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> DeletePage(Guid id)
    {
        var (ok, error) = await _content.DeletePageAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    // ── blog ──
    [HttpGet("posts")]
    public async Task<ActionResult<ApiResponse<List<BlogAdminItem>>>> Posts()
        => Ok(ApiResponse<List<BlogAdminItem>>.Ok(await _content.ListPostsAsync()));

    [HttpGet("posts/{id:guid}")]
    public async Task<ActionResult<ApiResponse<BlogEditModel>>> Post(Guid id)
    {
        var m = await _content.GetPostForEditAsync(id);
        return m == null ? NotFound(ApiResponse<BlogEditModel>.Fail("Post not found")) : Ok(ApiResponse<BlogEditModel>.Ok(m));
    }

    [HttpPost("posts")]
    public async Task<ActionResult<ApiResponse<Guid>>> SavePost([FromBody] BlogEditModel model)
    {
        var (ok, error, id) = await _content.SavePostAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("posts/{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> TogglePost(Guid id)
    {
        await _content.TogglePostAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("posts/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> DeletePost(Guid id)
    {
        var (ok, error) = await _content.DeletePostAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
