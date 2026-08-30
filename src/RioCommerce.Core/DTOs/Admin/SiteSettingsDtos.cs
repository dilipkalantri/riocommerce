namespace RioCommerce.Core.DTOs.Admin;

public class SiteSettings
{
    // ── Branding ──
    public string? CompanyName { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? CopyrightText { get; set; }
    public string? LogoUrl { get; set; }

    // ── Contact ──
    public string? ContactPhone1 { get; set; }
    public string? ContactPhone2 { get; set; }
    public string? ContactPhone3 { get; set; }
    public string? ContactEmail { get; set; }
    public string? SupportEmail { get; set; }
    public string? Address { get; set; }
    public string? GoogleMapsUrl { get; set; }
    public string? BusinessHours { get; set; }

    // ── Social & Messaging ──
    public string? WhatsappNumber { get; set; }
    public string? WhatsappMessage { get; set; }
    public string? InstagramUrl { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? TelegramUrl { get; set; }

    // ── Analytics ──
    public string? Ga4Id { get; set; }
    public string? GtmId { get; set; }
    public string? MetaPixelId { get; set; }

    // ── Widgets ──
    public string? AnnouncementText { get; set; }
    public bool FomoEnabled { get; set; } = true;

    // ── Feature Flags ──
    public bool FranchiseEnabled { get; set; } = true;
    public bool SerialKeysEnabled { get; set; } = true;

    // ── Theme ──
    public string? ThemePrimaryColor { get; set; }
    public string? ThemeAccentColor { get; set; }
    public string? ThemeFontFamily { get; set; }
}
