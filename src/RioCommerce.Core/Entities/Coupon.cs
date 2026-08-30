using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Coupon : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public SharingType CouponType { get; set; }
    public decimal Value { get; set; }
    public decimal? MaxDiscount { get; set; }
    public decimal MinOrder { get; set; }
    public int? TotalLimit { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public int TotalUsed { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>When set, the coupon is affiliate-exclusive: it only applies to orders referred by this affiliate.</summary>
    public Guid? AffiliateId { get; set; }

    // ── Audience ──────────────────────────────────────────────────────────────
    // Which order channel may redeem this coupon. Both default to the pre-existing behaviour:
    // every coupon works on the customer website, none work in the Franchise Portal until an
    // admin opts it in. Set both to run one code across both channels.

    /// <summary>Customers may apply this code at the website checkout.</summary>
    public bool IsCustomerApplicable { get; set; } = true;

    /// <summary>Franchisees may apply this code on /franchise/order. The discount comes off the
    /// order total on top of — never instead of — the franchisee's own share, which is calculated
    /// from product pricing and is unaffected by any coupon.</summary>
    public bool IsFranchiseApplicable { get; set; }
}
