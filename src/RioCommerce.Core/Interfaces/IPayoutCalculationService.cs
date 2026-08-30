using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

// Pure calculation engine — computes what each beneficiary is owed for a window from the order/refund/
// rule ledger. Stateless and side-effect free; SettlementService persists the results into batches.
public interface IPayoutCalculationService
{
    // Per-beneficiary payable for a window. <paramref name="excludeAlreadySettled"/> filters out earning/
    // clawback lines that already exist in a non-cancelled payout (prevents double payouts).
    Task<List<PayoutPreviewDto>> PreviewAsync(PayoutType type, DateTime fromUtc, DateTime toUtc, bool excludeAlreadySettled = true);

    // Total net still owed (not yet Paid/Cancelled) per beneficiary type — for dashboards/liabilities.
    Task<List<PayableSliceDto>> OutstandingPayableAsync();
}
