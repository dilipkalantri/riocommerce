using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

// Owns the settlement-batch lifecycle: generate → (approve) → pay, plus reconciliation, adjustments,
// export and the dashboard/liability rollups. Reuses IPayoutCalculationService for the maths and the
// approval engine for large/adjustment-heavy batches.
public interface ISettlementService
{
    // ── Generation ──
    Task<(bool ok, string? error, Guid? batchId)> GenerateBatchAsync(PayoutType type, DateTime fromUtc, DateTime toUtc, Guid? actorId, string actorName);

    // ── Reads ──
    Task<List<SettlementBatchListItem>> ListBatchesAsync(SettlementStatus? status = null, int take = 100);
    Task<SettlementBatchDetailDto?> GetBatchAsync(Guid batchId);
    Task<(IReadOnlyList<PayoutListItem> rows, int total)> ListPayoutsAsync(PayoutType? type, PayoutStatus? status, int page, int pageSize);
    Task<PayoutDashboardDto> GetDashboardAsync();

    // ── Lifecycle ──
    Task<(bool ok, string? error)> SubmitForApprovalAsync(Guid batchId, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> ApproveBatchAsync(Guid batchId, string? notes, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> RejectBatchAsync(Guid batchId, string? notes, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> ResolveBatchApprovalAsync(Guid approvalRequestId, bool approve, string? notes, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> MarkPaidAsync(Guid batchId, MarkPaidRequest req, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> RecalculateBatchAsync(Guid batchId, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> CancelBatchAsync(Guid batchId, string? notes, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> AddAdjustmentAsync(Guid batchId, SettlementAdjustmentRequest req, Guid? actorId, string actorName);

    // ── Export (bank-transfer / provider ready) ──
    Task<(string fileName, string csv)?> ExportBatchCsvAsync(Guid batchId);
}
