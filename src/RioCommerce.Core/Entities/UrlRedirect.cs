namespace RioCommerce.Core.Entities;

/// <summary>
/// One stored URL redirect rule. The middleware looks up <see cref="OldUrl"/>
/// on every request that isn't an admin / api / static-asset request, and
/// emits a 301 (or 302) to <see cref="NewUrl"/> when a match is found.
///
/// OldUrl is normalised to the path-only, leading-slash, lower-case form
/// (e.g. "/ca-foundation-economics-regular-by-ca-harshad-jaju") so a
/// case-insensitive index lookup is enough.
/// </summary>
public class UrlRedirect : BaseEntity
{
    public string OldUrl { get; set; } = string.Empty;
    public string NewUrl { get; set; } = string.Empty;
    /// <summary>301 (permanent) or 302 (temporary). 301 is the SEO-preserving default.</summary>
    public int RedirectType { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public int HitCount { get; set; }
    public DateTime? LastHitAt { get; set; }
    /// <summary>Admin-facing free-text — e.g. "Imported from old sitemap" / "Manual entry".</summary>
    public string? Notes { get; set; }
}
