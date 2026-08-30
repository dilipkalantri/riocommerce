namespace RioCommerce.Core.Entities;

// A reusable value belonging to a ProductAttribute, used as a template when mapping the attribute to a product.
public class PredefinedProductAttributeValue : BaseEntity
{
    public Guid ProductAttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
    public bool PriceAdjustmentUsePercentage { get; set; }
    public bool IsPreSelected { get; set; }
    public int DisplayOrder { get; set; }
    public ProductAttribute ProductAttribute { get; set; } = null!;
}
