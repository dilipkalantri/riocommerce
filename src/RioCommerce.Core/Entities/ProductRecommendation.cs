using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

/// <summary>
/// An admin-curated "Offer / Combo Recommendation" link from one product to another. Each row carries
/// its own display order, optional badge, and recommendation type so a single source product can have
/// multiple themed recommendation rows. Storefront pages (Course Detail, Cart, Checkout) fan-out
/// from this table to render "Students Also Added" / "Frequently Bought Together" sections.
/// </summary>
public class ProductRecommendation : BaseEntity
{
    public Guid ProductId { get; set; }                  // source — page this recommendation will appear on
    public Guid RecommendedProductId { get; set; }       // target — the product being suggested
    public int DisplayOrder { get; set; }
    public string? CustomTitle { get; set; }             // optional override of the section heading
    public RecommendationType Type { get; set; } = RecommendationType.StudentsAlsoAdded;
    public string? BadgeText { get; set; }               // e.g. "Most Popular", "Best Combo", "Save More"
    public string? BadgeColor { get; set; }              // e.g. "#16A34A" or a CSS color name
    public int Priority { get; set; }                    // higher = shown earlier; ties broken by DisplayOrder
    public bool IsActive { get; set; } = true;
    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public Product? Product { get; set; }
    public Product? RecommendedProduct { get; set; }
}
