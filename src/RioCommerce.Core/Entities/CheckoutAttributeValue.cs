namespace RioCommerce.Core.Entities;

// A selectable value for a CheckoutAttribute, with an optional price adjustment applied to the order total.
public class CheckoutAttributeValue : BaseEntity
{
    public Guid CheckoutAttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
    public bool PriceAdjustmentUsePercentage { get; set; }
    public bool IsPreSelected { get; set; }
    public int DisplayOrder { get; set; }
    public CheckoutAttribute CheckoutAttribute { get; set; } = null!;
}
