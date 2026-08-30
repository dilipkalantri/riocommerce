namespace RioCommerce.Core.Entities;
public class CmsPage : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;   // HTML
    public bool IsPublished { get; set; } = true;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public int DisplayOrder { get; set; }
}
