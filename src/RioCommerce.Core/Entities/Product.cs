using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Product : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ShortDesc { get; set; }
    public string? FullDesc { get; set; }
    public CourseLevel Level { get; set; }
    public CourseType CourseType { get; set; } = CourseType.Regular;
    public Guid? CategoryId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid? PrimaryFacultyId { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal GstRate { get; set; } = 18.00m;
    public bool GstInclusive { get; set; } = true;
    public string SacCode { get; set; } = "999293";
    public string? Sku { get; set; }

    // ── Default franchise share (replaces the old FranchisePrice margin model) ──
    // When enabled, this is the commission applied to every franchise assigned to this product,
    // unless a per-franchise FranchiseCommission row overrides it. Percent is applied to the
    // EFFECTIVE selling price (special price when active, else regular).
    public bool EnableDefaultFranchiseShare { get; set; }
    public CommissionType DefaultFranchiseShareType { get; set; } = CommissionType.Percent;
    public decimal DefaultFranchiseShareValue { get; set; }

    // ── Default faculty share ──
    // The FALLBACK rate for this product's faculty: every faculty attached to the product who has
    // no explicit FacultySharingRule earns this. It is deliberately NOT a pool that gets divided —
    // each faculty earns the default independently, because faculty are paid for their own
    // contribution rather than splitting a fixed allowance. A product with three faculty on a 10%
    // default therefore pays out 30% of the base in total, which is why the calculator caps the
    // combined share at the taxable base and FacultySettings.MaxTotalSharePct is checked on save.
    public bool EnableDefaultFacultyShare { get; set; }
    public SharingType DefaultFacultyShareType { get; set; } = SharingType.Percentage;
    public decimal DefaultFacultyShareValue { get; set; }

    public DateOnly? BatchStartDate { get; set; }
    public string[]? ApplicableAttempts { get; set; }
    public string? TotalLectures { get; set; }
    public string? TotalHours { get; set; }
    public string? BooksInfo { get; set; }
    public string? ExamOrientedInfo { get; set; }
    public string? AdditionalDetails { get; set; }

    // ── Added catalogue fields (columns added via SQL: Views, Validity, Language, CourseSchedule) ──
    public string? Views { get; set; }
    public string? Validity { get; set; }
    public string? Language { get; set; }
    public string? CourseSchedule { get; set; }
    // ── nopCommerce-style catalogue fields ──
    public string? Gtin { get; set; }                  // global trade item number
    public string? Tags { get; set; }                  // comma-separated product tags
    public string? AdminComment { get; set; }          // internal note, never shown to customers
    public bool MarkAsNew { get; set; }
    public DateTime? AvailableStartUtc { get; set; }
    public DateTime? AvailableEndUtc { get; set; }
    public decimal ProductCost { get; set; }           // cost basis (for margin reporting)
    public bool AllowReviews { get; set; } = true;

    /// <summary>
    /// Whether customers may buy this product through the storefront (Add To Cart / Buy Now).
    ///
    /// Deliberately SEPARATE from <see cref="Status"/> and <see cref="IsFeatured"/>: a product can
    /// stay published, searchable and on the homepage while being unpurchasable. Defaults to true
    /// so nothing that exists today changes behaviour — the column is backfilled to true by
    /// 0044_product_allow_customer_purchase.sql.
    /// </summary>
    public bool AllowCustomerPurchase { get; set; } = true;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    // Dedicated landscape image used ONLY by homepage featured/trending and category listing cards.
    // When null, the storefront falls back to the primary gallery picture. Doesn't replace the
    // existing ProductImages system — product detail page, cart, checkout still use that.
    public string? HomeCardImageUrl { get; set; }
    // Offer / Profile picture used by combo recommendation cards (Cart, Checkout, "Students Also Added").
    // Fallback chain: OfferImageUrl → HomeCardImageUrl → PrimaryProductImage → illustration.
    public string? OfferImageUrl { get; set; }
    // Current batch availability (drives storefront badge + the OutOfStock add-to-cart guard).
    public BatchStatus BatchStatus { get; set; } = BatchStatus.Upcoming;
    // ── 📦 Estimated Delivery Information ──
    // Per-product delivery copy rendered on the Product Detail page, Cart line items, and the
    // Checkout review. Backwards-compatible defaults so existing rows look sensible until an
    // admin overrides them. EstimatedDeliveryMessage is optional and may carry multiple lines.
    public string LectureAccessTiming { get; set; } = "Within 24 Hours";
    public string NotesDispatchTimeline { get; set; } = "Within 48 Hours";
    public string? EstimatedDeliveryMessage { get; set; }
    // ── Course detail page enhancements ──
    public string? LecturesVideoUrl { get; set; }           // single YouTube URL — "About the Lectures" tab
    public string? BookPreviewPdfUrl { get; set; }          // PDF (e.g. Google Drive embed) — "Book Preview" tab
    public string? TestimonialVideoUrls { get; set; }       // newline-separated YouTube URLs — "Video Testimonials" tab
    public string? FaqsJson { get; set; }                   // JSON array of { q, a } — "FAQs" tab
    public string? RelatedProductSlugs { get; set; }        // comma-separated slugs — "Students Also Enrolled For"
    // ── Special Price (time-limited promotional price) ──
    public decimal? SpecialPrice { get; set; }
    public DateTime? SpecialPriceStartDateUtc { get; set; }
    public DateTime? SpecialPriceEndDateUtc { get; set; }

    /// <summary>True when a special price is set and the current UTC time falls within its window.</summary>
    public bool IsSpecialPriceActive =>
        SpecialPrice.HasValue && SpecialPrice.Value > 0
        && (!SpecialPriceStartDateUtc.HasValue || DateTime.UtcNow >= SpecialPriceStartDateUtc.Value)
        && (!SpecialPriceEndDateUtc.HasValue || DateTime.UtcNow <= SpecialPriceEndDateUtc.Value);

    /// <summary>The effective price — special price when active, otherwise regular selling price.
    /// Overrides mode prices too — use this everywhere a customer-facing price is needed.</summary>
    public decimal EffectiveSellingPrice => IsSpecialPriceActive ? SpecialPrice!.Value : SellingPrice;

    public string? Badge { get; set; }
    public bool IsFeatured { get; set; }
    public int DisplayOrder { get; set; }
    // Position in the homepage "Trending Courses" section (products with IsFeatured/Show-on-home-page).
    // Separate from DisplayOrder (global listing order) and from ProductCategory.DisplayOrder (per-category).
    public int HomePageDisplayOrder { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public int TotalOrders { get; set; }
    public int TotalViews { get; set; }
    public decimal AvgRating { get; set; }
    public int RatingCount { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Category? Category { get; set; }
    public Subject? Subject { get; set; }
    public Faculty? PrimaryFaculty { get; set; }
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductVideo> Videos { get; set; } = new List<ProductVideo>();
    public ICollection<ProductMode> Modes { get; set; } = new List<ProductMode>();
    public ICollection<ProductOptionGroup> OptionGroups { get; set; } = new List<ProductOptionGroup>();
    public ICollection<ProductInclusion> Inclusions { get; set; } = new List<ProductInclusion>();
    public ICollection<ProductFaculty> ProductFaculty { get; set; } = new List<ProductFaculty>();
    /// <summary>Every subject this product covers. <see cref="SubjectId"/> stays the primary one.</summary>
    public ICollection<ProductSubject> ProductSubject { get; set; } = new List<ProductSubject>();
    public ICollection<ProductBookPreview> BookPreviews { get; set; } = new List<ProductBookPreview>();
    public ICollection<ProductTestimonial> Testimonials { get; set; } = new List<ProductTestimonial>();
    public ICollection<FacultySharingRule> SharingRules { get; set; } = new List<FacultySharingRule>();
    public ICollection<ProductAttributeMapping> AttributeMappings { get; set; } = new List<ProductAttributeMapping>();
    public ICollection<ProductSpecificationAttribute> SpecificationAttributes { get; set; } = new List<ProductSpecificationAttribute>();
    public ICollection<ProductCategory> ProductCategories { get; set; } = new List<ProductCategory>();
    public ICollection<SpecialPriceAudit> SpecialPriceAudits { get; set; } = new List<SpecialPriceAudit>();
}
