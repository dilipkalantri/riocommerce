using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Products;

public class ProductEditModel
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ShortDesc { get; set; }
    public string? FullDesc { get; set; }
    public CourseLevel Level { get; set; } = CourseLevel.Beginner;
    public CourseType CourseType { get; set; } = CourseType.Regular;
    public Guid? CategoryId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid? PrimaryFacultyId { get; set; }
    // ── Multi-select: a product can belong to multiple categories and faculties ──
    public List<Guid> CategoryIds { get; set; } = new();
    public List<Guid> FacultyIds { get; set; } = new();
    /// <summary>Every subject this course covers; the FIRST is the primary and is what
    /// <see cref="SubjectId"/> is set from. Same contract as <see cref="FacultyIds"/>.</summary>
    public List<Guid> SubjectIds { get; set; } = new();
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public bool EnableDefaultFranchiseShare { get; set; }
    public CommissionType DefaultFranchiseShareType { get; set; } = CommissionType.Percent;
    public decimal DefaultFranchiseShareValue { get; set; }
    // Fallback share for every faculty on this product who has no explicit rule. Saved with the
    // product (like the franchise default) so it works on a brand-new product; the per-faculty rules
    // grid is a separate section that needs a saved product to attach rows to.
    public bool EnableDefaultFacultyShare { get; set; }
    public SharingType DefaultFacultyShareType { get; set; } = SharingType.Percentage;
    public decimal DefaultFacultyShareValue { get; set; }
    public decimal GstRate { get; set; } = 18m;
    public bool GstInclusive { get; set; } = true;
    public string? TotalLectures { get; set; }
    public string? TotalHours { get; set; }
    public string? BooksInfo { get; set; }
    public string? AdditionalDetails { get; set; }
    public string? Views { get; set; }
    public string? Validity { get; set; }
    public string? Language { get; set; }
    public string? CourseSchedule { get; set; }
    public string? ApplicableAttemptsCsv { get; set; }   // comma-separated for the form
    // ── nopCommerce-style catalogue fields ──
    public string? Sku { get; set; }
    public string? Gtin { get; set; }
    public string? Tags { get; set; }
    public string? AdminComment { get; set; }
    public bool MarkAsNew { get; set; }
    public DateTime? AvailableStartUtc { get; set; }
    public DateTime? AvailableEndUtc { get; set; }
    public decimal ProductCost { get; set; }
    public bool AllowReviews { get; set; } = true;
    /// <summary>Offer Add To Cart / Buy Now on the storefront. Independent of Published.</summary>
    public bool AllowCustomerPurchase { get; set; } = true;
    // ── Special Price (time-limited promotional price) ──
    public decimal? SpecialPrice { get; set; }
    public DateTime? SpecialPriceStartDateUtc { get; set; }
    public DateTime? SpecialPriceEndDateUtc { get; set; }
    public string? SpecialPriceRemarks { get; set; }   // optional audit remark (not persisted on Product)
    public string? Badge { get; set; }
    public bool IsFeatured { get; set; }              // "Show on home page"
    public int DisplayOrder { get; set; }
    public int HomePageDisplayOrder { get; set; }     // position in homepage Trending Courses (lower = first)
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public BatchStatus BatchStatus { get; set; } = BatchStatus.Upcoming;
    // ── 📦 Estimated Delivery Information (Required: Timing+Timeline ≤100ch · Optional message ≤500ch) ──
    public string LectureAccessTiming { get; set; } = "Within 24 Hours";
    public string NotesDispatchTimeline { get; set; } = "Within 48 Hours";
    public string? EstimatedDeliveryMessage { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    // Home/category-card image (separate from the gallery — see Product.HomeCardImageUrl).
    public string? HomeCardImageUrl { get; set; }
    // Offer/Profile image — used by combo recommendation cards across Cart/Checkout/Student also added sections.
    public string? OfferImageUrl { get; set; }
    // ── Course detail page enhancements ──
    public string? LecturesVideoUrl { get; set; }
    public string? BookPreviewPdfUrl { get; set; }
    public string? TestimonialVideoUrls { get; set; }
    public string? RelatedProductSlugs { get; set; }
    public List<FaqEdit> Faqs { get; set; } = new();
    public List<ProductModeEdit> Modes { get; set; } = new();
    public List<ProductOptionGroupEdit> OptionGroups { get; set; } = new();
    public List<ProductInclusionEdit> Inclusions { get; set; } = new();
    public List<ProductAttributeMappingEdit> AttributeMappings { get; set; } = new();
    public List<ProductSpecEdit> SpecificationAttributes { get; set; } = new();
    public List<ProductImageEdit> Images { get; set; } = new();
    /// <summary>Student testimonials managed via the new entity-based section.</summary>
    public List<ProductTestimonialEdit> Testimonials { get; set; } = new();
}

// One product picture as shown in the editor's Pictures grid.
public record ProductImageEdit(Guid Id, string ImageUrl, string? AltText, string? Title, int DisplayOrder, bool IsPrimary);

// One product demo video (YouTube) as shown in the editor, after the Pictures section.
public record ProductVideoEdit(Guid Id, string YoutubeUrl, string? Title, int DisplayOrder);

// A row in the "Purchased with orders" panel.
public record PurchasedOrderRow(Guid OrderId, string OrderNumber, string? Email, string OrderStatus, string PaymentStatus, DateTime CreatedAt);

public class ProductAttributeMappingEdit
{
    public Guid? Id { get; set; }
    public Guid ProductAttributeId { get; set; }
    public string? TextPrompt { get; set; }
    public bool IsRequired { get; set; }
    public AttributeControlType ControlType { get; set; } = AttributeControlType.DropdownList;
    public int DisplayOrder { get; set; }
    public List<ProductAttributeValueEdit> Values { get; set; } = new();
}

public class ProductAttributeValueEdit
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAdjustment { get; set; }
    public bool PriceAdjustmentUsePercentage { get; set; }
    public bool IsPreSelected { get; set; }
    public int DisplayOrder { get; set; }
}

public class ProductSpecEdit
{
    public Guid? Id { get; set; }
    public Guid SpecificationAttributeId { get; set; }   // UI helper to drive the option dropdown
    public Guid SpecificationAttributeOptionId { get; set; }
    public bool AllowFiltering { get; set; }
    public bool ShowOnProductPage { get; set; } = true;
    public int DisplayOrder { get; set; }
}

// Pickers that populate the dropdowns on the product editor's Attributes tab.
public record ProductAttributeMeta(List<AttrPick> Attributes, List<SpecPick> SpecAttributes);
public record AttrPick(Guid Id, string Name, List<PredefinedPick> PredefinedValues);
public record PredefinedPick(string Name, decimal PriceAdjustment, bool PriceAdjustmentUsePercentage);
public record SpecPick(Guid Id, string Name, List<SpecOptionPick> Options);
public record SpecOptionPick(Guid Id, string Name);

public class ProductModeEdit
{
    /// <summary>The persisted <see cref="RioCommerce.Core.Entities.ProductMode"/> id. Empty for a
    /// not-yet-saved mode row. The serial-key config's mode whitelist keys on this so two modes that
    /// share a <see cref="LectureMode"/> enum (e.g. Softcopy vs Hardcopy) are selectable independently.</summary>
    public Guid Id { get; set; }
    public string ModeName { get; set; } = string.Empty;
    public LectureMode ModeType { get; set; } = LectureMode.LivePlusRecorded;
    public decimal Price { get; set; }
    public bool IsEnabled { get; set; } = true;
}

// Configurable purchase-option group (e.g. "Books", "Test Series") shown in the product editor.
public class ProductOptionGroupEdit
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ProductOptionItemEdit> Items { get; set; } = new();
}

public class ProductOptionItemEdit
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceAddOn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductInclusionEdit
{
    public string? Icon { get; set; }
    public string Title { get; set; } = string.Empty;
}

public class FaqEdit
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public record AdminProductStats(int Total, int Active, int Foundation, int Intermediate);

/// <summary>One row in the Special Price Audit history panel on the product editor.</summary>
public record SpecialPriceAuditItem(
    Guid Id, decimal? OldPrice, decimal? NewPrice,
    DateTime? OldStartDate, DateTime? NewStartDate,
    DateTime? OldEndDate, DateTime? NewEndDate,
    string ModifiedByName, DateTime ModifiedAt, string? Remarks);
