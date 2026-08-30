namespace RioCommerce.Core.Entities;

// One sample-pages preview attached to a Product.
//
// The brief lets a product carry multiple preview chapters in the future
// (DisplayOrder hints at this); v1's admin UI authors a single record per
// product but the model is already a collection so future expansion is purely
// additive — no schema change required.
public class ProductBookPreview : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Relative path under the protected file root — e.g. "book-previews/abc.pdf".
    /// Physical file lives in {ContentRoot}/secure-files/&lt;PdfRelativePath&gt; and is NEVER
    /// served by static-file middleware. The only read path is the signed-token
    /// controller at /bookpreview/view/{token} (see BookPreviewController).
    /// </summary>
    public string PdfRelativePath { get; set; } = string.Empty;

    /// <summary>Public-facing cover thumbnail (lives under /uploads — fine to expose).</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>Master switch — when false the preview is hidden from the public course page.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Optional client-side hint — admin sets this to cap the visible page count
    /// (e.g. "first 10 pages of the book only"). Enforced in the viewer UI; the
    /// signed token still streams the full file so this is presentation, not a security boundary.</summary>
    public int? MaxPagesAllowed { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Pre-rendered WebP pages of the source PDF (1 row per page).</summary>
    public ICollection<BookPreviewPage> Pages { get; set; } = new List<BookPreviewPage>();
}
