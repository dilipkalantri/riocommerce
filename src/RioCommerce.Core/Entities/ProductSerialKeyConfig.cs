namespace RioCommerce.Core.Entities;

/// <summary>
/// Per-product serial-key configuration. Sits as a 1-to-many child of <see cref="Product"/>
/// so the same product can carry different config per provider if you ever switch over,
/// and so adding a 3rd provider doesn't churn the Product table.
///
/// <see cref="ConfigJson"/> is a <c>jsonb</c> blob — its shape depends on the provider:
/// for RioPlay it deserialises to <c>RioPlayProductConfig</c> (see provider DTOs).
/// Adding a new provider means defining a new typed POCO and updating its provider class;
/// schema stays untouched.
/// </summary>
public class ProductSerialKeyConfig : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>When set, this config is a PER-MODE override that applies only to the given
    /// <see cref="ProductMode"/> (matched against <c>OrderItem.ProductModeId</c> at key-generation time).
    /// When <c>null</c>, it is the PRODUCT-LEVEL config — the fallback used for any purchased mode that
    /// has no override of its own, and for mode-less products. Resolution order at enqueue is:
    /// mode-specific row → product-level row → no key.</summary>
    public Guid? ProductModeId { get; set; }

    /// <summary>Matches <c>ISerialKeyProvider.Key</c> — <c>"rioplay"</c>, <c>"valence"</c>.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>For RioPlay, FK to <see cref="RioPlayTenant"/>. Null = use the provider's default tenant.
    /// String to keep this entity provider-agnostic (Valence might key its tenancy differently).</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Provider-specific product code (Rio's Int32 product id). Stored as string for the same reason.</summary>
    public string? ProviderProductCode { get; set; }

    /// <summary>jsonb blob of provider-specific request defaults (validity, watch time, device count, etc).</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>If <c>true</c>, the provider's create+activate endpoint is used (Rio's <c>GenerateSerialKeyForNop</c>).
    /// If <c>false</c>, the create-only endpoint is used and the key starts in <c>Generated</c> state
    /// until the customer or admin hits the activate route.</summary>
    public bool AutoActivate { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public Product? Product { get; set; }

    /// <summary>Navigation to the specific mode this override targets (null for the product-level row).</summary>
    public ProductMode? ProductMode { get; set; }
}
