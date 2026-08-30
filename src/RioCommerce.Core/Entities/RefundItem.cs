namespace RioCommerce.Core.Entities;

// A line within a refund — tracks how much of a specific order item was refunded (for item-level refunds).
public class RefundItem : BaseEntity
{
    public Guid RefundId { get; set; }
    public Guid OrderItemId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public Refund Refund { get; set; } = null!;
}
