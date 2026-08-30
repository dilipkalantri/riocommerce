using System.Text.RegularExpressions;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Security;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ContentService : IContentService
{
    private readonly RioCommerceDbContext _db;
    private readonly IPublicFileStorage _files;
    public ContentService(RioCommerceDbContext db, IPublicFileStorage files)
    {
        _db = db;
        _files = files;
    }

    public async Task<ContentStats> StatsAsync() => new(
        await _db.Set<CmsPage>().CountAsync(),
        await _db.Set<CmsPage>().CountAsync(p => p.IsPublished),
        await _db.BlogPosts.CountAsync(),
        await _db.BlogPosts.CountAsync(p => p.Status == ContentStatus.Published));

    // ── CMS pages ──

    public async Task<CmsPageView?> GetPageAsync(string slug)
    {
        var s = slug.Trim().ToLower();
        var p = await _db.Set<CmsPage>().FirstOrDefaultAsync(x => x.Slug == s && x.IsPublished);
        if (p == null) return null;
        // Extract + page-scope custom CSS and strip scripts so nothing leaks past this page's wrapper.
        var (html, scopedCss) = CmsContentRenderer.Render(p.Body, p.Id);
        return new CmsPageView(p.Title, html, p.SeoTitle, p.SeoDescription, p.Id, p.Slug, scopedCss);
    }

    public async Task<List<CmsPageItem>> ListPagesAsync() =>
        await _db.Set<CmsPage>().OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title)
            .Select(p => new CmsPageItem(p.Id, p.Title, p.Slug, p.IsPublished, p.DisplayOrder, p.UpdatedAt)).ToListAsync();

    public async Task<CmsPageEditModel?> GetPageForEditAsync(Guid id)
    {
        var p = await _db.Set<CmsPage>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return p == null ? null : new CmsPageEditModel
        {
            Id = p.Id, Title = p.Title, Slug = p.Slug, Body = p.Body,
            IsPublished = p.IsPublished, SeoTitle = p.SeoTitle, SeoDescription = p.SeoDescription, DisplayOrder = p.DisplayOrder
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SavePageAsync(CmsPageEditModel m)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(m.Title)) return (false, "Title is required.", Guid.Empty);
        var slug = string.IsNullOrWhiteSpace(m.Slug) ? Slugify(m.Title) : Slugify(m.Slug);
        if (await _db.Set<CmsPage>().AnyAsync(p => p.Slug == slug && p.Id != (m.Id ?? Guid.Empty)))
            return (false, "Another page already uses that slug.", Guid.Empty);

        CmsPage e;
        if (m.Id is { } id && id != Guid.Empty)
            e = await _db.Set<CmsPage>().FirstOrDefaultAsync(p => p.Id == id) ?? throw new InvalidOperationException("Page not found.");
        else { e = new CmsPage(); _db.Add(e); }

        e.Title = m.Title.Trim(); e.Slug = slug; e.Body = m.Body ?? "";
        e.IsPublished = m.IsPublished; e.DisplayOrder = m.DisplayOrder;
        e.SeoTitle = Clean(m.SeoTitle); e.SeoDescription = Clean(m.SeoDescription);
        await _db.SaveChangesAsync();
        return (true, null, e.Id);
    }

    public async Task TogglePageAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Set<CmsPage>().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return;
        p.IsPublished = !p.IsPublished;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeletePageAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Set<CmsPage>().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Page not found.");
        _db.Remove(p); await _db.SaveChangesAsync();
        return (true, null);
    }

    // ── Blog ──

    // Published posts, newest first, paginated. Homepage requests page 1 with pageSize 3;
    // the blog listing page uses a larger page size. TotalCount lets callers decide whether
    // to show a "View All Blogs" button / pager.
    public async Task<PagedResult<BlogListItem>> PublishedPostsAsync(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 60);

        var query = _db.BlogPosts.Where(p => p.Status == ContentStatus.Published);
        var total = await query.CountAsync();

        // Body is needed only to estimate reading time; project a slim shape then compute in memory.
        // Public ordering: manual DisplayOrder ascending, then newest PublishedAt first.
        var rows = await query.OrderBy(p => p.DisplayOrder).ThenByDescending(p => p.PublishedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new { p.Id, p.Title, p.Slug, p.Excerpt, p.FeaturedImage, p.Category, p.PublishedAt, p.Views, p.Body })
            .ToListAsync();

        return new PagedResult<BlogListItem>
        {
            Items = rows.Select(p => new BlogListItem(
                p.Id, p.Title, p.Slug, p.Excerpt, p.FeaturedImage, p.Category, p.PublishedAt, p.Views, ReadMinutes(p.Body))).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<BlogPostView?> GetPostAsync(string slug)
    {
        var s = slug.Trim().ToLower();
        var p = await _db.BlogPosts.FirstOrDefaultAsync(x => x.Slug == s && x.Status == ContentStatus.Published);
        if (p == null) return null;
        p.Views++;
        await _db.SaveChangesAsync();

        var published = _db.BlogPosts.Where(x => x.Status == ContentStatus.Published && x.Id != p.Id);

        // Prev = the next-older published post; Next = the next-newer one (chronological navigation).
        var prev = await published.Where(x => x.PublishedAt < p.PublishedAt)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new BlogNavLink(x.Title, x.Slug)).FirstOrDefaultAsync();
        var next = await published.Where(x => x.PublishedAt > p.PublishedAt)
            .OrderBy(x => x.PublishedAt)
            .Select(x => new BlogNavLink(x.Title, x.Slug)).FirstOrDefaultAsync();

        // Related = up to 3 posts in the same category (newest first); fall back to the latest others.
        var relatedQuery = p.Category != null
            ? published.Where(x => x.Category == p.Category)
            : published;
        var relatedRows = await relatedQuery.OrderByDescending(x => x.PublishedAt).Take(3)
            .Select(x => new { x.Id, x.Title, x.Slug, x.Excerpt, x.FeaturedImage, x.Category, x.PublishedAt, x.Views, x.Body })
            .ToListAsync();
        if (relatedRows.Count == 0 && p.Category != null)
            relatedRows = await published.OrderByDescending(x => x.PublishedAt).Take(3)
                .Select(x => new { x.Id, x.Title, x.Slug, x.Excerpt, x.FeaturedImage, x.Category, x.PublishedAt, x.Views, x.Body })
                .ToListAsync();
        var related = relatedRows.Select(x => new BlogListItem(
            x.Id, x.Title, x.Slug, x.Excerpt, x.FeaturedImage, x.Category, x.PublishedAt, x.Views, ReadMinutes(x.Body))).ToList();

        // Extract + page-scope custom CSS (and any inline <style>) and strip scripts so nothing leaks
        // past this post's [data-blog-id] wrapper. Read time still uses the raw body.
        var (safeHtml, scopedCss) = CmsContentRenderer.RenderScoped(p.Body, p.CustomCss, "data-blog-id", p.Id);

        // "In A Hurry" points. Only read when the post has the section switched on, so a
        // post with drafted-but-disabled points sends nothing to the storefront.
        var summary = p.ShowSummary
            ? await _db.BlogSummaryPoints.AsNoTracking()
                .Where(x => x.BlogPostId == p.Id)
                .OrderBy(x => x.PointNumber)
                .Select(x => new BlogSummaryPointView(x.PointNumber, x.Title, x.Description))
                .ToListAsync()
            : new List<BlogSummaryPointView>();

        return new BlogPostView(
            p.Title, p.Slug, safeHtml, p.Excerpt, p.FeaturedImage, p.Category, p.PublishedAt, p.Views, ReadMinutes(p.Body),
            p.SeoTitle, p.SeoDescription, prev, next, related, p.Id, scopedCss, summary,
            p.PromotionalImageUrl,
            // Re-validated on read, so a value that somehow bypassed save validation still can't
            // reach an href. IsExternal drives the same new-tab convention the course cards use.
            SafeUrl.IsHttpOrRelative(p.PromotionalImageLinkUrl) ? p.PromotionalImageLinkUrl!.Trim() : null,
            SafeUrl.IsExternal(p.PromotionalImageLinkUrl),
            // Embed URL is derived here, not stored, so an id that was valid on save
            // is re-validated on every read and the page only ever sees a safe src.
            p.ShowVideoSection, p.VideoHeading, YouTubeEmbed.TryGetEmbedUrl(p.YoutubeVideoUrl),
            p.ShowCounsellingForm,
            p.ShowExploreCourses, CardViews(p.ExploreCoursesJson));
    }

    public async Task<List<BlogAdminItem>> ListPostsAsync() =>
        await _db.BlogPosts.OrderBy(p => p.DisplayOrder).ThenByDescending(p => p.CreatedAt)
            .Select(p => new BlogAdminItem(p.Id, p.Title, p.Slug, p.Category, p.Status == ContentStatus.Published, p.PublishedAt, p.Views, p.DisplayOrder))
            .ToListAsync();

    public async Task<BlogEditModel?> GetPostForEditAsync(Guid id)
    {
        var p = await _db.BlogPosts.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return null;

        // Saved points are merged into a fixed set of five rows by position, so the form
        // always has five inputs to bind and a post saved with gaps still edits cleanly.
        var saved = await _db.BlogSummaryPoints.AsNoTracking()
            .Where(x => x.BlogPostId == p.Id)
            .ToDictionaryAsync(x => x.PointNumber);

        var rows = BlogSummaryPointEdit.EmptySet();
        foreach (var row in rows)
            if (saved.TryGetValue(row.Number, out var s)) { row.Title = s.Title; row.Description = s.Description; }

        return new BlogEditModel
        {
            Id = p.Id, Title = p.Title, Slug = p.Slug, Excerpt = p.Excerpt, Body = p.Body, CustomCss = p.CustomCss,
            FeaturedImage = p.FeaturedImage, PromotionalImageUrl = p.PromotionalImageUrl,
            PromotionalImageLinkUrl = p.PromotionalImageLinkUrl,
            Category = p.Category, DisplayOrder = p.DisplayOrder,
            IsPublished = p.Status == ContentStatus.Published,
            SeoTitle = p.SeoTitle, SeoDescription = p.SeoDescription,
            ShowSummary = p.ShowSummary, SummaryPoints = rows,
            ShowVideoSection = p.ShowVideoSection, VideoHeading = p.VideoHeading,
            YoutubeVideoUrl = p.YoutubeVideoUrl, ShowCounsellingForm = p.ShowCounsellingForm,
            ShowExploreCourses = p.ShowExploreCourses, CourseCards = ReadCards(p.ExploreCoursesJson)
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SavePostAsync(BlogEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Title)) return (false, "Title is required.", Guid.Empty);
        var slug = string.IsNullOrWhiteSpace(m.Slug) ? Slugify(m.Title) : Slugify(m.Slug);
        if (await _db.BlogPosts.AnyAsync(p => p.Slug == slug && p.Id != (m.Id ?? Guid.Empty)))
            return (false, "Another post already uses that slug.", Guid.Empty);

        // Promo image: an uploaded path we produced, or an external http(s) URL.
        // Anything else — javascript:, data:, vbscript:, file: — is rejected rather
        // than sanitised, so a hostile value can never reach an img src.
        var promo = Clean(m.PromotionalImageUrl);
        if (promo != null && !IsAllowedImageRef(promo))
            return (false, "The promotional image must be an uploaded image or an http:// or https:// URL.", Guid.Empty);

        // Click destination for that image — a separate value from the image source above.
        // Empty is allowed and simply leaves the image non-clickable.
        var promoLink = Clean(m.PromotionalImageLinkUrl);
        if (promoLink != null && !SafeUrl.IsHttpOrRelative(promoLink))
            return (false, "The promotional image hyperlink must start with / or http(s)://.", Guid.Empty);

        // Video link. Rejected outright rather than silently dropped, so an editor
        // who mistypes a URL is told instead of publishing a video-less block.
        // Only enforced when the video half is switched on.
        var videoUrl = Clean(m.YoutubeVideoUrl);
        if (m.ShowVideoSection)
        {
            if (videoUrl == null)
                return (false, "Add a YouTube video URL, or switch the video section off.", Guid.Empty);
            if (!YouTubeEmbed.IsValid(videoUrl))
                return (false, "That is not a recognised YouTube link. Use a youtube.com/watch?v=… or youtu.be/… URL.", Guid.Empty);
        }

        // "Explore Our Courses" validation. Only enabled cards are checked, so an editor can
        // leave a half-drafted card saved as long as it is switched off.
        if (m.ShowExploreCourses)
        {
            var cards = (m.CourseCards ?? new()).Take(BlogCourseCardEdit.CardCount).ToList();
            for (var i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (!c.Enabled) continue;

                var hasImage = !string.IsNullOrWhiteSpace(c.ImageUrl);
                // An image card is complete on its own — the artwork carries the design, so
                // Title/SubText are only required when there is no image to show.
                if (!hasImage && (string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(c.SubText)))
                    return (false, $"Course card {i + 1} needs a title and sub text (or an image URL), or switch that card off.", Guid.Empty);
                if (hasImage && !SafeUrl.IsHttpOrRelative(c.ImageUrl))
                    return (false, $"Course card {i + 1}: the image URL must start with / or http(s)://.", Guid.Empty);
                if (!SafeUrl.IsHttpOrRelative(c.Url))
                    return (false, $"Course card {i + 1} needs a link starting with / or http(s)://.", Guid.Empty);
            }
        }

        // "In A Hurry" validation. Enforced only when the section is switched on —
        // an editor can leave half-drafted points saved while the section is off.
        var points = (m.SummaryPoints ?? new()).Where(x => x.HasContent).ToList();
        if (m.ShowSummary)
        {
            if (points.Count != BlogSummaryPointEdit.PointCount)
                return (false, $"The In A Hurry summary needs all {BlogSummaryPointEdit.PointCount} points filled in, or switch it off.", Guid.Empty);
            // A row with only one half filled would render a card with a missing line.
            if (points.Any(x => string.IsNullOrWhiteSpace(x.Title) || string.IsNullOrWhiteSpace(x.Description)))
                return (false, "Every In A Hurry point needs both a title and a description.", Guid.Empty);
        }

        BlogPost e;
        if (m.Id is { } id && id != Guid.Empty)
            e = await _db.BlogPosts.FirstOrDefaultAsync(p => p.Id == id) ?? throw new InvalidOperationException("Post not found.");
        else { e = new BlogPost(); _db.BlogPosts.Add(e); }

        var wasPublished = e.Status == ContentStatus.Published;
        var oldImage = e.FeaturedImage;
        var newImage = Clean(m.FeaturedImage);
        var oldPromo = e.PromotionalImageUrl;
        e.Title = m.Title.Trim(); e.Slug = slug; e.Excerpt = Clean(m.Excerpt); e.Body = m.Body ?? "";
        e.CustomCss = string.IsNullOrWhiteSpace(m.CustomCss) ? null : m.CustomCss;
        e.FeaturedImage = newImage; e.PromotionalImageUrl = promo; e.PromotionalImageLinkUrl = promoLink;
        e.Category = Clean(m.Category); e.DisplayOrder = m.DisplayOrder;
        e.SeoTitle = Clean(m.SeoTitle); e.SeoDescription = Clean(m.SeoDescription);
        e.Status = m.IsPublished ? ContentStatus.Published : ContentStatus.Draft;
        if (m.IsPublished && (!wasPublished || e.PublishedAt == null)) e.PublishedAt = DateTime.UtcNow;
        e.ShowSummary = m.ShowSummary;
        e.ShowVideoSection = m.ShowVideoSection;
        e.VideoHeading = Clean(m.VideoHeading);
        e.YoutubeVideoUrl = videoUrl;
        e.ShowCounsellingForm = m.ShowCounsellingForm;
        e.ShowExploreCourses = m.ShowExploreCourses;
        e.ExploreCoursesJson = WriteCards(m.CourseCards);
        // Save the post first so a brand-new post has its Id before the child rows
        // reference it.
        await _db.SaveChangesAsync();
        await SaveSummaryPointsAsync(e.Id, points);
        await _db.SaveChangesAsync();
        // Clean up a replaced/removed managed upload (no-op for legacy external URLs).
        if (!string.IsNullOrEmpty(oldImage) && oldImage != newImage) _files.Delete(oldImage);
        // Same for the promo. _files.Delete is a no-op for external URLs, and the
        // guard stops a promo that merely reuses the featured image from deleting it.
        if (!string.IsNullOrEmpty(oldPromo) && oldPromo != promo && oldPromo != newImage) _files.Delete(oldPromo);
        return (true, null, e.Id);
    }

    /// <summary>
    /// Replaces a post's summary points with <paramref name="rows"/> (already filtered to
    /// rows that have content). Existing rows are updated in place rather than deleted and
    /// re-inserted, so Ids and CreatedAt survive an edit and the unique
    /// (BlogPostId, PointNumber) index is never transiently violated.
    /// Caller commits.
    /// </summary>
    private async Task SaveSummaryPointsAsync(Guid postId, List<BlogSummaryPointEdit> rows)
    {
        var existing = await _db.BlogSummaryPoints.Where(x => x.BlogPostId == postId).ToListAsync();
        var keep = new HashSet<int>();

        foreach (var r in rows)
        {
            keep.Add(r.Number);
            var title = (r.Title ?? "").Trim();
            var desc = (r.Description ?? "").Trim();

            var row = existing.FirstOrDefault(x => x.PointNumber == r.Number);
            if (row == null)
            {
                _db.BlogSummaryPoints.Add(new BlogSummaryPoint
                {
                    BlogPostId = postId, PointNumber = r.Number, Title = title, Description = desc
                });
            }
            else { row.Title = title; row.Description = desc; }
        }

        // Rows the editor cleared.
        var drop = existing.Where(x => !keep.Contains(x.PointNumber)).ToList();
        if (drop.Count > 0) _db.BlogSummaryPoints.RemoveRange(drop);
    }

    public async Task TogglePostAsync(Guid id)
    {
        var p = await _db.BlogPosts.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return;
        if (p.Status == ContentStatus.Published) p.Status = ContentStatus.Draft;
        else { p.Status = ContentStatus.Published; p.PublishedAt ??= DateTime.UtcNow; }
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeletePostAsync(Guid id)
    {
        var p = await _db.BlogPosts.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return (false, "Post not found.");
        var image = p.FeaturedImage;
        _db.BlogPosts.Remove(p); await _db.SaveChangesAsync();
        _files.Delete(image);   // remove the managed upload if any (no-op for external URLs)
        return (true, null);
    }

    public Task<string> UploadBlogImageAsync(string extension, Stream content) =>
        _files.SaveAsync("blog", extension, content);

    public async Task<(List<string> pageSlugs, List<string> blogSlugs)> PublishedSlugsAsync()
    {
        var pages = await _db.Set<CmsPage>().Where(p => p.IsPublished).Select(p => p.Slug).ToListAsync();
        var blogs = await _db.BlogPosts.Where(p => p.Status == ContentStatus.Published).Select(p => p.Slug).ToListAsync();
        return (pages, blogs);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── "Explore Our Courses" cards (Product.FaqsJson convention) ──
    private static readonly System.Text.Json.JsonSerializerOptions _cardJson =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Saved cards, or the built-in defaults when the post has never been configured.
    /// A malformed or empty payload also falls back to the defaults rather than showing
    /// nothing, so a bad row can never blank the section.
    /// </summary>
    private static List<BlogCourseCardEdit> ReadCards(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return BlogCourseCardEdit.DefaultSet();
        try
        {
            var list = System.Text.Json.JsonSerializer.Deserialize<List<BlogCourseCardEdit>>(json, _cardJson);
            if (list is not { Count: > 0 }) return BlogCourseCardEdit.DefaultSet();

            // Pad or trim to the fixed slot count so the admin form always binds cleanly,
            // even against a payload written by an older or newer shape.
            while (list.Count < BlogCourseCardEdit.CardCount) list.Add(new BlogCourseCardEdit { Enabled = false });
            return list.Take(BlogCourseCardEdit.CardCount).ToList();
        }
        catch { return BlogCourseCardEdit.DefaultSet(); }
    }

    private static string WriteCards(List<BlogCourseCardEdit>? cards) =>
        System.Text.Json.JsonSerializer.Serialize(
            (cards ?? BlogCourseCardEdit.DefaultSet()).Take(BlogCourseCardEdit.CardCount).Select(c => new BlogCourseCardEdit
            {
                Title = Clean(c.Title), SubText = Clean(c.SubText), Url = Clean(c.Url), Enabled = c.Enabled,
                ImageUrl = Clean(c.ImageUrl), AltText = Clean(c.AltText)
            }).ToList(), _cardJson);

    /// <summary>
    /// Storefront projection: enabled cards only, each with URLs that passed the allowlist.
    /// A card is dropped rather than rendered broken if its link is unsafe, or if it has neither
    /// artwork nor title text to show. An image card needs no Title/SubText — the artwork is the
    /// card — so those are only required on a text card.
    /// </summary>
    private static List<BlogCourseCardView> CardViews(string? json) =>
        ReadCards(json)
            .Where(c => c.Enabled && SafeUrl.IsHttpOrRelative(c.Url))
            // An image URL that fails the allowlist demotes the card to its text form rather than
            // silently emitting an unsafe src.
            .Select(c => new
            {
                Card = c,
                Image = SafeUrl.IsHttpOrRelative(c.ImageUrl) ? c.ImageUrl!.Trim() : null
            })
            .Where(x => x.Image != null
                        || (!string.IsNullOrWhiteSpace(x.Card.Title) && !string.IsNullOrWhiteSpace(x.Card.SubText)))
            .Select(x => new BlogCourseCardView(
                (x.Card.Title ?? "").Trim(),
                (x.Card.SubText ?? "").Trim(),
                x.Card.Url!.Trim(),
                SafeUrl.IsExternal(x.Card.Url),
                x.Image,
                // Alt falls back to the title; an image card with neither gets "" (decorative).
                string.IsNullOrWhiteSpace(x.Card.AltText) ? (x.Card.Title ?? "").Trim() : x.Card.AltText.Trim()))
            .ToList();

    /// <summary>Allowlist for anything that will end up in an img src: a site-relative path
    /// (what UploadBlogImageAsync returns), or an absolute http/https URL. Same rule the
    /// Explore Our Courses links use — see <see cref="SafeUrl"/> for why it is an allowlist.</summary>
    private static bool IsAllowedImageRef(string value) => SafeUrl.IsHttpOrRelative(value);

    // Estimated reading time in minutes: strip HTML, count words, assume ~200 wpm (min 1).
    private static int ReadMinutes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return 1;
        var text = Regex.Replace(body, "<[^>]+>", " ");
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / 200.0));
    }

    private static string Slugify(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? Guid.NewGuid().ToString("n")[..8] : s;
    }
}
