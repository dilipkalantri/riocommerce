using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// Generic, reusable approval request — powers refund/payout/discount/override approvals.
// Single-step now (CurrentStep/TotalSteps), with ApprovalStep + ApprovalComment for multi-step/audit growth.
public class ApprovalRequest : BaseEntity
{
    public ApprovalType Type { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public string RelatedEntityType { get; set; } = string.Empty;   // e.g. "Refund"
    public Guid? RelatedEntityId { get; set; }
    public int CurrentStep { get; set; } = 1;
    public int TotalSteps { get; set; } = 1;
    public Guid? RequestedById { get; set; }
    public string RequestedByName { get; set; } = "system";
    public Guid? DecidedById { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNotes { get; set; }
    public ICollection<ApprovalStep> Steps { get; set; } = new List<ApprovalStep>();
    public ICollection<ApprovalComment> Comments { get; set; } = new List<ApprovalComment>();
}

public class ApprovalStep : BaseEntity
{
    public Guid ApprovalRequestId { get; set; }
    public int StepOrder { get; set; }
    public string? ApproverRole { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public ApprovalRequest ApprovalRequest { get; set; } = null!;
}

public class ApprovalComment : BaseEntity
{
    public Guid ApprovalRequestId { get; set; }
    public Guid? AuthorId { get; set; }
    public string AuthorName { get; set; } = "system";
    public string Body { get; set; } = string.Empty;
    public ApprovalRequest ApprovalRequest { get; set; } = null!;
}
