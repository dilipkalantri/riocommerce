using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// A single beneficiary's payable for a period — the unit that gets grouped into a SettlementBatch.
// NetAmount = GrossAmount + Adjustments - RefundAdjustments - TaxDeduction (Adjustments may be negative).
public class Payout : BaseEntity
{
    public PayoutType BeneficiaryType { get; set; }
    public Guid BeneficiaryId { get; set; }
    public string BeneficiaryName { get; set; } = string.Empty;   // snapshot at generation time

    public decimal GrossAmount { get; set; }          // sum of positive earnings
    public decimal Adjustments { get; set; }          // manual deductions/bonuses (signed)
    public decimal RefundAdjustments { get; set; }    // refund clawbacks (stored positive, subtracted)
    public decimal TaxDeduction { get; set; }         // TDS / withholding (subtracted)
    public decimal NetAmount { get; set; }

    public PayoutStatus Status { get; set; } = PayoutStatus.Draft;
    public Guid? SettlementBatchId { get; set; }

    // The earning window this payout covers (CreatedAt on BaseEntity is the created-on timestamp).
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public DateTime? PaidOnUtc { get; set; }

    public string? PaymentReference { get; set; }     // bank UTR / provider payout id / manual ref
    public string? Provider { get; set; }             // "Manual" | "Razorpay" | "BankTransfer" | "UPI"
    public string? Notes { get; set; }

    public Guid? ApprovedById { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedOnUtc { get; set; }

    public SettlementBatch? SettlementBatch { get; set; }
    public ICollection<PayoutItem> Items { get; set; } = new List<PayoutItem>();
}

// One auditable line within a payout, traceable to the order/item/refund/rule it came from.
// Amount is signed: positive for earnings, negative for clawbacks/deductions.
public class PayoutItem : BaseEntity
{
    public Guid PayoutId { get; set; }
    public PayoutItemSource Source { get; set; } = PayoutItemSource.Earning;

    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }          // snapshot
    public Guid? OrderItemId { get; set; }
    public Guid? RefundId { get; set; }
    public Guid? SharingRuleId { get; set; }          // FacultySharingRule / commission source

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public Payout Payout { get; set; } = null!;
}
