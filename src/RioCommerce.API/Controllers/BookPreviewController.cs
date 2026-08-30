using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

// Streams Book Preview WebP images on a short-lived signed token.
//
// /api/bookpreview/image/{token}/{page} is what the reader fetches one page at
// a time. The token (issued by IBookPreviewService.IssueImageTokenAsync) grants
// access to ALL pages of one preview for 15 minutes; the page number lives in
// the URL and is validated against the BookPreviewPage table before any disk
// read happens.
//
// No PDF endpoint is exposed anywhere. The original PDF lives outside wwwroot
// and is only read by BookPreviewService during the upload-time rendering pass.
[ApiController]
[Route("api/bookpreview")]
public class BookPreviewController : ControllerBase
{
    private readonly IBookPreviewService _previews;
    private readonly IProtectedFileStorage _files;
    private readonly ILogger<BookPreviewController> _log;

    public BookPreviewController(
        IBookPreviewService previews,
        IProtectedFileStorage files,
        ILogger<BookPreviewController> log)
    {
        _previews = previews;
        _files = files;
        _log = log;
    }

    [HttpGet("image/{token}/{page:int}")]
    public async Task<IActionResult> Image(string token, int page, CancellationToken ct)
    {
        var tokenPreview = token is { Length: > 12 } ? token[..12] + "…" : token ?? "(null)";

        // Model binding hands back null for a missing ?token=, despite the non-nullable signature.
        // Reject it here rather than passing null into a method that does not accept one.
        if (string.IsNullOrWhiteSpace(token)) return NotFound();

        var relativePath = await _previews.TryResolvePageImageAsync(token, page, ct);
        if (relativePath == null)
        {
            _log.LogWarning("📚 BookPreview · image · token {Token} · page {Page} → unauthorized / not-found", tokenPreview, page);
            return NotFound();
        }

        var physical = _files.Resolve(relativePath);
        if (physical == null)
        {
            _log.LogWarning("📚 BookPreview · image · relativePath '{Path}' resolved to null", relativePath);
            return NotFound();
        }

        var fi = new FileInfo(physical);
        if (!fi.Exists || fi.Length == 0) return NotFound();

        // No-store + cookie-free + sniff-free + referrer-stripped. Each WebP is
        // small (~100-400 KB) so disabling cache doesn't hurt UX measurably while
        // it stops the file from leaking into HTTP proxies.
        Response.Headers["Cache-Control"]            = "no-store, no-cache, must-revalidate, private";
        Response.Headers["Pragma"]                   = "no-cache";
        Response.Headers["X-Content-Type-Options"]   = "nosniff";
        Response.Headers["Referrer-Policy"]          = "no-referrer";

        var stream = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return File(stream, "image/webp");
    }
}
