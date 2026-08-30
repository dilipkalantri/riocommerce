using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Affiliate : BaseEntity
{
    public Guid? UserId { get; set; }              // optional link to a user account
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Code { get; set; } = string.Empty;   // unique referral code (used in /r/{code})
    public SharingType CommissionType { get; set; } = SharingType.Percentage;
    public decimal CommissionValue { get; set; }
    public bool IsActive { get; set; } = true;
    public int TotalReferrals { get; set; }
    public decimal TotalEarned { get; set; }
    public decimal TotalPaid { get; set; }
    public User? User { get; set; }
    public ICollection<AffiliateReferral> Referrals { get; set; } = new List<AffiliateReferral>();
}
