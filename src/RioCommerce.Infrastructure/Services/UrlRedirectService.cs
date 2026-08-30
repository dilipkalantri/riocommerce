using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public sealed class UrlRedirectService : IUrlRedirectService
{
    private readonly RioCommerceDbContext _db;
    public UrlRedirectService(RioCommerceDbContext db) => _db = db;

    // ─── Admin grid ────────────────────────────────────────────────────────
    public async Task<UrlRedirectPage> ListAsync(UrlRedirectFilter filter, CancellationToken ct = default)
    {
        var q = _db.UrlRedirects.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(r => r.OldUrl.Contains(s) || r.NewUrl.Contains(s));
        }
        if (filter.IsActive is { } active) q = q.Where(r => r.IsActive == active);
        if (filter.RedirectType is { } rt) q = q.Where(r => r.RedirectType == rt);

        var total = await q.CountAsync(ct);
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 200);

        var rows = await q
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new UrlRedirectRow(r.Id, r.OldUrl, r.NewUrl, r.RedirectType, r.IsActive, r.HitCount, r.LastHitAt, r.CreatedAt))
            .ToListAsync(ct);
        return new UrlRedirectPage(rows, total, page, size);
    }

    public async Task<UrlRedirectEdit?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.UrlRedirects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null) return null;
        return new UrlRedirectEdit
        {
            Id = r.Id, OldUrl = r.OldUrl, NewUrl = r.NewUrl,
            RedirectType = r.RedirectType, IsActive = r.IsActive, Notes = r.Notes,
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(UrlRedirectEdit edit, CancellationToken ct = default)
    {
        var (ok, err, oldNorm, newNorm) = Normalise(edit);
        if (!ok) return (false, err, Guid.Empty);

        var existing = await _db.UrlRedirects
            .Where(r => r.OldUrl == oldNorm && r.Id != (edit.Id ?? Guid.Empty))
            .AnyAsync(ct);
        if (existing) return (false, $"Another redirect already exists for '{oldNorm}'.", Guid.Empty);

        UrlRedirect entity;
        if (edit.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.UrlRedirects.FirstOrDefaultAsync(r => r.Id == id, ct)
                  ?? throw new InvalidOperationException("Redirect not found.");
        }
        else
        {
            entity = new UrlRedirect();
            _db.UrlRedirects.Add(entity);
        }

        entity.OldUrl = oldNorm;
        entity.NewUrl = newNorm;
        entity.RedirectType = edit.RedirectType is 301 or 302 ? edit.RedirectType : 301;
        entity.IsActive = edit.IsActive;
        entity.Notes = string.IsNullOrWhiteSpace(edit.Notes) ? null : edit.Notes.Trim();
        await _db.SaveChangesAsync(ct);
        return (true, null, entity.Id);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.UrlRedirects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null) return false;
        _db.UrlRedirects.Remove(r);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ToggleAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.UrlRedirects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null) return;
        r.IsActive = !r.IsActive;
        await _db.SaveChangesAsync(ct);
    }

    // ─── Middleware lookup (hot path) ──────────────────────────────────────
    public Task<UrlRedirect?> FindActiveAsync(string path, CancellationToken ct = default)
    {
        var norm = NormalisePath(path);
        return _db.UrlRedirects.AsNoTracking()
            .Where(r => r.IsActive && r.OldUrl == norm)
            .FirstOrDefaultAsync(ct);
    }

    public async Task IncrementHitAsync(Guid id, CancellationToken ct = default)
    {
        // Single-shot UPDATE with no Select — avoids change-tracker overhead on the hot path.
        await _db.UrlRedirects.Where(r => r.Id == id)
            .ExecuteUpdateAsync(u => u
                .SetProperty(r => r.HitCount, r => r.HitCount + 1)
                .SetProperty(r => r.LastHitAt, _ => DateTime.UtcNow), ct);
    }

    public async Task<(int Total, int Active, int Hits, DateTime? LastHit)> StatsAsync(CancellationToken ct = default)
    {
        var total  = await _db.UrlRedirects.CountAsync(ct);
        var active = await _db.UrlRedirects.CountAsync(r => r.IsActive, ct);
        var hits   = await _db.UrlRedirects.SumAsync(r => (int?)r.HitCount, ct) ?? 0;
        var lastHit = await _db.UrlRedirects.Where(r => r.LastHitAt != null)
                       .OrderByDescending(r => r.LastHitAt).Select(r => r.LastHitAt).FirstOrDefaultAsync(ct);
        return (total, active, hits, lastHit);
    }

    public async Task<(int inserted, int skipped)> BulkInsertAsync(IEnumerable<UrlRedirectEdit> rows, CancellationToken ct = default)
    {
        var existing = await _db.UrlRedirects.Select(r => r.OldUrl).ToListAsync(ct);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var inserted = 0; var skipped = 0;
        foreach (var row in rows)
        {
            var (ok, _, oldNorm, newNorm) = Normalise(row);
            if (!ok || seen.Contains(oldNorm)) { skipped++; continue; }
            seen.Add(oldNorm);
            _db.UrlRedirects.Add(new UrlRedirect
            {
                OldUrl = oldNorm,
                NewUrl = newNorm,
                RedirectType = row.RedirectType is 301 or 302 ? row.RedirectType : 301,
                IsActive = row.IsActive,
                Notes = string.IsNullOrWhiteSpace(row.Notes) ? "Imported from sitemap" : row.Notes.Trim(),
            });
            inserted++;
        }
        await _db.SaveChangesAsync(ct);
        return (inserted, skipped);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────
    private static (bool ok, string? error, string oldNorm, string newNorm) Normalise(UrlRedirectEdit edit)
    {
        var oldNorm = NormalisePath(edit.OldUrl);
        var newNorm = (edit.NewUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(oldNorm)) return (false, "Old URL is required.", "", "");
        if (string.IsNullOrEmpty(newNorm)) return (false, "Destination URL is required.", "", "");
        // Reject self-redirects: a rule mapping a path to itself (or to a URL whose
        // path equals the source) produces ERR_TOO_MANY_REDIRECTS. We catch both
        // OldUrl == NewUrl directly and the absolute-URL case where the destination's
        // AbsolutePath equals the normalised source.
        var newPath = NormalisePath(newNorm);
        if (string.Equals(oldNorm, newPath, StringComparison.OrdinalIgnoreCase))
            return (false, $"Source and destination resolve to the same path ('{oldNorm}') — this would loop.", "", "");
        return (true, null, oldNorm, newNorm);
    }

    public static string NormalisePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim().ToLowerInvariant();
        // Strip protocol + host if present so admins can paste absolute URLs.
        if (Uri.TryCreate(s, UriKind.Absolute, out var abs)) s = abs.AbsolutePath;
        if (!s.StartsWith('/')) s = "/" + s;
        // Strip trailing slash except for root.
        if (s.Length > 1 && s.EndsWith('/')) s = s.TrimEnd('/');
        // Strip query / fragment — we match path only.
        var q = s.IndexOf('?'); if (q >= 0) s = s[..q];
        var h = s.IndexOf('#'); if (h >= 0) s = s[..h];
        return s;
    }
}
