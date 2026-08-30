using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class BlogPost : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Excerpt { get; set; }
    public string Body { get; set; } = string.Empty;          // content HTML (may include inline <style>)
    public string? CustomCss { get; set; }                    // separate page-scoped CSS for the blog detail page
    public string? FeaturedImage { get; set; }
    // Square promo shown in the blog detail sidebar, per post. Either a managed
    // upload path (/uploads/blog/…) or an external http(s) URL — never binary.
    // Null means the sidebar simply omits the promo block.
    public string? PromotionalImageUrl { get; set; }
    /// <summary>Where a click on that promo image goes. Distinct from PromotionalImageUrl, which is
    /// the image SOURCE. Null means the image renders as a plain, non-clickable picture.</summary>
    public string? PromotionalImageLinkUrl { get; set; }
    public string? Category { get; set; }
    public Guid? AuthorId { get; set; }
    public ContentStatus Status { get; set; } = ContentStatus.Draft;
    // Manual ordering for public listings: lower shows first, ties broken by newest PublishedAt.
    public int DisplayOrder { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public int Views { get; set; }
    // "In A Hurry" 30-second summary. Off by default so existing posts — which have
    // no points — never render an empty section; the editor opts in per post.
    public bool ShowSummary { get; set; }

    // ── Video + counselling block, rendered between the article and Explore Our
    //    Courses. The two switches are independent, so a post can show the video
    //    alone, the form alone, both, or neither. YoutubeVideoUrl holds the plain
    //    link the editor pasted — never iframe HTML; the embed src is built from a
    //    validated id at render time (Core/Security/YouTubeEmbed).
    public bool ShowVideoSection { get; set; }
    public string? VideoHeading { get; set; }
    public string? YoutubeVideoUrl { get; set; }
    public bool ShowCounsellingForm { get; set; }

    // ── "Explore Our Courses" cards ──
    // Stored as one JSON array rather than 16 flat columns, following the existing
    // Product.FaqsJson convention for a repeating structured list on a content entity.
    // NULL means "never configured" and the four site defaults are used, so existing
    // posts keep the section they already had without a data backfill.
    public bool ShowExploreCourses { get; set; } = true;
    public string? ExploreCoursesJson { get; set; }   // JSON array of { title, subText, url, enabled }
    public User? Author { get; set; }
    public ICollection<BlogSummaryPoint> SummaryPoints { get; set; } = new List<BlogSummaryPoint>();
}
