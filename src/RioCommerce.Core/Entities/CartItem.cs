namespace RioCommerce.Core.Entities;
public class CartItem : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductModeId { get; set; }
    public int Quantity { get; set; } = 1;
    // Selected product attributes (resolved JSON: name, value, per-value price adjustment) and their
    // summed price impact, computed server-side and added on top of the base/mode price.
    public string? SelectedAttributesJson { get; set; }
    public string? SelectedOptionIdsJson { get; set; }   // configurable purchase-option picks (JSON array of option Guids)
    public decimal AttributePriceAdjustment { get; set; }
    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductMode? ProductMode { get; set; }
}
