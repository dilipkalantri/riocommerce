namespace RioCommerce.Core.Entities;

/// <summary>A configurable, admin-named purchase-option group on a product (e.g. "Books",
/// "Test Series"). A product can have any number of groups. The customer picks exactly one
/// option per group shown; each option adds its PriceAddOn to the base price. Lecture Modes
/// remain a separate first-class feature (ProductMode); this supplements it.</summary>
public class ProductOptionGroup : BaseEntity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public Product Product { get; set; } = null!;
    public List<ProductOptionGroupItem> Items { get; set; } = new();
}
