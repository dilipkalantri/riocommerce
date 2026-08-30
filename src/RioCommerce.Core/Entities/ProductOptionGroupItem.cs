namespace RioCommerce.Core.Entities;

/// <summary>A single selectable option within a <see cref="ProductOptionGroup"/>. Its
/// <see cref="PriceAddOn"/> is ADDED to the product's base price when selected (0 = no extra).</summary>
public class ProductOptionGroupItem : BaseEntity
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAddOn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ProductOptionGroup Group { get; set; } = null!;
}
