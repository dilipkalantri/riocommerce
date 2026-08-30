namespace RioCommerce.Core.Entities;
public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    // ── nopCommerce-style category fields ──
    public bool ShowOnHomePage { get; set; }
    public bool IncludeInTopMenu { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoKeywords { get; set; }
    public string? SeoDescription { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
}
