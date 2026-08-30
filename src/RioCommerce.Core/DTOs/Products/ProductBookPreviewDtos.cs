namespace RioCommerce.Core.DTOs.Products;

// Admin edit-form DTO — bound to the “Book Preview” tab on /admin/products/{id}.
// PdfRelativePath isn't surfaced to the form directly; instead the form shows
// the friendly file name and lets the admin replace via upload.
public class ProductBookPreviewEdit
{
    public Guid? Id { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    /// <summary>Set by the upload endpoint — the form treats it as read-only.</summary>
    public string? PdfRelativePath { get; set; }
    public int? MaxPagesAllowed { get; set; }
    public int DisplayOrder { get; set; }
}

// Public Course-detail page DTO — what the Blazor page sees once it has resolved
// the product. PdfRelativePath stays server-side; the page receives a fresh
// signed-token ViewerUrl that gates streaming through BookPreviewController.
public record ProductBookPreviewPublic(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    int? MaxPagesAllowed,
    /// <summary>Total number of pre-rendered WebP pages available for the viewer.</summary>
    int TotalPages);
