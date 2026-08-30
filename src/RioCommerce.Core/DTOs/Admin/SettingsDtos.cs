using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

// Strongly-typed finance configuration (persisted as finance.* keys in AppSettings).
public class FinanceSettings
{
    public decimal FranchiseCommissionPct { get; set; }                 // finance.franchise_commission_pct
    public decimal TdsPct { get; set; }                                 // finance.tds_pct
    public decimal SettlementApprovalThreshold { get; set; } = 50000m;  // finance.settlement_approval_threshold
    public decimal RefundApprovalThreshold { get; set; } = 5000m;       // finance.refund_approval_threshold
    public decimal DefaultGstPct { get; set; } = 18m;                   // finance.default_gst_pct
    public TaxMode TaxMode { get; set; } = TaxMode.Exclusive;           // finance.tax_mode
    public RoundingMode RoundingMode { get; set; } = RoundingMode.None; // finance.rounding_mode
}

// Strongly-typed payout configuration (persisted as payout.* keys in AppSettings).
public class PayoutSettings
{
    public bool AutoSettlementEnabled { get; set; }                     // payout.auto_settlement
    public PayoutFrequency Frequency { get; set; } = PayoutFrequency.Monthly; // payout.frequency
    public decimal MinimumPayoutAmount { get; set; }                    // payout.minimum_amount
    public int MaxRetries { get; set; } = 3;                            // payout.max_retries
    public int RetryIntervalHours { get; set; } = 24;                   // payout.retry_interval_hours
}

public record SettingHistoryDto(string Key, string? OldValue, string? NewValue, bool WasSecret, string ChangedByName, DateTime CreatedAt);
