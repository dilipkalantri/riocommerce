using RioCommerce.Core.DTOs.Banners;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Banner / homepage-slider management. Admin CRUD + image upload + ordering live here, alongside
/// the storefront read path (<see cref="GetSliderAsync"/>) which returns only the banners that are
/// active AND inside their scheduling window, ordered for display.
/// </summary>
public interface IBannerService
{
    // ── Admin: list & edit ──────────────────────────────────────────────────
    Task<List<BannerAdminItem>> ListAsync(string placement = "homepage");
    Task<BannerEditModel?> GetAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveAsync(BannerEditModel model);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
    Task ToggleAsync(Guid id);

    /// <summary>Persist a new display order (ids in the desired order).</summary>
    Task ReorderAsync(IReadOnlyList<Guid> orderedIds);

    // ── Admin: image upload (mirrors SiteSettingsService.UploadLogoAsync) ──────
    /// <summary>device = "desktop" | "mobile". Returns the public URL on success.</summary>
    Task<(bool ok, string? error, string? url)> UploadImageAsync(string device, string extension, Stream content);

    // ── Admin: global slider settings ─────────────────────────────────────────
    Task<BannerSliderSettingsModel> GetSettingsAsync();
    Task SaveSettingsAsync(BannerSliderSettingsModel model);

    // ── Storefront ─────────────────────────────────────────────────────────────
    Task<BannerSliderView> GetSliderAsync(string placement = "homepage");
}
