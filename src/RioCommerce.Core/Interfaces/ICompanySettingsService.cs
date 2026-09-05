using RioCommerce.Core.DTOs.Invoices;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Typed company / invoice-issuer configuration over the existing AppSettings store.
///
/// Centralises the eleven <c>company.*</c> keys so the admin screen and the invoice renderer
/// read and write the SAME strings — a typo in one place can no longer silently blank a field
/// on the tax invoice. No new table and no second settings mechanism: every value goes through
/// <see cref="ISettingService"/> exactly like finance.*, payout.* and the gateway keys.
/// </summary>
public interface ICompanySettingsService
{
    /// <summary>Current issuer profile. Missing keys come back null, never invented.</summary>
    Task<CompanyProfile> GetAsync();

    /// <summary>
    /// Writes the eleven company.* keys in one batched, audited operation. Keys this screen does
    /// not own (company.website, company.logo_url) are left untouched.
    /// </summary>
    Task SaveAsync(CompanyProfile profile, Guid? actorId, string actorName);
}
