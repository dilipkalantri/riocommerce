using RioCommerce.Core.DTOs.Content;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// The ONE centralised abstraction for the global public-URL namespace. No other service checks
/// another entity's table for slug collisions — everything goes through here. Enforces
/// "one public URL = one owner" across Category, Product and any future public entity.
/// </summary>
public interface ISeoUrlService
{
    /// <summary>Canonicalise a raw slug/path: lower, trim, strip slashes, spaces→'-', collapse '-', drop unsafe chars.</summary>
    string Normalize(string? raw);

    /// <summary>True when the slug maps onto a reserved system/app route (admin, api, cart, …) and can never be owned.</summary>
    bool IsReserved(string? raw);

    /// <summary>Full availability check. Excludes the current owner (selfType+selfId) so an entity never conflicts with itself.
    /// When taken/reserved, fills in the owner details and the next free "-2/-3" suggestion.</summary>
    Task<GlobalUrlLookupResult> CheckAsync(string? rawSlug, string? selfType = null, Guid? selfId = null, CancellationToken ct = default);

    /// <summary>Next globally-available slug based on a desired base (base, base-2, base-3 …).</summary>
    Task<string> SuggestAsync(string? rawSlug, string? selfType = null, Guid? selfId = null, CancellationToken ct = default);

    /// <summary>Resolve a root-level request path to its owner, or null. Indexed, AsNoTracking — the hot path.</summary>
    Task<SeoUrlResolution?> ResolveAsync(string? rawPath, CancellationToken ct = default);

    /// <summary>Create/point the registry row for an owner at <paramref name="rawSlug"/>. When the owner already had a
    /// different slug, the old one is preserved as a 301 redirect. Returns (ok,error,normalizedSlug). Fails on a
    /// duplicate/reserved slug (also caught by the DB unique index for concurrency safety).</summary>
    Task<(bool ok, string? error, string normalizedSlug)> RegisterOrUpdateAsync(
        string entityType, Guid entityId, string? entityName, string? rawSlug, CancellationToken ct = default);

    /// <summary>Remove an owner's registry row (e.g. on delete). Its slug stays reserved via any redirect that was created.</summary>
    Task ReleaseAsync(string entityType, Guid entityId, CancellationToken ct = default);
}
