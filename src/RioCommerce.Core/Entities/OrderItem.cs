namespace RioCommerce.Core.Entities;
public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductModeId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public string? ModeName { get; set; }
    // Snapshot of the product attributes the buyer selected (price impact is baked into UnitPrice).
    public string? SelectedAttributesJson { get; set; }
    public string? SelectedOptionIdsJson { get; set; }   // configurable purchase-option picks (JSON array of option Guids)
    public string? SelectedOptionsJson { get; set; }      // audit snapshot: [{groupName, optionName, addOn}] at time of order
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal? GstRate { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }
    public string? Attempt { get; set; }
    public bool IsActivated { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductMode? ProductMode { get; set; }
}
