using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

public record CouponAdminItem(
    Guid Id, string Code, string? Name, SharingType CouponType, decimal Value, decimal? MaxDiscount,
    decimal MinOrder, int? TotalLimit, int PerUserLimit, int TotalUsed,
    DateOnly? StartsAt, DateOnly? ExpiresAt, bool IsActive, bool IsExpired,
    Guid? AffiliateId, string? AffiliateName,
    // Which channel may redeem the code — website checkout, Franchise Portal, or both.
    bool IsCustomerApplicable = true, bool IsFranchiseApplicable = false);

public class CouponEditModel
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public SharingType CouponType { get; set; } = SharingType.Percentage;
    public decimal Value { get; set; }
    public decimal? MaxDiscount { get; set; }
    public decimal MinOrder { get; set; }
    public int? TotalLimit { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public DateOnly? StartsAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Optional — links the coupon to an affiliate (becomes affiliate-exclusive + auto-applying).</summary>
    public Guid? AffiliateId { get; set; }

    /// <summary>Customers may apply this code at the website checkout.</summary>
    public bool IsCustomerApplicable { get; set; } = true;

    /// <summary>Franchisees may apply this code on /franchise/order. The discount comes off the order
    /// total on top of — never instead of — the franchisee's share.</summary>
    public bool IsFranchiseApplicable { get; set; }
}

/// <summary>Affiliate options for the coupon "assign to affiliate" dropdown.</summary>
public record AffiliateOption(Guid Id, string Name, string Code);

public record CouponStats(int Total, int Active, int Expired, int TotalRedemptions);

// A row in the discount editor's "Usage history" — an order that used this coupon.
public record CouponUsageRow(Guid OrderId, string OrderNumber, decimal OrderTotal, DateTime CreatedAt);
