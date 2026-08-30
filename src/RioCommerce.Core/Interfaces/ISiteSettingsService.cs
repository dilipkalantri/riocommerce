using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface ISiteSettingsService
{
    Task<SiteSettings> GetAsync();
    Task SaveAsync(SiteSettings settings);
    /// <summary>Saves the uploaded logo to public file storage and persists its URL. Replaces any existing logo file.</summary>
    Task<(bool ok, string? error, string? url)> UploadLogoAsync(string extension, Stream content);
    Task DeleteLogoAsync();
}
