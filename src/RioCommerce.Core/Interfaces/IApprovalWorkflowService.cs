using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

// Generic approval lifecycle store. Domain services (e.g. RefundService) create requests and act on the
// outcome; this service stays domain-agnostic so it can power payouts/discounts/overrides later.
public interface IApprovalWorkflowService
{
    Task<Guid> CreateRequestAsync(ApprovalType type, string title, string? description, decimal? amount,
        string relatedEntityType, Guid? relatedEntityId, Guid? actorId, string actorName);

    Task<(bool ok, string? error)> ApproveAsync(Guid requestId, string? notes, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> RejectAsync(Guid requestId, string? notes, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> RequestChangesAsync(Guid requestId, string notes, Guid? actorId, string actorName);
    Task AddCommentAsync(Guid requestId, string body, Guid? actorId, string actorName);

    Task<List<ApprovalRequestDto>> ListAsync(ApprovalStatus? status = null, int take = 50);
    Task<ApprovalRequestDetailDto?> GetAsync(Guid requestId);
    Task<ApprovalRequestDto?> GetForEntityAsync(string relatedEntityType, Guid relatedEntityId);
    Task<int> PendingCountAsync();
}
