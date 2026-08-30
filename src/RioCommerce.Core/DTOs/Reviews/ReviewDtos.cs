using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Reviews;

public record ReviewItem(
    Guid Id, string AuthorName, int Rating, string? Title, string Comment,
    bool IsVerifiedPurchase, DateTime CreatedAt);

public class ReviewSummary
{
    public decimal AvgRating { get; set; }
    public int RatingCount { get; set; }
    /// <summary>Count per star — index 0 = 1★ … index 4 = 5★.</summary>
    public int[] StarCounts { get; set; } = new int[5];
}

public record MyReview(int Rating, string? Title, string Comment, ReviewStatus Status);

public class ProductReviews
{
    public ReviewSummary Summary { get; set; } = new();
    public List<ReviewItem> Items { get; set; } = new();
    public MyReview? Mine { get; set; }
}

public class SubmitReviewRequest
{
    public Guid ProductId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public record AdminReviewItem(
    Guid Id, string ProductTitle, string AuthorName, int Rating, string? Title, string Comment,
    bool IsVerifiedPurchase, ReviewStatus Status, DateTime CreatedAt);

public record ReviewStats(int Pending, int Approved, int Rejected);
