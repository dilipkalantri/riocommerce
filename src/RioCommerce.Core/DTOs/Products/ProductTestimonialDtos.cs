namespace RioCommerce.Core.DTOs.Products;

// Admin edit-form DTO — one row per testimonial. Bound to the new Testimonials
// section on Product Edit; the order in the list is the wire DisplayOrder.
public class ProductTestimonialEdit
{
    public Guid? Id { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string? CourseName { get; set; }
    public string YoutubeUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;
}

// Public DTO consumed by the Course-detail Testimonials tab. The repository
// extracts the YouTube video id from the raw URL once at load time so the UI
// doesn't need to parse it on every render.
public record ProductTestimonialPublic(
    Guid Id,
    string StudentName,
    string? CourseName,
    string YoutubeVideoId,
    bool IsShorts,
    string ThumbnailUrl,
    string FallbackThumbnailUrl,
    bool IsFeatured);

// Centralised URL parser + thumbnail resolver. The brief calls for the highest-
// quality thumbnail by default with an automatic fallback when YouTube hasn't
// generated maxresdefault for that video. The `<img>` tag at the use site
// performs the actual fallback via `onerror`; we expose both URLs here so the
// markup stays declarative.
public static class YouTubeUrlHelpers
{
    public const string MaxThumbnailTemplate = "https://img.youtube.com/vi/{0}/maxresdefault.jpg";
    public const string HqThumbnailTemplate  = "https://img.youtube.com/vi/{0}/hqdefault.jpg";

    /// <summary>
    /// Extracts the video id from common YouTube URL shapes.
    /// Returns (id, isShorts). On failure returns (null, false).
    /// </summary>
    public static (string? Id, bool IsShorts) Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (null, false);
        var u = url.Trim();

        // youtu.be/<id>
        var m = System.Text.RegularExpressions.Regex.Match(u, @"youtu\.be/([A-Za-z0-9_-]{6,})");
        if (m.Success) return (m.Groups[1].Value, false);

        // youtube.com/shorts/<id>
        m = System.Text.RegularExpressions.Regex.Match(u, @"youtube\.com/shorts/([A-Za-z0-9_-]{6,})");
        if (m.Success) return (m.Groups[1].Value, true);

        // youtube.com/embed/<id>
        m = System.Text.RegularExpressions.Regex.Match(u, @"youtube\.com/embed/([A-Za-z0-9_-]{6,})");
        if (m.Success) return (m.Groups[1].Value, false);

        // youtube.com/watch?v=<id>
        m = System.Text.RegularExpressions.Regex.Match(u, @"[?&]v=([A-Za-z0-9_-]{6,})");
        if (m.Success) return (m.Groups[1].Value, false);

        return (null, false);
    }

    /// <summary>Primary thumbnail URL — admin override wins; otherwise maxresdefault.jpg.</summary>
    public static string ResolveThumbnail(string? overrideUrl, string videoId)
        => !string.IsNullOrWhiteSpace(overrideUrl)
            ? overrideUrl!.Trim()
            : string.Format(MaxThumbnailTemplate, videoId);

    /// <summary>Fallback thumbnail URL — always hqdefault.jpg from the YouTube CDN.</summary>
    public static string FallbackThumbnail(string videoId)
        => string.Format(HqThumbnailTemplate, videoId);
}
