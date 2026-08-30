namespace RioCommerce.Core.Entities;

// Assigns a specification option to a product, controlling storefront display and filtering.
public class ProductSpecificationAttribute : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid SpecificationAttributeOptionId { get; set; }
    public bool AllowFiltering { get; set; }
    public bool ShowOnProductPage { get; set; } = true;
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
    public SpecificationAttributeOption SpecificationAttributeOption { get; set; } = null!;
}
