namespace RioCommerce.Core.Entities;

// One pre-rendered WebP page belonging to a ProductBookPreview.
//
// On PDF upload BookPreviewService renders every page of the source PDF into a
// WebP image stored OUTSIDE wwwroot (see IProtectedFileStorage). One row per
// page is inserted here so the viewer can look up "image for preview X page N"
// without re-querying the file system, and so admin tooling can report
// page-count / dimensions without touching the originals.
public class BookPreviewPage : BaseEntity
{
    public Guid BookPreviewId { get; set; }
    public ProductBookPreview? BookPreview { get; set; }

    /// <summary>1-based — page 1 is rendered as the cover.</summary>
    public int PageNumber { get; set; }

    /// <summary>Relative path under the protected file root (e.g. "bookpreview-images/&lt;previewId&gt;/page-001.webp").</summary>
    public string ImageRelativePath { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
}
