namespace RioCommerce.Core.Entities;
public class ReturnRequest : BaseEntity
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
    public string Status { get; set; } = "Requested";   // Requested | Approved | Rejected | Refunded
    public DateTime? ResolvedAt { get; set; }
    public Order Order { get; set; } = null!;
}
