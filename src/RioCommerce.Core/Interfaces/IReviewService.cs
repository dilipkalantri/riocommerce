using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Reviews;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

public interface IReviewService
{
    /// <summary>Approved reviews + aggregate for a product, plus the current user's own review (any status) if signed in.</summary>
    Task<ProductReviews> GetForProductAsync(Guid productId, Guid? currentUserId);

    /// <summary>Creates or updates the user's review for a product; resets it to Pending for moderation.</summary>
    Task<(bool ok, string? error)> SubmitAsync(Guid userId, SubmitReviewRequest request);

    Task<PagedResult<AdminReviewItem>> AdminListAsync(ReviewStatus? status, int page, int pageSize);
    Task<ReviewStats> AdminStatsAsync();

    /// <summary>Approves or rejects a review and recomputes the product's average rating.</summary>
    Task ModerateAsync(Guid reviewId, bool approve);
}
