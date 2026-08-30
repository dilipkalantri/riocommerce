using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Orders;

// ── Payment transactions ──
/// <param name="GatewayMode">Gateway-reported instrument for this attempt ("UPI", "Credit Card", …).
/// Null for manual/offline rows and for gateway rows settled before instrument capture existed.</param>
public record PaymentTransactionDto(
    Guid Id, string Gateway, string? GatewayTransactionId, PaymentStatus Status, decimal Amount, string Currency,
    PaymentMode Method, bool IsRefund, string? Reference, string? ResponseMessage, string? RawResponseJson,
    string CreatedByName, DateTime CreatedAt, DateTime? PaidOnUtc, string? GatewayMode = null);

public class ManualPaymentRequest
{
    public decimal Amount { get; set; }
    public PaymentMode Method { get; set; } = PaymentMode.Cash;
    public string? Reference { get; set; }      // UTR / cheque no / bank ref
    public string? Notes { get; set; }
    public bool MarkOrderPaid { get; set; } = true;
}

// ── Refunds ──
public record RefundableItemDto(
    Guid OrderItemId, string ProductTitle, int Quantity, int RefundedQuantity, int RefundableQuantity,
    decimal UnitPrice, decimal LineTotal);

public class RefundItemRequest { public Guid OrderItemId { get; set; } public int Quantity { get; set; } }

public class RefundRequest
{
    public string Type { get; set; } = "Partial";   // Full | Partial | Item | Offline
    public decimal Amount { get; set; }              // Partial / Offline custom amount
    public string? Reason { get; set; }
    public bool IsOffline { get; set; }
    public List<RefundItemRequest> Items { get; set; } = new();
}

public record RefundLineDto(Guid OrderItemId, string ProductTitle, int Quantity, decimal Amount);

public record RefundDto(
    Guid Id, decimal Amount, string? Reason, RefundStatus Status, string RefundType, bool IsOffline,
    string? GatewayRefundId, string? FailureReason, string InitiatedByName, DateTime CreatedAt, List<RefundLineDto> Items);

public class RefundSummaryDto
{
    public decimal OrderTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RemainingRefundable { get; set; }
    public bool CanRefund { get; set; }
    public List<RefundableItemDto> Items { get; set; } = new();
    public List<RefundDto> History { get; set; } = new();
}
