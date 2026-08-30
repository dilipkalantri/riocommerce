using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

// Typed finance/payout configuration façade over ISettingService. Domain code reads these models;
// the admin UI saves them. All persistence/caching/audit is delegated to ISettingService.
public interface IFinanceSettingsService
{
    Task<FinanceSettings> GetFinanceAsync();
    Task SaveFinanceAsync(FinanceSettings settings, Guid? actorId, string actorName);

    Task<PayoutSettings> GetPayoutAsync();
    Task SavePayoutAsync(PayoutSettings settings, Guid? actorId, string actorName);

    // Merged change history across finance.* and payout.* keys (most recent first).
    Task<List<SettingHistoryDto>> GetHistoryAsync(int take = 30);
}
