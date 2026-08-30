namespace RioCommerce.Core.DTOs.Content;

// ── CMS pages ──
// Body = safe, style-free HTML (custom <style> blocks are extracted server-side).
// ScopedCss = that page's CSS with every selector prefixed to [data-cms-page-id="{Id}"], so it can
// only ever style this page's wrapper. Id/Slug feed the unique page wrapper on the storefront.
public record CmsPageView(string Title, string Body, string? SeoTitle, string? SeoDescription,
    Guid Id, string Slug, string ScopedCss);
public record CmsPageItem(Guid Id, string Title, string Slug, bool IsPublished, int DisplayOrder, DateTime UpdatedAt);

public class CmsPageEditModel
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsPublished { get; set; } = true;
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public int DisplayOrder { get; set; }
}

// ── Blog ──
public record BlogListItem(Guid Id, string Title, string Slug, string? Excerpt, string? FeaturedImage, string? Category, DateTime? PublishedAt, int Views, int ReadMinutes);

// Lightweight prev/next navigation link for the blog detail page.
public record BlogNavLink(string Title, string Slug);

/// <summary>One "In A Hurry" summary card as the storefront renders it.</summary>
public record BlogSummaryPointView(int Number, string Title, string Description);

/// <summary>
/// One "Explore Our Courses" card, already filtered to the enabled ones and with every URL
/// validated. IsExternal drives target="_blank" + rel="noopener noreferrer".
///
/// Two shapes share this record: when <paramref name="ImageUrl"/> is set the card IS the artwork
/// (rendered on a transparent container so the image's own colours are the only ones visible);
/// otherwise it falls back to the original Title/SubText tile.
/// </summary>
public record BlogCourseCardView(string Title, string SubText, string Url, bool IsExternal,
    string? ImageUrl, string? AltText)
{
    public bool IsImage => !string.IsNullOrWhiteSpace(ImageUrl);
}

/// <summary>Editable card. Title/SubText are plain text — never rendered as HTML.</summary>
public class BlogCourseCardEdit
{
    /// <summary>The section is a fixed four slots, matching the storefront design.</summary>
    public const int CardCount = 4;

    public string? Title { get; set; }
    public string? SubText { get; set; }
    public string? Url { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Optional artwork (GIF/PNG/JPG URL). When set, the card renders as this image on a
    /// transparent container and Title/SubText are not drawn — the image supplies the whole design.
    /// Leaving it empty keeps the original text tile, so existing cards are unaffected.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Accessible description used as the img alt. Falls back to Title when blank.</summary>
    public string? AltText { get; set; }

    /// <summary>Computed, so it must not be written into the stored JSON — it would bloat the
    /// payload and become a stale duplicate of the three fields it is derived from.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(SubText) || !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// What a post shows when its cards have never been configured. These are the same four
    /// links the page hardcoded before this feature, so existing posts are unchanged on screen.
    /// Defaults only — the admin can rewrite every field.
    /// </summary>
    public static List<BlogCourseCardEdit> DefaultSet() => new()
    {
        new() { Title = "CA Foundation",    SubText = "View Courses →", Url = "/ca-foundation",    Enabled = true },
        new() { Title = "CA Intermediate",  SubText = "View Courses →", Url = "/ca-intermediate",  Enabled = true },
        new() { Title = "CA Final",         SubText = "View Courses →", Url = "/ca-final",         Enabled = true },
        new() { Title = "Our Books",        SubText = "View Courses →", Url = "/books",            Enabled = true },
    };
}

// Body = safe, style-free HTML; ScopedCss = this post's CSS prefixed to [data-blog-id="{Id}"] so it
// can only style this post's wrapper. Id/Slug feed the unique blog wrapper on the detail page.
// SummaryPoints is empty unless the post has ShowSummary on AND has saved points, so the detail
// page can render the section on a simple non-empty check.
public record BlogPostView(
    string Title, string Slug, string Body, string? Excerpt, string? FeaturedImage, string? Category,
    DateTime? PublishedAt, int Views, int ReadMinutes, string? SeoTitle, string? SeoDescription,
    BlogNavLink? Prev, BlogNavLink? Next, List<BlogListItem> Related,
    Guid Id, string? ScopedCss, List<BlogSummaryPointView> SummaryPoints,
    // PromotionalImageUrl = the image SOURCE. PromotionalImageLinkUrl = where a click on it goes;
    // null there means the image renders un-wrapped, with no anchor at all.
    string? PromotionalImageUrl, string? PromotionalImageLinkUrl, bool PromotionalImageLinkIsExternal,
    // Video + counselling block. YoutubeEmbedUrl is already the safe
    // https://www.youtube.com/embed/{id} form built server-side from a validated
    // id, so the page never parses a URL — it is null when the post has no usable
    // video, which is also how the frontend decides not to render that half.
    bool ShowVideoSection, string? VideoHeading, string? YoutubeEmbedUrl,
    bool ShowCounsellingForm,
    // Already filtered to enabled cards with valid URLs, so the page renders the list as-is
    // and an empty list simply means no section.
    bool ShowExploreCourses, List<BlogCourseCardView> CourseCards);

public record BlogAdminItem(Guid Id, string Title, string Slug, string? Category, bool IsPublished, DateTime? PublishedAt, int Views, int DisplayOrder);

public class BlogEditModel
{
    public Guid? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string Body { get; set; } = string.Empty;
    // Separate page-scoped CSS for the blog detail page (auto-scoped to [data-blog-id] on render).
    public string? CustomCss { get; set; }
    // Relative path of an uploaded image (e.g. /uploads/blog/ab12.png) — legacy absolute URLs still work.
    public string? FeaturedImage { get; set; }
    /// <summary>Square sidebar promo for THIS post: an uploaded path or an external http(s) URL.
    /// This is the image SOURCE.</summary>
    public string? PromotionalImageUrl { get; set; }

    /// <summary>Optional click destination for that image — NOT the image source. Empty leaves the
    /// image non-clickable.</summary>
    public string? PromotionalImageLinkUrl { get; set; }
    public string? Category { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPublished { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }

    // ── "In A Hurry" summary ──
    /// <summary>Per-post switch. False on every existing post (migration 0027 default).</summary>
    public bool ShowSummary { get; set; }

    /// <summary>Always exactly <see cref="BlogSummaryPointEdit.PointCount"/> rows, in order, so the
    /// admin form can bind fixed inputs. Blank rows are simply not persisted.</summary>
    public List<BlogSummaryPointEdit> SummaryPoints { get; set; } = BlogSummaryPointEdit.EmptySet();

    // ── Video + counselling block ──
    public bool ShowVideoSection { get; set; }
    public string? VideoHeading { get; set; }
    /// <summary>The raw YouTube link as typed. Validated on save; the embed URL is derived, never stored.</summary>
    public string? YoutubeVideoUrl { get; set; }
    public bool ShowCounsellingForm { get; set; }

    // ── Explore Our Courses ──
    public bool ShowExploreCourses { get; set; } = true;
    /// <summary>Always exactly <see cref="BlogCourseCardEdit.CardCount"/> rows so the admin form
    /// can bind fixed inputs; seeded with the defaults for a post that has none saved.</summary>
    public List<BlogCourseCardEdit> CourseCards { get; set; } = BlogCourseCardEdit.DefaultSet();
}

/// <summary>One editable summary point. Number is fixed by position, not user-entered.</summary>
public class BlogSummaryPointEdit
{
    public const int PointCount = 5;

    public int Number { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>True when the editor has typed anything into this row.</summary>
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Description);

    /// <summary>Five blank, correctly-numbered rows for a new post.</summary>
    public static List<BlogSummaryPointEdit> EmptySet() =>
        Enumerable.Range(1, PointCount).Select(n => new BlogSummaryPointEdit { Number = n }).ToList();
}

public record ContentStats(int Pages, int PublishedPages, int Posts, int PublishedPosts);
