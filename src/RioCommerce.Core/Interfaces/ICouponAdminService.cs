using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface ICouponAdminService
{
    Task<List<CouponAdminItem>> ListAsync();
    Task<CouponStats> StatsAsync();
    Task<List<AffiliateOption>> AffiliateOptionsAsync();
    Task<CouponEditModel?> GetAsync(Guid id);
    Task<List<CouponUsageRow>> UsageHistoryAsync(Guid couponId);
    Task<(bool ok, string? error, Guid id)> SaveAsync(CouponEditModel model);
    Task ToggleAsync(Guid id);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
}
