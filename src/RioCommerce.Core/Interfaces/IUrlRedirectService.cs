using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// CRUD + middleware-friendly lookup for the url_redirects table. Used by the
/// admin grid (paged list + add/edit/delete) AND by UrlRedirectMiddleware on
/// every incoming request that isn't an admin / api / static asset.
/// </summary>
public interface IUrlRedirectService
{
    Task<UrlRedirectPage> ListAsync(UrlRedirectFilter filter, CancellationToken ct = default);
    Task<UrlRedirectEdit?> GetAsync(Guid id, CancellationToken ct = default);
    Task<(bool ok, string? error, Guid id)> SaveAsync(UrlRedirectEdit edit, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task ToggleAsync(Guid id, CancellationToken ct = default);

    /// <summary>Middleware hot-path. Returns the active redirect for the path, or null.</summary>
    Task<UrlRedirect?> FindActiveAsync(string path, CancellationToken ct = default);

    /// <summary>Fire-and-forget hit counter increment.</summary>
    Task IncrementHitAsync(Guid id, CancellationToken ct = default);

    Task<(int Total, int Active, int Hits, DateTime? LastHit)> StatsAsync(CancellationToken ct = default);

    /// <summary>Bulk insert from the sitemap migration tool — skips rows whose OldUrl already exists.</summary>
    Task<(int inserted, int skipped)> BulkInsertAsync(IEnumerable<UrlRedirectEdit> rows, CancellationToken ct = default);
}

/// <summary>
/// Matches a legacy URL path against current routes (products / faculty /
/// categories / known static pages / SEO landing slug heuristics). Returns the
/// best-guess new URL and a confidence label so the migration tool can group
/// rows in the review grid.
/// </summary>
public interface IUrlMatchService
{
    Task<(string NewUrl, UrlMatchStatus Status, string Reason)> MatchAsync(string oldPath, CancellationToken ct = default);
}
