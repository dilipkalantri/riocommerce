namespace RioCommerce.Core.DTOs.Admin;

public sealed class UrlRedirectFilter
{
    public string? Search { get; set; }   // matches OldUrl OR NewUrl substring
    public bool? IsActive { get; set; }
    public int? RedirectType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed record UrlRedirectRow(
    Guid Id,
    string OldUrl,
    string NewUrl,
    int RedirectType,
    bool IsActive,
    int HitCount,
    DateTime? LastHitAt,
    DateTime CreatedAt);

public sealed record UrlRedirectPage(IReadOnlyList<UrlRedirectRow> Items, int TotalCount, int Page, int PageSize);

public sealed class UrlRedirectEdit
{
    public Guid? Id { get; set; }
    public string OldUrl { get; set; } = string.Empty;
    public string NewUrl { get; set; } = string.Empty;
    public int RedirectType { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}

public enum UrlMatchStatus
{
    ExactMatch,   // hit an existing product / faculty / category / static-page slug
    PartialMatch, // SEO landing slug that contains a known token (e.g. "best-ca-foundation-classes-in-pune")
    NoMatch,      // nothing found — falls back to "/"
}

public sealed record SitemapMigrationRow(
    int Index,
    string OldUrl,
    string SuggestedNewUrl,
    UrlMatchStatus Status,
    string Reason,
    bool Selected);

public sealed record SitemapMigrationPreview(IReadOnlyList<SitemapMigrationRow> Rows, int ExactCount, int PartialCount, int NoMatchCount);
