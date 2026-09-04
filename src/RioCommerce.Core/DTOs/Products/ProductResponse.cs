using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Products;
public class ProductListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public CourseLevel Level { get; set; }
    public CourseType CourseType { get; set; }
    public string? FacultyName { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    // ── Special Price (time-limited promo) ──
    public decimal? SpecialPrice { get; set; }
    public DateTime? SpecialPriceStartDateUtc { get; set; }
    public DateTime? SpecialPriceEndDateUtc { get; set; }
    // ── School Student Price ──
    /// <summary>The tier price registered school students pay. Null / 0 = no school tier on this course.</summary>
    public decimal? SchoolStudentPrice { get; set; }
    /// <summary>
    /// Stamped by the API when the caller is a school-linked user (student on a roll or active
    /// staff). Drives both the display price and the "School student price" badge on the card.
    /// </summary>
    public bool SchoolPriceApplied { get; set; }
    /// <summary>True when a special price is configured and the current UTC time falls within its window.</summary>
    public bool IsSpecialPriceActive =>
        SpecialPrice.HasValue && SpecialPrice.Value > 0
        && (!SpecialPriceStartDateUtc.HasValue || DateTime.UtcNow >= SpecialPriceStartDateUtc.Value)
        && (!SpecialPriceEndDateUtc.HasValue || DateTime.UtcNow <= SpecialPriceEndDateUtc.Value);
    /// <summary>The price to display on the storefront — school tier when the caller qualifies and
    /// it is lower; else the special price when active; else the regular selling price.</summary>
    public decimal EffectivePrice
    {
        get
        {
            var regular = IsSpecialPriceActive ? SpecialPrice!.Value : SellingPrice;
            return (SchoolPriceApplied && SchoolStudentPrice.HasValue && SchoolStudentPrice.Value > 0)
                ? System.Math.Min(regular, SchoolStudentPrice.Value)
                : regular;
        }
    }
    public string? Badge { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public string? HomeCardImageUrl { get; set; }   // when present, storefront cards prefer this over PrimaryImageUrl
    public int TotalOrders { get; set; }
    public decimal AvgRating { get; set; }
    public int RatingCount { get; set; }
    public ProductStatus Status { get; set; }
    // Admin product-list conveniences: whether the product shows on the homepage and its homepage order.
    public bool IsFeatured { get; set; }
    public int HomePageDisplayOrder { get; set; }
    /// <summary>Current batch availability — drives the storefront badge on every card.</summary>
    /// <summary>False hides Add To Cart / Buy Now. Defaults true so any projection that
    /// does not set it keeps today's purchasable behaviour.</summary>
    public bool AllowCustomerPurchase { get; set; } = true;

    public BatchStatus BatchStatus { get; set; } = BatchStatus.Upcoming;
    /// <summary>Display label for <see cref="BatchStatus"/>. Computed so every consumer (API JSON
    /// payloads, Razor pages, cards, recommendation rows) reads the SAME text from the SAME source.
    /// Do not hardcode the label anywhere else.</summary>
    public string BatchStatusText => BatchStatus switch
    {
        BatchStatus.Upcoming    => "Upcoming Batch",
        BatchStatus.Ongoing     => "Ongoing Batch",
        BatchStatus.PreRecorded => "Pre-Recorded",
        BatchStatus.ComingSoon  => "Coming Soon",
        BatchStatus.OutOfStock  => "Out Of Stock",
        _                       => BatchStatus.ToString()
    };
    public List<string> InclusionTitles { get; set; } = new();
}
public class ProductDetailResponse : ProductListItem
{
    public string? ShortDesc { get; set; }
    public string? FullDesc { get; set; }
    public string? TotalLectures { get; set; }
    public string? TotalHours { get; set; }
    public string? BooksInfo { get; set; }
    public string? AdditionalDetails { get; set; }
    public string? Views { get; set; }
    public string? Validity { get; set; }
    public string? Language { get; set; }
    public string? CourseSchedule { get; set; }
    public string[]? ApplicableAttempts { get; set; }
    public DateOnly? BatchStartDate { get; set; }
    public string? FacultyBio { get; set; }
    /// <summary>Premium reel-card testimonials — pre-parsed video ids so the UI doesn't reparse on render.</summary>
    public List<ProductTestimonialPublic> Testimonials { get; set; } = new();
    // ── Faculty card on the Course detail page links straight to /faculty/{Code}
    //    so visitors can dig into the full profile. FacultyShortDescription is the
    //    one-liner shown on the card; when empty, the UI falls back to the first
    //    ~250 chars of FacultyBio (see FacultyShortDescription.Resolve). ──
    public string? FacultyCode { get; set; }
    public string? FacultyPhotoUrl { get; set; }
    public string? FacultyDesignation { get; set; }
    public string? FacultyShortDescription { get; set; }
    public string? FacultyYearsOfExperience { get; set; }
    public string? FacultyStudentsTaught { get; set; }
    public decimal GstRate { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    // ── Course detail enhancements ──
    public string? LecturesVideoUrl { get; set; }            // "About the Lectures" tab
    public string? BookPreviewPdfUrl { get; set; }           // "Book Preview" tab
    public List<string> TestimonialVideoUrls { get; set; } = new();  // "Video Testimonials" tab
    public List<FaqItem> Faqs { get; set; } = new();         // "FAQs" tab
    public List<ProductListItem> RelatedProducts { get; set; } = new();  // "Students Also Enrolled For"
    public List<ProductModeDto> Modes { get; set; } = new();
    public List<ProductOptionGroupDto> OptionGroups { get; set; } = new();
    public List<ProductInclusionDto> Inclusions { get; set; } = new();
    public List<ProductImageDto> Images { get; set; } = new();
    public List<ProductVideoDto> Videos { get; set; } = new();
    public List<ProductAttributeDto> Attributes { get; set; } = new();
    public List<ProductSpecDto> Specifications { get; set; } = new();
    // ── 📦 Estimated Delivery Information — live values; powers the Detail-page card. ──
    public string LectureAccessTiming { get; set; } = "Within 24 Hours";
    public string NotesDispatchTimeline { get; set; } = "Within 48 Hours";
    public string? EstimatedDeliveryMessage { get; set; }
}

public record FaqItem(string Question, string Answer);

public record ProductAttributeDto(
    Guid MappingId, string AttributeName, string ControlType, string? TextPrompt, bool IsRequired,
    List<ProductAttributeValueDto> Values);
public record ProductAttributeValueDto(
    Guid Id, string Name, decimal PriceAdjustment, bool PriceAdjustmentUsePercentage, bool IsPreSelected);
public record ProductSpecDto(string? GroupName, string AttributeName, string OptionName, string? ColorSquaresRgb);
public record ProductSuggestion(Guid Id, string Title, string Slug, CourseLevel Level, string? FacultyName);
public record ProductModeDto(Guid Id, string ModeName, string ModeType, decimal Price, bool IsEnabled);
public record ProductOptionGroupDto(Guid Id, string Name, List<ProductOptionItemDto> Options);
public record ProductOptionItemDto(Guid Id, string Name, decimal PriceAddOn);
public record ProductInclusionDto(string? Icon, string Title);
public record ProductImageDto(string ImageUrl, string? AltText, bool IsPrimary);
public record ProductVideoDto(string YoutubeUrl, string? Title);
public class ProductFilterRequest
{
    public CourseLevel? Level { get; set; }
    public CourseType? CourseType { get; set; }
    public Guid? CategoryId { get; set; }
    public bool SearchSubcategories { get; set; }   // include descendant categories of CategoryId
    public Guid? FacultyId { get; set; }
    public Guid? SubjectId { get; set; }
    public ProductStatus? Status { get; set; }
    public BatchStatus? BatchStatus { get; set; }   // admin grid filter + storefront chip filter (when wired)
    public string? Search { get; set; }
    public List<Guid> SpecOptionIds { get; set; } = new();   // storefront spec facet filter
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// Dropdown data for the admin product search panel (categories are flattened with indentation).
public record ProductFilterMeta(List<IdName> Categories, List<IdName> Subjects, List<IdName> Faculties);
