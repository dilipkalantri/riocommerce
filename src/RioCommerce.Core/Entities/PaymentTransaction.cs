using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// Enterprise payment ledger: one row per payment/refund attempt (gateway, manual, offline).
// Future-ready for Razorpay/Stripe/Cashfree — stores raw gateway responses + parent links for refunds.
public class PaymentTransaction : BaseEntity
{
    public Guid OrderId { get; set; }
    public string Gateway { get; set; } = "Manual";            // TestGateway | Razorpay | Manual | Offline …
    public string? GatewayTransactionId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Success;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string? ResponseCode { get; set; }
    public string? ResponseMessage { get; set; }
    public string? RawResponseJson { get; set; }
    public PaymentMode PaymentMethod { get; set; }
    public bool IsRefund { get; set; }
    public Guid? ParentTransactionId { get; set; }             // links a refund back to the capture
    public DateTime? PaidOnUtc { get; set; }
    public string? Reference { get; set; }                     // UTR / cheque no / bank ref
    public string CreatedByName { get; set; } = "system";
    public Guid? CreatedById { get; set; }
    public string? Notes { get; set; }
    public Order Order { get; set; } = null!;
}
