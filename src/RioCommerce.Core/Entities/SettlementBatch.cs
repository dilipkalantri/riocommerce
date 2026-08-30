using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// A reconcilable batch of payouts for one beneficiary type + period. Drives the approve → pay lifecycle
// and the bank/provider export. Totals are denormalised snapshots kept in sync on (re)calculation.
public class SettlementBatch : BaseEntity
{
    public string BatchNumber { get; set; } = string.Empty;   // e.g. STL-FAC-20260526-0001
    public PayoutType BeneficiaryType { get; set; }
    public SettlementStatus Status { get; set; } = SettlementStatus.Draft;

    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }

    public int BeneficiaryCount { get; set; }
    public decimal TotalGross { get; set; }
    public decimal TotalAdjustments { get; set; }
    public decimal TotalRefundAdjustments { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalNet { get; set; }

    public Guid? ApprovalRequestId { get; set; }
    public Guid? ApprovedById { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public DateTime? PaidOnUtc { get; set; }

    public string? Provider { get; set; }
    public string? PaymentReference { get; set; }
    public string? Notes { get; set; }

    public Guid? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "system";

    public ICollection<Payout> Payouts { get; set; } = new List<Payout>();
    public ICollection<SettlementAdjustment> Adjustments { get; set; } = new List<SettlementAdjustment>();
}

// A manual adjustment applied to a batch (or a specific payout within it). Signed amount.
public class SettlementAdjustment : BaseEntity
{
    public Guid SettlementBatchId { get; set; }
    public Guid? PayoutId { get; set; }               // optional — null = batch-wide adjustment
    public AdjustmentType Type { get; set; }
    public decimal Amount { get; set; }               // signed
    public string Reason { get; set; } = string.Empty;
    public Guid? CreatedById { get; set; }
    public string CreatedByName { get; set; } = "system";

    public SettlementBatch SettlementBatch { get; set; } = null!;
}
