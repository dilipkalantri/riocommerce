namespace RioCommerce.Core.Entities;

/// <summary>
/// Global registry of root-level public URLs — the single source of truth that guarantees
/// ONE PUBLIC URL = ONE OWNER across the whole storefront. Every URL-owning public entity
/// (Category, Product, …) has exactly one row here; the root resolver reads it to map a
/// slug to its (EntityType, EntityId), and a DB UNIQUE index on NormalizedSlug makes a
/// duplicate physically impossible even under concurrent writes.
/// </summary>
public class SeoUrlRecord : BaseEntity
{
    /// <summary>The display slug as entered (already cleaned): e.g. "ca-foundation".</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>The canonical comparison key: lower-cased, trimmed, single-hyphen, no slashes.
    /// A UNIQUE index is enforced on this column.</summary>
    public string NormalizedSlug { get; set; } = string.Empty;

    /// <summary>Owner kind — "Category", "Product", … (stored as text so new types need no migration).</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Owner id in its own table.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Human-friendly owner name, for the admin duplicate-conflict message.</summary>
    public string? EntityName { get; set; }

    public bool IsActive { get; set; } = true;
}
