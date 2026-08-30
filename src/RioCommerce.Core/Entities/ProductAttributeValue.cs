namespace RioCommerce.Core.Entities;

// A selectable value for a product's attribute mapping (e.g. "6 months", "12 months") with optional price adjustment.
public class ProductAttributeValue : BaseEntity
{
    public Guid ProductAttributeMappingId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
    public bool PriceAdjustmentUsePercentage { get; set; }
    public bool IsPreSelected { get; set; }
    public int DisplayOrder { get; set; }
    public ProductAttributeMapping ProductAttributeMapping { get; set; } = null!;
}
