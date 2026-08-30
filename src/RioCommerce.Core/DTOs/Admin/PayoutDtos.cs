using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

// ── Calculation preview (not yet persisted) ──
public record PayoutLineDto(
    PayoutItemSource Source, Guid? OrderId, string? OrderNumber, Guid? OrderItemId,
    Guid? RefundId, Guid? SharingRuleId, string Description, decimal Amount);

public record PayoutPreviewDto(
    PayoutType BeneficiaryType, Guid BeneficiaryId, string BeneficiaryName,
    int OrderCount, decimal Gross, decimal RefundClawback, decimal TaxDeduction, decimal Net,
    List<PayoutLineDto> Lines);

// ── Grid / list projections ──
public record PayoutListItem(
    Guid Id, PayoutType BeneficiaryType, string BeneficiaryName, decimal GrossAmount,
    decimal Adjustments, decimal RefundAdjustments, decimal TaxDeduction, decimal NetAmount,
    PayoutStatus Status, Guid? SettlementBatchId, string? BatchNumber,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime CreatedAt, string? PaymentReference);

public record SettlementBatchListItem(
    Guid Id, string BatchNumber, PayoutType BeneficiaryType, SettlementStatus Status,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, int BeneficiaryCount,
    decimal TotalGross, decimal TotalNet, Guid? ApprovalRequestId, string CreatedByName, DateTime CreatedAt);

public record SettlementBatchDetailDto(SettlementBatchListItem Batch, List<PayoutListItem> Payouts, List<SettlementAdjustmentDto> Adjustments);

public record SettlementAdjustmentDto(Guid Id, Guid? PayoutId, AdjustmentType Type, decimal Amount, string Reason, string CreatedByName, DateTime CreatedAt);

// ── Dashboard / reporting ──
public record PayableSliceDto(PayoutType BeneficiaryType, int Count, decimal Amount);

public record PayoutDashboardDto(
    int PendingPayoutsCount, decimal PendingPayoutsAmount,
    int UpcomingSettlements, decimal OutstandingLiabilities, decimal PaidLiabilities,
    decimal SettlementSuccessRate, List<PayableSliceDto> Payable);

// ── Requests ──
public record GenerateBatchRequest(PayoutType BeneficiaryType, DateTime FromUtc, DateTime ToUtc);
public record MarkPaidRequest(string? Provider, string? PaymentReference, string? Notes);
public record SettlementAdjustmentRequest(Guid? PayoutId, AdjustmentType Type, decimal Amount, string Reason);
