using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/content")]
public class ContentController : ControllerBase
{
    private readonly IContentService _content;
    public ContentController(IContentService content) => _content = content;

    [HttpGet("page/{slug}")]
    public async Task<ActionResult<ApiResponse<CmsPageView>>> Page(string slug)
    {
        var p = await _content.GetPageAsync(slug);
        return p == null ? NotFound(ApiResponse<CmsPageView>.Fail("Page not found")) : Ok(ApiResponse<CmsPageView>.Ok(p));
    }

    [HttpGet("blog")]
    public async Task<ActionResult<ApiResponse<PagedResult<BlogListItem>>>> Blog([FromQuery] int page = 1, [FromQuery] int pageSize = 9)
        => Ok(ApiResponse<PagedResult<BlogListItem>>.Ok(await _content.PublishedPostsAsync(page, pageSize)));

    [HttpGet("blog/{slug}")]
    public async Task<ActionResult<ApiResponse<BlogPostView>>> Post(string slug)
    {
        var p = await _content.GetPostAsync(slug);
        return p == null ? NotFound(ApiResponse<BlogPostView>.Fail("Post not found")) : Ok(ApiResponse<BlogPostView>.Ok(p));
    }
}
