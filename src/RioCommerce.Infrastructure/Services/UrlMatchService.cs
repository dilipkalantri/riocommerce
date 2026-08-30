using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Matches a legacy URL path against current routes.
///
/// Resolution order — first hit wins:
///   1. Exact product slug → /course/{slug}
///   2. Exact faculty short-code (lower-cased) → /faculty/{ShortCode}
///   3. Exact category slug → /courses?category={slug}
///   4. Known static page mapping
///   5. SEO landing-slug heuristic — partial token match (e.g. "best-ca-foundation-classes-in-pune" → /courses?level=ca-foundation)
///   6. No match → "/"
///
/// The PartialMatch label is used by the migration tool to group these for admin review.
/// </summary>
public sealed class UrlMatchService : IUrlMatchService
{
    private readonly RioCommerceDbContext _db;
    public UrlMatchService(RioCommerceDbContext db) => _db = db;

    private static readonly Dictionary<string, string> _staticMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["about-us"]          = "/about",
        ["about"]             = "/about",
        ["contactus"]         = "/contact",
        ["contact-us"]        = "/contact",
        ["contact"]           = "/contact",
        ["privacy-notice"]    = "/privacy-policy",
        ["privacy"]           = "/privacy-policy",
        ["conditions-of-use"] = "/terms",
        ["terms"]             = "/terms",
        ["shipping-returns"]  = "/refund-policy",
        ["refund-policy"]     = "/refund-policy",
        ["blogs"]             = "/blog",
        ["blog"]              = "/blog",
        ["courses"]           = "/courses",
        ["test-series"]       = "/courses?type=test-series",
        ["book"]              = "/courses?type=books",
        ["books"]             = "/courses?type=books",
        ["air-talks"]         = "/air-talks",
        ["download-app"]      = "/download-app",
        ["ca-foundation"]     = "/courses?level=ca-foundation",
        ["ca-foundations"]    = "/courses?level=ca-foundation",
        ["ca-inter"]          = "/courses?level=ca-intermediate",
        ["ca-intermediate"]   = "/courses?level=ca-intermediate",
        ["vendor/apply"]      = "/franchise/apply",
        ["manufacturer/all"]  = "/faculty",
    };

    public async Task<(string NewUrl, UrlMatchStatus Status, string Reason)> MatchAsync(string oldPath, CancellationToken ct = default)
    {
        var norm = UrlRedirectService.NormalisePath(oldPath);
        if (string.IsNullOrEmpty(norm) || norm == "/")
            return ("/", UrlMatchStatus.ExactMatch, "Root URL");

        var slug = norm.TrimStart('/');

        // 1. Product slug
        if (await _db.Products.AnyAsync(p => p.Slug == slug && p.Status == ProductStatus.Active, ct))
            return ($"/course/{slug}", UrlMatchStatus.ExactMatch, "Active product slug");

        // 2. Faculty short code (case-insensitive)
        var facultyCode = await _db.Faculty
            .Where(f => f.IsActive && f.ShortCode.ToLower() == slug)
            .Select(f => f.ShortCode)
            .FirstOrDefaultAsync(ct);
        if (facultyCode != null)
            return ($"/faculty/{facultyCode}", UrlMatchStatus.ExactMatch, "Faculty short code");

        // 3. Category slug
        if (await _db.Categories.AnyAsync(c => c.Slug == slug, ct))
            return ($"/courses?category={slug}", UrlMatchStatus.ExactMatch, "Category slug");

        // 4. Static page map
        if (_staticMap.TryGetValue(slug, out var mapped))
            return (mapped, UrlMatchStatus.ExactMatch, "Known static page");

        // 5. SEO landing slug heuristic
        if (slug.Contains("ca-foundation"))   return ("/courses?level=ca-foundation",   UrlMatchStatus.PartialMatch, "Contains 'ca-foundation'");
        if (slug.Contains("ca-inter") || slug.Contains("ca-intermediate"))
            return ("/courses?level=ca-intermediate", UrlMatchStatus.PartialMatch, "Contains 'ca-inter'");
        if (slug.Contains("test-series"))     return ("/courses?type=test-series",      UrlMatchStatus.PartialMatch, "Contains 'test-series'");
        if (slug.Contains("books") || slug.Contains("book-set"))
            return ("/courses?type=books", UrlMatchStatus.PartialMatch, "Contains 'book'");
        if (slug.Contains("franchise") || slug.Contains("vendor")) return ("/franchise/apply", UrlMatchStatus.PartialMatch, "Contains 'franchise/vendor'");
        if (slug.Contains("contact"))         return ("/contact",                       UrlMatchStatus.PartialMatch, "Contains 'contact'");
        if (slug.Contains("about"))           return ("/about",                         UrlMatchStatus.PartialMatch, "Contains 'about'");

        // 6. No match → home
        return ("/", UrlMatchStatus.NoMatch, "No matching slug or token — falling back to home page");
    }
}
