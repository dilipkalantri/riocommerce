namespace RioCommerce.Core.Enums;

// Who a payout is owed to. Doubles as the "BeneficiaryType" classification.
public enum PayoutType { Faculty, Franchise, Affiliate, Vendor }

// Lifecycle of an individual payout (mirrors the batch it belongs to once batched).
public enum PayoutStatus { Draft, PendingApproval, Approved, Processing, Paid, Failed, Cancelled }

// Lifecycle of a settlement batch.
public enum SettlementStatus { Draft, PendingApproval, Approved, Processing, Paid, Failed, Cancelled }

// What a settlement-level adjustment represents (signed amount carries the direction).
public enum AdjustmentType { RefundClawback, ManualDeduction, Bonus, TaxDeduction, Correction }

// What a single payout line is derived from — keeps the ledger auditable back to its source.
public enum PayoutItemSource { Earning, RefundClawback, Adjustment }
