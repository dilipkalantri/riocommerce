using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface IAffiliateService
{
    Task<List<AffiliateAdminItem>> ListAsync();
    Task<AffiliateStats> StatsAsync();
    Task<AffiliateEditModel?> GetAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveAsync(AffiliateEditModel model);
    Task ToggleAsync(Guid id);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);

    Task<List<AffiliateReferralItem>> ReferralsAsync(Guid? affiliateId = null);
    Task MarkPaidAsync(Guid referralId);
}
