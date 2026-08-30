using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Payment : BaseEntity
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMode PaymentMode { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? GatewayName { get; set; }
    public string? GatewayOrderId { get; set; }
    public string? GatewayPaymentId { get; set; }
    public string? GatewaySignature { get; set; }

    /// <summary>Normalised instrument reported by the gateway for THIS attempt — "UPI",
    /// "Credit Card", "Net Banking", … See <see cref="Order.GatewayPaymentMode"/>.</summary>
    public string? GatewayPaymentMode { get; set; }

    /// <summary>Extra instrument context for ops/reconciliation: issuing bank, wallet name,
    /// card network, masked VPA. Free text straight from the gateway — display only.</summary>
    public string? GatewayPaymentModeDetail { get; set; }

    public string? BankRef { get; set; }
    public DateTime? PaidAt { get; set; }
    public Order Order { get; set; } = null!;
}
