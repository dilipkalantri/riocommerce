using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

public record ApprovalRequestDto(
    Guid Id, ApprovalType Type, ApprovalStatus Status, string Title, string? Description, decimal? Amount,
    string RelatedEntityType, Guid? RelatedEntityId, string RequestedByName, DateTime CreatedAt,
    string? DecidedByName, DateTime? DecidedAt, string? DecisionNotes);

public record ApprovalCommentDto(string AuthorName, string Body, DateTime CreatedAt);

public record ApprovalRequestDetailDto(ApprovalRequestDto Request, List<ApprovalCommentDto> Comments);
