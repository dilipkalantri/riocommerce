namespace RioCommerce.Core.Entities;

// A node in the storefront "mega menu". Top-level items (ParentId == null) render as main-nav entries;
// their children render as a dropdown. URL is the resolved link target (e.g. /page/faq, /courses/ca-foundation).
public class MenuItem : BaseEntity
{
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = "#";
    public int DisplayOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool OpenInNewTab { get; set; }
    public MenuItem? Parent { get; set; }
    public ICollection<MenuItem> Children { get; set; } = new List<MenuItem>();
}
