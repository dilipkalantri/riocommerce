using System.Diagnostics;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Application service backing the Book Preview feature.
///
/// New architecture (image-based reader):
///   • On PDF upload we render every page of the source PDF to a WebP image
///     using PDFium (via PDFtoImage) + SkiaSharp.
///   • Each page is stored OUTSIDE wwwroot under a per-preview folder, plus
///     one row in <see cref="BookPreviewPage"/> with width/height metadata.
///   • The public viewer never receives a PDF URL. It fetches one WebP at a
///     time through /api/bookpreview/image/{token}/{page} — the token grants
///     read access to all pages of the preview for 15 minutes.
///
/// Token format (issued via Data Protection):
///   "&lt;productId&gt;|&lt;previewId&gt;|&lt;expiryUnix&gt;"
/// The page number lives in the URL, not in the token, so a single token can
/// serve every page without re-issuing.
/// </summary>
public sealed class BookPreviewService : IBookPreviewService
{
    // Pages above this are clipped — a sensible upper bound that protects us from
    // someone uploading a 5000-page book and exhausting disk space.
    private const int MaxRenderedPages = 250;
    // Render width in CSS pixels (PDFium picks DPI to hit this width). 1200 looks
    // crisp on retina displays at modal sizes; ≈200-400 KB per page after WebP-80.
    private const int RenderTargetWidth = 1200;
    private const int WebpQuality = 80;

    private readonly RioCommerceDbContext _db;
    private readonly IProtectedFileStorage _files;
    private readonly IDataProtector _protector;
    private readonly ILogger<BookPreviewService> _log;

    // Defensive upper bound — if rendering ever wedges (native lib, locked file, etc.)
    // we want to surface a clear error instead of hanging the admin's upload button.
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromMinutes(2);

    public BookPreviewService(
        RioCommerceDbContext db,
        IProtectedFileStorage files,
        IDataProtectionProvider dp,
        ILogger<BookPreviewService> log)
    {
        _db = db;
        _files = files;
        _protector = dp.CreateProtector("RioCommerce.BookPreview.ViewToken.v2");
        _log = log;
    }

    // ─── Admin read / write ────────────────────────────────────────────────
    public async Task<ProductBookPreviewEdit?> GetForAdminAsync(Guid productId, CancellationToken ct = default)
    {
        var bp = await ActiveQuery(productId).FirstOrDefaultAsync(ct);
        if (bp == null) return null;
        return new ProductBookPreviewEdit
        {
            Id = bp.Id,
            IsEnabled = bp.IsEnabled,
            Title = bp.Title,
            Description = bp.Description,
            CoverImageUrl = bp.CoverImageUrl,
            PdfRelativePath = bp.PdfRelativePath,
            MaxPagesAllowed = bp.MaxPagesAllowed,
            DisplayOrder = bp.DisplayOrder,
        };
    }

    public async Task<(bool ok, string? error)> SaveAsync(Guid productId, ProductBookPreviewEdit model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.Title)) return (false, "Title is required.");

        var bp = await ActiveQuery(productId).FirstOrDefaultAsync(ct);
        if (bp == null)
        {
            bp = new ProductBookPreview { ProductId = productId };
            _db.ProductBookPreviews.Add(bp);
        }

        bp.IsEnabled = model.IsEnabled;
        bp.Title = model.Title.Trim();
        bp.Description = Trim(model.Description);
        bp.CoverImageUrl = Trim(model.CoverImageUrl);
        bp.MaxPagesAllowed = model.MaxPagesAllowed;
        bp.DisplayOrder = model.DisplayOrder;
        // PdfRelativePath is set ONLY by AttachPdfAsync — the metadata form can't replace bytes accidentally.

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    // ─── Public DTO ────────────────────────────────────────────────────────
    public async Task<ProductBookPreviewPublic?> GetActiveAsync(Guid productId, CancellationToken ct = default)
    {
        var bp = await ActiveQuery(productId)
            .Where(b => b.IsEnabled)
            .FirstOrDefaultAsync(ct);
        if (bp == null) return null;
        var pageCount = await _db.BookPreviewPages.CountAsync(p => p.BookPreviewId == bp.Id, ct);
        // Only surface the preview when there ARE rendered pages — otherwise the
        // viewer would open empty.
        if (pageCount == 0) return null;
        var visible = bp.MaxPagesAllowed is int cap && cap > 0 ? Math.Min(cap, pageCount) : pageCount;
        return new ProductBookPreviewPublic(bp.Id, bp.Title, bp.Description, bp.CoverImageUrl, bp.MaxPagesAllowed, visible);
    }

    // ─── Signed token issuance + page resolution ───────────────────────────
    public async Task<string?> IssueImageTokenAsync(Guid productId, CancellationToken ct = default)
    {
        var bp = await ActiveQuery(productId)
            .Where(b => b.IsEnabled)
            .FirstOrDefaultAsync(ct);
        if (bp == null) return null;
        var hasPages = await _db.BookPreviewPages.AnyAsync(p => p.BookPreviewId == bp.Id, ct);
        if (!hasPages) return null;

        var expiry = DateTimeOffset.UtcNow.Add(IBookPreviewService.TokenLifetime).ToUnixTimeSeconds();
        var payload = string.Join('|', productId, bp.Id, expiry);
        return _protector.Protect(payload);
    }

    public async Task<string?> TryResolvePageImageAsync(string token, int pageNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || pageNumber < 1) return null;
        string payload;
        try { payload = _protector.Unprotect(token); }
        catch { return null; }

        var parts = payload.Split('|', 3);
        if (parts.Length != 3) return null;
        if (!Guid.TryParse(parts[1], out var previewId)) return null;
        if (!long.TryParse(parts[2], out var expiryUnix)) return null;
        if (DateTimeOffset.FromUnixTimeSeconds(expiryUnix) <= DateTimeOffset.UtcNow) return null;

        // Enforce the admin's max-pages cap at the auth boundary so an attacker
        // can't iterate past it by guessing page numbers.
        var bp = await _db.ProductBookPreviews
            .AsNoTracking()
            .Where(b => b.Id == previewId)
            .Select(b => new { b.IsEnabled, b.MaxPagesAllowed })
            .FirstOrDefaultAsync(ct);
        if (bp == null || !bp.IsEnabled) return null;
        if (bp.MaxPagesAllowed is int cap && cap > 0 && pageNumber > cap) return null;

        var relPath = await _db.BookPreviewPages
            .Where(p => p.BookPreviewId == previewId && p.PageNumber == pageNumber)
            .Select(p => p.ImageRelativePath)
            .FirstOrDefaultAsync(ct);
        if (relPath == null) return null;

        // Defence-in-depth — the file must still live under the protected root.
        return _files.Resolve(relPath) == null ? null : relPath;
    }

    // ─── PDF upload + per-page WebP rendering ──────────────────────────────
    public async Task<(bool ok, string? error)> AttachPdfAsync(Guid productId, Stream pdf, string fileName, CancellationToken ct = default)
    {
        var overallSw = Stopwatch.StartNew();
        _log.LogInformation("📚 BookPreview · upload started · product={ProductId} · file={FileName}", productId, fileName);

        var ext = (Path.GetExtension(fileName) ?? "").Trim().ToLowerInvariant();
        if (ext != ".pdf")
        {
            _log.LogWarning("📚 BookPreview · upload rejected · not a PDF (ext='{Ext}')", ext);
            return (false, "Only PDF files are accepted.");
        }

        // Bring the PDF fully into memory once. We need to rewind for two consumers
        // (save-to-disk + PDFium renderer) and the upload stream may not be seekable.
        using var pdfBytes = new MemoryStream();
        try
        {
            await pdf.CopyToAsync(pdfBytes, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "📚 BookPreview · failed reading upload stream");
            return (false, "Could not read the uploaded PDF: " + ex.Message);
        }
        _log.LogInformation("📚 BookPreview · upload buffered · {Bytes} bytes", pdfBytes.Length);
        if (pdfBytes.Length == 0) return (false, "Uploaded PDF is empty.");

        // Ensure the parent row exists so we have an Id to scope the image folder under.
        var bp = await ActiveQuery(productId).FirstOrDefaultAsync(ct);
        if (bp == null)
        {
            bp = new ProductBookPreview { ProductId = productId, IsEnabled = true, Title = "Book Preview" };
            _db.ProductBookPreviews.Add(bp);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("📚 BookPreview · created new preview row {PreviewId}", bp.Id);
        }

        // 1) Persist the new PDF first so we have it on disk even if rendering fails halfway.
        var previousPdfPath = bp.PdfRelativePath;
        pdfBytes.Position = 0;
        bp.PdfRelativePath = await _files.SaveAsync("book-previews", ".pdf", pdfBytes, ct);
        _log.LogInformation("📚 BookPreview · new PDF saved → '{Path}'", bp.PdfRelativePath);

        // 2) Render every page to WebP under a watchdog timeout so a wedged native
        //    call can't hang the upload button forever. Runs on a background thread
        //    so the request thread isn't pinned by CPU-bound PDFium work.
        pdfBytes.Position = 0;
        List<BookPreviewPage> newPages;
        using var renderCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        renderCts.CancelAfter(RenderTimeout);
        try
        {
            var renderSw = Stopwatch.StartNew();
            newPages = await Task.Run(() => RenderPdfToWebPsCore(pdfBytes, bp.Id, renderCts.Token), renderCts.Token);
            _log.LogInformation("📚 BookPreview · render complete · {Count} page(s) in {Ms} ms", newPages.Count, renderSw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (renderCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _log.LogError("📚 BookPreview · render TIMED OUT after {Min} min — rolling back", RenderTimeout.TotalMinutes);
            SafeDelete(bp.PdfRelativePath);
            bp.PdfRelativePath = previousPdfPath ?? string.Empty;
            return (false, $"PDF rendering timed out after {RenderTimeout.TotalMinutes:0} minutes. Try a smaller PDF.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "📚 BookPreview · render FAILED — rolling back new PDF");
            SafeDelete(bp.PdfRelativePath);
            bp.PdfRelativePath = previousPdfPath ?? string.Empty;
            return (false, "PDF could not be rendered: " + ex.Message);
        }

        if (newPages.Count == 0)
        {
            _log.LogWarning("📚 BookPreview · render produced 0 pages — rolling back");
            SafeDelete(bp.PdfRelativePath);
            bp.PdfRelativePath = previousPdfPath ?? string.Empty;
            return (false, "PDF appears to have no pages.");
        }

        // 3) Swap the page set inside a transaction so a concurrent reader either
        //    sees the entire old set or the entire new one — never a half-replaced view.
        var previousPages = await _db.BookPreviewPages
            .Where(p => p.BookPreviewId == bp.Id).ToListAsync(ct);
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            _db.BookPreviewPages.RemoveRange(previousPages);
            _db.BookPreviewPages.AddRange(newPages);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        _log.LogInformation("📚 BookPreview · DB swap committed · removed {Old} · added {New}",
            previousPages.Count, newPages.Count);

        // 4) Clean up the OLD PDF + OLD images now that the new content is durable.
        //    Done after the transaction so a delete failure can't roll back the swap.
        if (!string.IsNullOrEmpty(previousPdfPath) && previousPdfPath != bp.PdfRelativePath)
            SafeDelete(previousPdfPath);
        var cleanupCount = 0;
        foreach (var p in previousPages)
        {
            if (SafeDelete(p.ImageRelativePath)) cleanupCount++;
        }
        _log.LogInformation("📚 BookPreview · cleanup done · {N} old image(s) removed · total elapsed {Ms} ms",
            cleanupCount, overallSw.ElapsedMilliseconds);

        return (true, null);
    }

    /// <summary>
    /// PDFium → SkiaSharp → WebP. One page at a time so peak memory stays bounded.
    /// Runs synchronously inside Task.Run — the SaveAsync block-on-Wait inside this
    /// loop is safe because we're already on a thread-pool worker that's not the
    /// request thread, so there's no SynchronizationContext to deadlock against.
    /// </summary>
    private List<BookPreviewPage> RenderPdfToWebPsCore(Stream pdfBytes, Guid previewId, CancellationToken ct)
    {
        var folder = $"bookpreview-images_{previewId:N}";
        var renderOptions = new RenderOptions(Width: RenderTargetWidth);
        var result = new List<BookPreviewPage>();
        var pageNumber = 1;

        _log.LogInformation("📚 BookPreview · rendering started · folder='{Folder}'", folder);

        // CA1416: the analyzer flags this as platform-specific, but the platforms it lists (Windows,
        // Linux, macOS, …) already cover everything this service is ever hosted on — the app ships to
        // Windows in development and Linux in production. Suppressed rather than guarded, because a
        // runtime OS check here could only ever take the true branch.
#pragma warning disable CA1416
        foreach (var bitmap in Conversion.ToImages(pdfBytes, leaveOpen: true, password: null, options: renderOptions))
#pragma warning restore CA1416
        {
            ct.ThrowIfCancellationRequested();
            var pageSw = Stopwatch.StartNew();
            using (bitmap)
            {
                using var img = SKImage.FromBitmap(bitmap);
                using var data = img.Encode(SKEncodedImageFormat.Webp, WebpQuality);
                using var webpStream = new MemoryStream();
                data.SaveTo(webpStream);
                webpStream.Position = 0;

                // Synchronous Wait() is fine here — we're on a Task.Run thread, no SyncContext.
                var saveTask = _files.SaveAsync(folder, ".webp", webpStream, ct);
                saveTask.Wait(ct);
                var relPath = saveTask.Result;

                result.Add(new BookPreviewPage
                {
                    BookPreviewId = previewId,
                    PageNumber = pageNumber,
                    ImageRelativePath = relPath,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                });
            }
            _log.LogInformation("📚 BookPreview · page {N} rendered in {Ms} ms · {W}x{H}",
                pageNumber, pageSw.ElapsedMilliseconds, result[^1].Width, result[^1].Height);

            if (pageNumber >= MaxRenderedPages)
            {
                _log.LogWarning("📚 BookPreview · capped at {Max} pages — remaining pages skipped", MaxRenderedPages);
                break;
            }
            pageNumber++;
        }

        return result;
    }

    /// <summary>Catch + log so a stale lock / missing file can't take down the upload.</summary>
    private bool SafeDelete(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;
        try { _files.Delete(relativePath); return true; }
        catch (Exception ex) { _log.LogWarning(ex, "📚 BookPreview · could not delete '{Path}'", relativePath); return false; }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────
    private IQueryable<ProductBookPreview> ActiveQuery(Guid productId)
        => _db.ProductBookPreviews
              .Where(b => b.ProductId == productId)
              .OrderBy(b => b.DisplayOrder)
              .ThenBy(b => b.CreatedAt);

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
