using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

public record AffiliateAdminItem(
    Guid Id, string Name, string Code, string? Email, string? Phone,
    SharingType CommissionType, decimal CommissionValue,
    int TotalReferrals, decimal TotalEarned, decimal TotalPaid, decimal Pending, bool IsActive);

public class AffiliateEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Code { get; set; } = string.Empty;   // blank on create → auto-generated
    public SharingType CommissionType { get; set; } = SharingType.Percentage;
    public decimal CommissionValue { get; set; }
    public bool IsActive { get; set; } = true;
}

public record AffiliateReferralItem(
    Guid Id, string AffiliateName, string AffiliateCode, string OrderNumber,
    decimal OrderAmount, decimal Commission, string? CouponCode, decimal Discount,
    bool IsPaid, DateTime CreatedAt);

public record AffiliateStats(int Affiliates, int Referrals, decimal TotalCommission, decimal PendingCommission);
