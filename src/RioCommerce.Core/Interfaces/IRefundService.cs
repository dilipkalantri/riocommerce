using RioCommerce.Core.DTOs.Orders;
namespace RioCommerce.Core.Interfaces;

public interface IRefundService
{
    /// <summary>Refundable balance, per-item refundable quantities, and refund history for an order.</summary>
    Task<RefundSummaryDto> GetSummaryAsync(Guid orderId);

    /// <summary>
    /// Validates a refund (full/partial/item/offline). Small refunds process immediately; refunds above the
    /// configurable threshold are created as Pending and routed to the approval workflow.
    /// </summary>
    Task<(bool ok, string? error, Guid? refundId, bool pendingApproval)> CreateRefundAsync(Guid orderId, RefundRequest req, Guid? actorId, string actorName);

    /// <summary>Approves (processes) or rejects (cancels) a refund tied to an approval request.</summary>
    Task<(bool ok, string? error)> ResolveRefundApprovalAsync(Guid approvalRequestId, bool approve, string? notes, Guid? actorId, string actorName);
}
