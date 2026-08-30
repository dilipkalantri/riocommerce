using RioCommerce.Core.DTOs.Referral;
namespace RioCommerce.Core.Interfaces;

public interface IReferralAdminService
{
    Task<List<ReferralSourceAdminItem>> ListAsync();
    Task<ReferralSourceEditModel?> GetAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveAsync(ReferralSourceEditModel model, Guid? actorId);
    Task ToggleAsync(Guid id);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
}

public interface IReferralService
{
    /// <summary>Active referral options for the storefront checkout dropdown — ordered by DisplayOrder.</summary>
    Task<List<ReferralSourceOption>> ListActiveAsync();
}
