using System.Text.RegularExpressions;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// The single owner of the global public-URL namespace. Guarantees "one public URL = one owner"
/// via a normalized-slug registry (seo_url_records) backed by a DB UNIQUE index, and preserves old
/// URLs as 301 redirects (url_redirects) when a slug changes.
/// </summary>
public class SeoUrlService : ISeoUrlService
{
    private readonly RioCommerceDbContext _db;
    public SeoUrlService(RioCommerceDbContext db) => _db = db;

    // Reserved single-segment roots — real app/system routes + the legacy entity-type prefixes.
    // NOTE: "courses" is intentionally NOT reserved (an existing category legitimately owns it).
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin","api","_blazor","_framework","_content","signalr","bookpreview",
        "account","login","register","logout","forgot-password","access-denied","auth",
        "cart","checkout","order","orders","payment","r",
        "blog","contact","faculty","search","wishlist","franchise","franchisee",
        "page","preview","home","category","course","product","products",
        "uploads","images","img","css","js","assets","lib","fonts","media",
        "robots.txt","sitemap.xml","favicon.ico","manifest.json"
    };

    public string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var s = raw.Trim().ToLowerInvariant();
        // If a full URL slipped in, keep only the path.
        if (Uri.TryCreate(s, UriKind.Absolute, out var abs)) s = abs.AbsolutePath;
        s = s.Trim().Trim('/');
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"[^a-z0-9\-/]", "");
        s = Regex.Replace(s, @"-+", "-");
        s = Regex.Replace(s, @"/+", "/");
        return s.Trim('-').Trim('/');
    }

    private static string FirstSegment(string normalized)
    {
        var i = normalized.IndexOf('/');
        return i < 0 ? normalized : normalized[..i];
    }

    public bool IsReserved(string? raw)
    {
        var n = Normalize(raw);
        if (n.Length == 0) return true;                 // the bare root "/" can't be owned
        return Reserved.Contains(FirstSegment(n));
    }

    public async Task<GlobalUrlLookupResult> CheckAsync(string? rawSlug, string? selfType = null, Guid? selfId = null, CancellationToken ct = default)
    {
        var n = Normalize(rawSlug);
        if (n.Length == 0)
            return new GlobalUrlLookupResult(false, true, n, SuggestedSlug: null);
        if (IsReserved(n))
            return new GlobalUrlLookupResult(false, true, n, "System", null, "Reserved system route",
                SuggestedSlug: await SuggestAsync(n, selfType, selfId, ct));

        // 1) live owner in the registry
        var owner = await _db.SeoUrlRecords.AsNoTracking().FirstOrDefaultAsync(x => x.NormalizedSlug == n, ct);
        if (owner != null && !(owner.EntityType == selfType && owner.EntityId == selfId))
            return new GlobalUrlLookupResult(false, false, n, owner.EntityType, owner.EntityId, owner.EntityName,
                "/" + n, AdminEditUrl(owner.EntityType, owner.EntityId),
                await SuggestAsync(n, selfType, selfId, ct));

        // 2) owned by an active redirect source
        var redirect = await _db.UrlRedirects.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive && x.OldUrl == "/" + n, ct);
        if (redirect != null)
            return new GlobalUrlLookupResult(false, false, n, SeoEntityTypes.Redirect, redirect.Id,
                $"Redirect → {redirect.NewUrl}", "/" + n, "/admin/url-redirects",
                await SuggestAsync(n, selfType, selfId, ct));

        return new GlobalUrlLookupResult(true, false, n);
    }

    public async Task<string> SuggestAsync(string? rawSlug, string? selfType = null, Guid? selfId = null, CancellationToken ct = default)
    {
        var n = Normalize(rawSlug);
        if (n.Length == 0) n = "page";
        // Base = slug without a trailing "-<number>" so "course-4" → "course".
        var m = Regex.Match(n, @"^(.*?)-(\d+)$");
        var baseSlug = m.Success ? m.Groups[1].Value : n;
        if (baseSlug.Length == 0) baseSlug = n;

        // Load everything already taken that starts with the base (registry + redirects) in one pass.
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in await _db.SeoUrlRecords.AsNoTracking()
                     .Where(x => x.NormalizedSlug == baseSlug || x.NormalizedSlug.StartsWith(baseSlug + "-"))
                     .Where(x => selfType == null || !(x.EntityType == selfType && x.EntityId == selfId))
                     .Select(x => x.NormalizedSlug).ToListAsync(ct))
            taken.Add(s);
        foreach (var o in await _db.UrlRedirects.AsNoTracking().Where(x => x.IsActive)
                     .Where(x => x.OldUrl == "/" + baseSlug || x.OldUrl.StartsWith("/" + baseSlug + "-"))
                     .Select(x => x.OldUrl).ToListAsync(ct))
            taken.Add(o.TrimStart('/'));

        bool Free(string cand) => !taken.Contains(cand) && !Reserved.Contains(FirstSegment(cand));
        if (Free(baseSlug)) return baseSlug;
        for (var i = 2; i < 1000; i++)
        {
            var cand = $"{baseSlug}-{i}";
            if (Free(cand)) return cand;
        }
        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(60, baseSlug.Length + 33)];
    }

    public async Task<SeoUrlResolution?> ResolveAsync(string? rawPath, CancellationToken ct = default)
    {
        var n = Normalize(rawPath);
        if (n.Length == 0) return null;
        return await _db.SeoUrlRecords.AsNoTracking()
            .Where(x => x.IsActive && x.NormalizedSlug == n)
            .Select(x => new SeoUrlResolution(x.EntityType, x.EntityId, x.NormalizedSlug))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(bool ok, string? error, string normalizedSlug)> RegisterOrUpdateAsync(
        string entityType, Guid entityId, string? entityName, string? rawSlug, CancellationToken ct = default)
    {
        var n = Normalize(rawSlug);
        if (n.Length == 0) return (false, "URL slug is empty.", n);
        if (IsReserved(n)) return (false, $"“/{n}” is reserved by the system and cannot be used.", n);

        // Reject if another owner (or an active redirect) already holds this URL.
        var clash = await _db.SeoUrlRecords.FirstOrDefaultAsync(
            x => x.NormalizedSlug == n && !(x.EntityType == entityType && x.EntityId == entityId), ct);
        if (clash != null)
            return (false, $"“/{n}” is already used by {clash.EntityType}: {clash.EntityName}.", n);
        if (await _db.UrlRedirects.AnyAsync(x => x.IsActive && x.OldUrl == "/" + n, ct))
            return (false, $"“/{n}” is already reserved by a redirect.", n);

        var existing = await _db.SeoUrlRecords.FirstOrDefaultAsync(
            x => x.EntityType == entityType && x.EntityId == entityId, ct);

        try
        {
            if (existing == null)
            {
                _db.SeoUrlRecords.Add(new SeoUrlRecord
                {
                    Slug = n, NormalizedSlug = n, EntityType = entityType, EntityId = entityId,
                    EntityName = entityName, IsActive = true
                });
            }
            else if (existing.NormalizedSlug != n)
            {
                var old = existing.NormalizedSlug;
                // Preserve the old URL as a 301, collapsing any chains (A→B, now B→C ⇒ A→C, B→C).
                await _db.UrlRedirects.Where(r => r.NewUrl == "/" + old)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.NewUrl, "/" + n), ct);
                if (!await _db.UrlRedirects.AnyAsync(r => r.OldUrl == "/" + old, ct))
                    _db.UrlRedirects.Add(new UrlRedirect { OldUrl = "/" + old, NewUrl = "/" + n, RedirectType = 301, IsActive = true, Notes = "Auto: slug changed" });
                existing.Slug = n; existing.NormalizedSlug = n; existing.EntityName = entityName; existing.IsActive = true;
            }
            else
            {
                existing.EntityName = entityName; existing.IsActive = true;   // same slug — just refresh metadata
            }
            await _db.SaveChangesAsync(ct);
            return (true, null, n);
        }
        catch (DbUpdateException)
        {
            // Concurrency: the DB unique index caught a race. Surface a clean conflict, not a raw error.
            _db.ChangeTracker.Clear();
            return (false, $"“/{n}” was just taken by another page. Please choose a different URL.", n);
        }
    }

    public async Task ReleaseAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        await _db.SeoUrlRecords.Where(x => x.EntityType == entityType && x.EntityId == entityId)
            .ExecuteDeleteAsync(ct);
    }

    private static string? AdminEditUrl(string entityType, Guid entityId) => entityType switch
    {
        SeoEntityTypes.Category => $"/admin/categories/{entityId}",
        SeoEntityTypes.Product => $"/admin/products/{entityId}",
        SeoEntityTypes.BlogPost => "/admin/blog",
        SeoEntityTypes.CmsPage => $"/admin/pages/{entityId}",
        SeoEntityTypes.Faculty => $"/admin/faculty",
        _ => null
    };
}
