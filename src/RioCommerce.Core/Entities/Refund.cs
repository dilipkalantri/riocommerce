using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

public class Refund : BaseEntity
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public string RefundType { get; set; } = "Partial";        // Full | Partial | Item | Offline
    public bool IsOffline { get; set; }
    public string? Gateway { get; set; }
    public string? GatewayRefundId { get; set; }
    public string? ResponseMessage { get; set; }
    public string? FailureReason { get; set; }
    public Guid? PaymentTransactionId { get; set; }            // the refund ledger entry
    public string InitiatedByName { get; set; } = "system";
    public Guid? InitiatedById { get; set; }
    public Order Order { get; set; } = null!;
    public ICollection<RefundItem> Items { get; set; } = new List<RefundItem>();
}
