namespace RioCommerce.Core.Entities;

/// <summary>Many-to-many join between Product and Category.
/// A product can belong to multiple categories; one may optionally be marked primary.</summary>
public class ProductCategory : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }
    public bool IsPrimary { get; set; }
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
