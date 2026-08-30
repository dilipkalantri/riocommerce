namespace RioCommerce.Core.DTOs.Banners;

/// <summary>Row in the admin banner list grid.</summary>
public class BannerAdminItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? DesktopImageUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? LinkUrl { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    /// <summary>True when active AND inside its scheduling window right now.</summary>
    public bool IsLive { get; set; }
}

/// <summary>Create / edit form model.</summary>
public class BannerEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DesktopImageUrl { get; set; }
    public string? MobileImageUrl { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ButtonText { get; set; }
    public string? LinkUrl { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>One renderable slide for the storefront carousel.</summary>
public record BannerSlide(
    Guid Id,
    string? Title,
    string? Description,
    string? ButtonText,
    string? LinkUrl,
    string DesktopImageUrl,
    string MobileImageUrl);

/// <summary>Everything the storefront component needs in one call: slides + global behaviour.</summary>
public record BannerSliderView(
    IReadOnlyList<BannerSlide> Slides,
    bool Autoplay,
    int AutoplayIntervalMs,
    bool ShowArrows,
    bool ShowDots,
    bool PauseOnHover);

/// <summary>Admin-editable global slider behaviour + recommended upload sizes.</summary>
public class BannerSliderSettingsModel
{
    public bool Autoplay { get; set; } = true;
    public int AutoplayIntervalMs { get; set; } = 5000;
    public bool ShowArrows { get; set; } = true;
    public bool ShowDots { get; set; } = true;
    public bool PauseOnHover { get; set; } = true;
    public int RecommendedDesktopW { get; set; } = 1920;
    public int RecommendedDesktopH { get; set; } = 600;
    public int RecommendedMobileW { get; set; } = 768;
    public int RecommendedMobileH { get; set; } = 900;
}
