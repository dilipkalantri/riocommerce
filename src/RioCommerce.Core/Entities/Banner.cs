namespace RioCommerce.Core.Entities;

/// <summary>
/// Homepage / placement banner shown below the site header (NopCommerce "Anywhere Slider"-style).
/// Supports separate desktop &amp; mobile artwork, an optional title/description/CTA button, a
/// scheduling window (Start/End) and per-banner display ordering. Multiple active banners in the
/// same placement render as an auto-advancing carousel on the storefront.
///
/// Backward-compat: the original <see cref="ImageUrl"/> field is retained and used as the desktop
/// image fallback when <see cref="DesktopImageUrl"/> is empty, so pre-existing rows keep working.
/// </summary>
public class Banner : BaseEntity
{
    /// <summary>Internal name shown in the admin list (not rendered on the storefront).</summary>
    public string Name { get; set; } = string.Empty;

    // ── Imagery ──────────────────────────────────────────────────────────────
    /// <summary>Legacy single image. Kept as the desktop fallback for old rows.</summary>
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>Desktop artwork (recommended 1920×600). Falls back to <see cref="ImageUrl"/> if empty.</summary>
    public string? DesktopImageUrl { get; set; }
    /// <summary>Mobile artwork (recommended 768×900). Falls back to the desktop image if empty.</summary>
    public string? MobileImageUrl { get; set; }

    // ── Optional overlay content ──────────────────────────────────────────────
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ButtonText { get; set; }

    /// <summary>
    /// Where a click on the banner / button goes. Mirrors the legacy <c>LinkUrl</c> column, which
    /// is kept as the storage column so existing data is preserved.
    /// </summary>
    public string? LinkUrl { get; set; }

    /// <summary>e.g. "homepage". Lets the same slider mechanism drive other placements later.</summary>
    public string Placement { get; set; } = "homepage";

    public int DisplayOrder { get; set; }

    // ── Scheduling ─────────────────────────────────────────────────────────────
    /// <summary>Inclusive start of the visibility window (UTC). Null = visible immediately.</summary>
    public DateTime? StartDate { get; set; }
    /// <summary>Inclusive end of the visibility window (UTC). Null = never expires.</summary>
    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; } = true;
}
