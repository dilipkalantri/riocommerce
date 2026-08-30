namespace RioCommerce.Core.Entities;

// One student testimonial attached to a Product. The public Course-detail tab
// renders these as YouTube reel cards; admins author them via the new
// Testimonials section on Product Edit.
public class ProductTestimonial : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string StudentName { get; set; } = string.Empty;
    public string? CourseName { get; set; }

    /// <summary>
    /// Raw YouTube URL — admin can paste any of:
    /// https://youtu.be/&lt;id&gt;, https://youtube.com/shorts/&lt;id&gt;,
    /// https://youtube.com/watch?v=&lt;id&gt;, https://www.youtube.com/embed/&lt;id&gt;.
    /// The repository extracts the video id at read time.
    /// </summary>
    public string YoutubeUrl { get; set; } = string.Empty;

    /// <summary>Override thumbnail. When null the UI falls back to the YouTube CDN derived from the video id.</summary>
    public string? ThumbnailUrl { get; set; }

    public int DisplayOrder { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsActive { get; set; } = true;
}
