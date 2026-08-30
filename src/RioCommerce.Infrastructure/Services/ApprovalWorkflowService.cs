using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ApprovalWorkflowService : IApprovalWorkflowService
{
    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationCenterService _notify;
    private readonly IRealtimeBus _bus;

    public ApprovalWorkflowService(RioCommerceDbContext db, IAuditService audit, INotificationCenterService notify, IRealtimeBus bus)
    {
        _db = db; _audit = audit; _notify = notify; _bus = bus;
    }

    public async Task<Guid> CreateRequestAsync(ApprovalType type, string title, string? description, decimal? amount,
        string relatedEntityType, Guid? relatedEntityId, Guid? actorId, string actorName)
    {
        var req = new ApprovalRequest
        {
            Type = type, Status = ApprovalStatus.Pending, Title = title, Description = description, Amount = amount,
            RelatedEntityType = relatedEntityType, RelatedEntityId = relatedEntityId,
            RequestedById = actorId, RequestedByName = actorName
        };
        _db.ApprovalRequests.Add(req);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "ApprovalRequested", "ApprovalRequest", req.Id.ToString(),
            JsonSerializer.Serialize(new { Type = type.ToString(), title, amount }));
        await _notify.NotifyAsync(AdminNotificationType.SystemAlert, NotificationSeverity.Warning,
            "Approval required", title, "/admin/approvals", req.Id.ToString());
        _bus.PublishDataChanged(new RealtimeEvent("approvals"));
        return req.Id;
    }

    public Task<(bool ok, string? error)> ApproveAsync(Guid requestId, string? notes, Guid? actorId, string actorName)
        => DecideAsync(requestId, ApprovalStatus.Approved, "ApprovalApproved", notes, actorId, actorName);

    public Task<(bool ok, string? error)> RejectAsync(Guid requestId, string? notes, Guid? actorId, string actorName)
        => DecideAsync(requestId, ApprovalStatus.Rejected, "ApprovalRejected", notes, actorId, actorName);

    public Task<(bool ok, string? error)> RequestChangesAsync(Guid requestId, string notes, Guid? actorId, string actorName)
        => DecideAsync(requestId, ApprovalStatus.ChangesRequested, "ApprovalChangesRequested", notes, actorId, actorName);

    private async Task<(bool ok, string? error)> DecideAsync(Guid requestId, ApprovalStatus status, string action, string? notes, Guid? actorId, string actorName)
    {
        var req = await _db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return (false, "Approval request not found.");
        if (req.Status != ApprovalStatus.Pending) return (false, $"This request is already {req.Status}.");

        req.Status = status;
        req.DecidedById = actorId;
        req.DecidedByName = actorName;
        req.DecidedAt = DateTime.UtcNow;
        req.DecisionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, action, "ApprovalRequest", req.Id.ToString(),
            JsonSerializer.Serialize(new { req.Title, status = status.ToString(), notes }));
        _bus.PublishDataChanged(new RealtimeEvent("approvals"));
        return (true, null);
    }

    public async Task AddCommentAsync(Guid requestId, string body, Guid? actorId, string actorName)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        _db.ApprovalComments.Add(new ApprovalComment { ApprovalRequestId = requestId, Body = body.Trim(), AuthorId = actorId, AuthorName = actorName });
        await _db.SaveChangesAsync();
        _bus.PublishDataChanged(new RealtimeEvent("approvals"));
    }

    public async Task<List<ApprovalRequestDto>> ListAsync(ApprovalStatus? status = null, int take = 50)
    {
        var q = _db.ApprovalRequests.AsQueryable();
        if (status.HasValue) q = q.Where(r => r.Status == status);
        return await q.OrderByDescending(r => r.CreatedAt).Take(take).Select(Project).ToListAsync();
    }

    public async Task<ApprovalRequestDto?> GetForEntityAsync(string relatedEntityType, Guid relatedEntityId) =>
        await _db.ApprovalRequests.Where(r => r.RelatedEntityType == relatedEntityType && r.RelatedEntityId == relatedEntityId)
            .OrderByDescending(r => r.CreatedAt).Select(Project).FirstOrDefaultAsync();

    public async Task<ApprovalRequestDetailDto?> GetAsync(Guid requestId)
    {
        var req = await _db.ApprovalRequests.Include(r => r.Comments).FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null) return null;
        var dto = new ApprovalRequestDto(req.Id, req.Type, req.Status, req.Title, req.Description, req.Amount,
            req.RelatedEntityType, req.RelatedEntityId, req.RequestedByName, req.CreatedAt, req.DecidedByName, req.DecidedAt, req.DecisionNotes);
        var comments = req.Comments.OrderBy(c => c.CreatedAt).Select(c => new ApprovalCommentDto(c.AuthorName, c.Body, c.CreatedAt)).ToList();
        return new ApprovalRequestDetailDto(dto, comments);
    }

    public Task<int> PendingCountAsync() => _db.ApprovalRequests.CountAsync(r => r.Status == ApprovalStatus.Pending);

    private static readonly System.Linq.Expressions.Expression<Func<ApprovalRequest, ApprovalRequestDto>> Project =
        r => new ApprovalRequestDto(r.Id, r.Type, r.Status, r.Title, r.Description, r.Amount,
            r.RelatedEntityType, r.RelatedEntityId, r.RequestedByName, r.CreatedAt, r.DecidedByName, r.DecidedAt, r.DecisionNotes);
}
