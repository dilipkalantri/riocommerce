using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Recommendations;

/// <summary>Admin form row — one recommendation link on the source product's edit page.</summary>
public class ProductRecommendationItem
{
    public Guid Id { get; set; }                         // empty for unsaved rows
    public Guid RecommendedProductId { get; set; }
    public string RecommendedTitle { get; set; } = string.Empty;
    public string? RecommendedThumbUrl { get; set; }     // small preview shown in the admin selected list
    public string? RecommendedFacultyName { get; set; }
    public decimal? RecommendedPrice { get; set; }
    public int DisplayOrder { get; set; }
    public string? CustomTitle { get; set; }
    public RecommendationType Type { get; set; } = RecommendationType.StudentsAlsoAdded;
    public string? BadgeText { get; set; }
    public string? BadgeColor { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Result returned by the admin product-search autocomplete on the recommendations tab.</summary>
public record RecommendationSearchHit(
    Guid Id, string Title, string? Sku, string? FacultyName, string? CategoryName, string? ThumbUrl, decimal? Price);

/// <summary>Public-facing recommendation card shown on the storefront (Course Detail / Cart / Checkout).</summary>
public record RecommendationCard(
    Guid Id, string Slug, string Title, string? FacultyName, string? CategoryName, string? ImageUrl,
    decimal Price, decimal? Mrp, int DiscountPercent,
    double AvgRating, int RatingCount,
    string? BadgeText, string? BadgeColor, RecommendationType Type, string? CustomTitle,
    BatchStatus BatchStatus = BatchStatus.Upcoming)
{
    /// <summary>Display label — mirrors <c>ProductListItem.BatchStatusText</c> so cards on Course Detail,
    /// Cart and Checkout all show the SAME text from the SAME source as the rest of the storefront.</summary>
    public string BatchStatusText => BatchStatus switch
    {
        BatchStatus.Upcoming    => "Upcoming Batch",
        BatchStatus.Ongoing     => "Ongoing Batch",
        BatchStatus.PreRecorded => "Pre-Recorded",
        BatchStatus.ComingSoon  => "Coming Soon",
        BatchStatus.OutOfStock  => "Out Of Stock",
        _                       => BatchStatus.ToString()
    };
}
