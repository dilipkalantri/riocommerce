namespace RioCommerce.Core.Entities;

/// <summary>
/// Single-row global configuration for the homepage banner carousel — autoplay behaviour,
/// nav controls, and the admin-facing "recommended size" guidance shown on the upload form.
/// Mirrors the single-row pattern used by <c>FranchiseSettings</c>.
/// </summary>
public class BannerSliderSettings : BaseEntity
{
    public bool Autoplay { get; set; } = true;
    /// <summary>Slide advance interval in milliseconds (clamped sensibly in the service).</summary>
    public int AutoplayIntervalMs { get; set; } = 5000;
    public bool ShowArrows { get; set; } = true;
    public bool ShowDots { get; set; } = true;
    public bool PauseOnHover { get; set; } = true;

    // Recommended upload dimensions surfaced in the admin UI + used for client-side validation.
    public int RecommendedDesktopW { get; set; } = 1920;
    public int RecommendedDesktopH { get; set; } = 600;
    public int RecommendedMobileW { get; set; } = 768;
    public int RecommendedMobileH { get; set; } = 900;
}
