using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Content;
namespace RioCommerce.Core.Interfaces;

public interface IContentService
{
    Task<ContentStats> StatsAsync();

    // CMS pages
    Task<CmsPageView?> GetPageAsync(string slug);
    Task<List<CmsPageItem>> ListPagesAsync();
    Task<CmsPageEditModel?> GetPageForEditAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SavePageAsync(CmsPageEditModel model);
    Task TogglePageAsync(Guid id);
    Task<(bool ok, string? error)> DeletePageAsync(Guid id);

    // Blog
    Task<PagedResult<BlogListItem>> PublishedPostsAsync(int page, int pageSize);
    Task<BlogPostView?> GetPostAsync(string slug);   // increments view count
    Task<List<BlogAdminItem>> ListPostsAsync();
    Task<BlogEditModel?> GetPostForEditAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SavePostAsync(BlogEditModel model);
    Task TogglePostAsync(Guid id);
    Task<(bool ok, string? error)> DeletePostAsync(Guid id);
    Task<string> UploadBlogImageAsync(string extension, Stream content);   // saves to /uploads/blog, returns relative path

    // For sitemap.xml
    Task<(List<string> pageSlugs, List<string> blogSlugs)> PublishedSlugsAsync();
}
