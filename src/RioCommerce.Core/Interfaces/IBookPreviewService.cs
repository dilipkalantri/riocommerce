using RioCommerce.Core.DTOs.Products;

namespace RioCommerce.Core.Interfaces;

// Application-level service that owns the Book Preview lifecycle.
//
// The router design — token issuance separated from streaming — means
// callers never see the physical file path. Tokens are signed via
// IDataProtectionProvider with a dedicated purpose; they self-expire after
// TokenLifetime, and validating them produces the relative path back so the
// streaming controller can resolve it against IProtectedFileStorage.
public interface IBookPreviewService
{
    static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    Task<ProductBookPreviewEdit?> GetForAdminAsync(Guid productId, CancellationToken ct = default);
    Task<(bool ok, string? error)> SaveAsync(Guid productId, ProductBookPreviewEdit model, CancellationToken ct = default);

    /// <summary>Returns the public-safe DTO for the currently active preview on a product, or null if none / disabled.</summary>
    Task<ProductBookPreviewPublic?> GetActiveAsync(Guid productId, CancellationToken ct = default);

    // ─── Image-based viewer ────────────────────────────────────────────────
    // The reader fetches one WebP page at a time through a signed-token URL.
    // The token carries the preview id + expiry; the page number lives in the
    // URL and is validated by Tr​yResolvePageImageAsync against the
    // BookPreviewPage table.

    /// <summary>Issues a short-lived signed token granting read access to all pre-rendered pages of the preview.</summary>
    Task<string?> IssueImageTokenAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Validates a token + page number combination and returns the relative path of that page's WebP. Null when expired / tampered / out-of-range.</summary>
    Task<string?> TryResolvePageImageAsync(string token, int pageNumber, CancellationToken ct = default);

    /// <summary>Save a freshly-uploaded PDF, pre-render every page to WebP, and replace any prior preview content.</summary>
    Task<(bool ok, string? error)> AttachPdfAsync(Guid productId, Stream pdf, string fileName, CancellationToken ct = default);
}
