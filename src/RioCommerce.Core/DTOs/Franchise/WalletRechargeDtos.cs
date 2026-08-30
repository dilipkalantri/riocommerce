using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Franchise;

public class InitiateRechargeRequest
{
    public decimal Amount { get; set; }
}

// Returned to the franchisee after Initiate — the UI hands these to the gateway widget.
public record RechargeIntent(
    Guid RechargeId, string GatewayName, string GatewayOrderId, string GatewayKey, decimal Amount);

public class ConfirmRechargeRequest
{
    public Guid RechargeId { get; set; }
    public string GatewayOrderId { get; set; } = string.Empty;
    public string? GatewayPaymentId { get; set; }
    public string? GatewaySignature { get; set; }
}

public class WalletRechargeRow
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public WalletRechargeStatus Status { get; set; }
    public string GatewayName { get; set; } = string.Empty;
    public string? GatewayOrderId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}

// Admin manual adjustment — credit or debit with a reason. Refunds are credit + Reason like "Refund: …".
public class AdjustWalletRequest
{
    public Guid FranchiseId { get; set; }
    public bool IsCredit { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>True when this credit is money the franchisee actually PAID us — the normal top-up.
    /// An invoice is raised in their name for it. Leave false for credits that aren't a sale, such
    /// as refunding a cancelled order or correcting a mistake: invoicing those would inflate
    /// revenue and charge GST on money that never came in. Ignored on debits.</summary>
    public bool MoneyReceived { get; set; } = true;

    /// <summary>How the money arrived — printed on the top-up invoice. Only used when
    /// <see cref="MoneyReceived"/> is true.</summary>
    public PaymentMode PaymentMode { get; set; } = PaymentMode.BankTransfer;
}
