using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;

namespace RioCommerce.Core.Interfaces;

/// <summary>Resolves an <see cref="ISerialKeyProvider"/> by its <c>Key</c>. Also lists all
/// registered providers for the admin product-edit dropdown — data-driven, so adding a
/// provider auto-populates the UI without any view changes.</summary>
public interface ISerialKeyProviderFactory
{
    /// <summary>Throws <see cref="InvalidOperationException"/> when the key isn't registered.
    /// Use <see cref="TryGet"/> if you want a soft lookup.</summary>
    ISerialKeyProvider Get(string providerKey);

    bool TryGet(string providerKey, out ISerialKeyProvider provider);

    IReadOnlyList<ISerialKeyProvider> All();
}

/// <summary>
/// The only thing business logic talks to. Hooks into <c>CheckoutService.CompleteOrderAsync</c>
/// via <see cref="EnqueueForOrderAsync"/>, exposes manual operations for the admin UI, and
/// owns the dispatch loop called by <c>SerialKeyRetryTask</c>.
/// </summary>
public interface ISerialKeyService
{
    /// <summary>Idempotent. Walks an order's items, finds the ones whose product has serial-key
    /// config attached, and inserts one <see cref="SerialKeyRecord"/> per item in <c>Pending</c>.
    /// Safe to call multiple times — items already enqueued are skipped.</summary>
    Task<int> EnqueueForOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Process all pending / failed records whose <c>NextRetryAt</c> has come due.
    /// Returns counts for the scheduled-task summary.</summary>
    Task<(int processed, int generated, int failed, int deadLettered)> ProcessDueAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Force a single record to be retried now. Resets <c>AttemptCount</c>.
    /// Used by the admin "Regenerate" button and by manual support tickets.</summary>
    Task<bool> RegenerateAsync(Guid recordId, Guid? actorId, CancellationToken ct = default);

    /// <summary>
    /// Send the "your serial key is ready" email again for every issued key on an order.
    ///
    /// <para>No provider is called and no key changes — this only re-sends what the customer was
    /// already given, for the common case where the original mail was lost, filtered, or sent to an
    /// address that has since been corrected.</para>
    ///
    /// <para>Only records that actually hold a key are sent: pending, failed and revoked records are
    /// skipped, as are registration-style providers (Superclass), which mail the customer themselves
    /// and have no key of ours to quote. <paramref name="recordId"/> narrows it to one key; null
    /// sends every eligible key on the order.</para>
    /// </summary>
    Task<(bool ok, string? error, int sent, string? sentTo)> ResendKeyEmailAsync(
        Guid orderId, Guid? recordId, Guid? actorId, CancellationToken ct = default);

    /// <summary>Look up a key in our own DB — no provider call. Returns null if the key isn't ours.</summary>
    Task<SerialKeyDetail?> ValidateAsync(string serialKey, CancellationToken ct = default);

    /// <summary>Calls the provider's activate endpoint and transitions the record's status.</summary>
    Task<ActivateKeyResult> ActivateAsync(string serialKey, CancellationToken ct = default);

    /// <summary>Returns provider-side status when the provider supports it; otherwise our local status.</summary>
    Task<KeyStatusResult> GetStatusAsync(string serialKey, CancellationToken ct = default);

    Task<bool> RevokeAsync(Guid recordId, string reason, Guid? actorId, CancellationToken ct = default);

    // ── Admin listing ───────────────────────────────────────────────────────
    Task<List<SerialKeyListItem>> ListAsync(SerialKeyListFilter filter, CancellationToken ct = default);
    Task<SerialKeyDetail?> GetAsync(Guid recordId, CancellationToken ct = default);

    // ── Provider catalogue (drives the admin dropdown) ──────────────────────
    Task<List<ProviderInfo>> ListProvidersAsync(CancellationToken ct = default);

    // ── Per-product / per-mode config (Product Edit page) ───────────────────
    /// <summary>Returns the active config for a product (product-level when <paramref name="productModeId"/>
    /// is null, or the per-mode override when it is set), or a blank model with sensible defaults if none
    /// exists yet. Never returns null — admin UI binds straight to it.</summary>
    Task<ProductSerialKeyConfigItem> GetProductConfigAsync(Guid productId, Guid? productModeId = null, CancellationToken ct = default);

    /// <summary>Lists the editable config scopes for a product: the product-level default plus one entry
    /// per enabled lecture mode, each flagged with whether it already has its own saved override. Drives
    /// the scope selector in the Product Edit serial-key section.</summary>
    Task<List<SerialKeyConfigScope>> ListConfigScopesAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Upserts a serial-key config. The <c>ProductModeId</c> on the model decides scope:
    /// null = product-level (fallback), set = per-mode override. One active row per
    /// (product, mode, provider), enforced by the partial unique index.</summary>
    Task<(bool ok, string? error)> SaveProductConfigAsync(ProductSerialKeyConfigItem model, Guid? actorId, CancellationToken ct = default);

    /// <summary>Disable + soft-remove config so it stops generating keys. When <paramref name="productModeId"/>
    /// is null, clears the product-level config; when set, clears just that mode's override.
    /// Doesn't touch existing records — they keep their keys.</summary>
    Task<bool> ClearProductConfigAsync(Guid productId, Guid? actorId, Guid? productModeId = null, CancellationToken ct = default);

    /// <summary>
    /// Re-sends the product↔pack registration to Valence for an ALREADY SAVED config, without
    /// touching the config itself. Saving does this too, but this gives a direct way to repair a
    /// product whose mapping never reached Valence — the symptom being key generation failing with
    /// "No pack found for this course" while the pack looks correctly set here. Idempotent.
    /// </summary>
    Task<(bool ok, string? error)> RemapValencePackAsync(Guid productId, Guid? productModeId = null, CancellationToken ct = default);

    /// <summary>Bulk-map serial-key config to many products in one action. Applies the shared
    /// template (provider + config) to each product, using that product's own provider product
    /// code. Overwrites any existing config for the selected products (same upsert as the
    /// single-product save). Returns a per-product outcome so the UI can show what succeeded.</summary>
    Task<BulkConfigResult> BulkSaveProductConfigAsync(BulkConfigRequest request, Guid? actorId, CancellationToken ct = default);

    /// <summary>Import product→provider serial-key mappings from the old ERP SQL Server. Reads ERP
    /// dbo.Products, decides provider by PackId (null → RioPlay, set → Valence), matches to new
    /// products by SKUCode→Sku, and upserts config via the same guarded SaveProductConfigAsync path.
    /// Rio uses the default tenant; Valence stamps the given BaseUrl/PathSegment/ClassId and per-row
    /// KeyViews (eWatchTime ?? eOpens ?? 1).</summary>
    Task<ErpMappingImportResult> ImportErpProviderMappingsAsync(ErpMappingImportRequest request, Guid? actorId, CancellationToken ct = default);
}

/// <summary>Thin CRUD over <see cref="RioPlayTenant"/> with the Data-Protection encrypt/decrypt
/// dance hidden inside. Callers never touch the encrypted column directly.</summary>
public interface IRioPlayTenantService
{
    Task<List<RioPlayTenantItem>> ListAsync(CancellationToken ct = default);
    Task<RioPlayTenantEdit?> GetAsync(Guid id, CancellationToken ct = default);
    Task<(bool ok, string? error, Guid id)> SaveAsync(RioPlayTenantEdit model, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Resolves a tenant by id, falling back to the default when <paramref name="tenantId"/> is null.
    /// Returns the decrypted secret + base URL ready for use by the provider.</summary>
    Task<RioPlayTenantCredentials?> ResolveCredentialsAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>Diagnostic: fire a real NopSignUp at Rio with the saved tenant's credentials and
    /// return the full raw round-trip detail (status, Server header, response body). Returns a
    /// human-readable error string if the tenant or secret is missing.</summary>
    Task<RioConnectionTestResult> TestConnectionAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Diagnostic: run the FULL chain (SignUp → Token → CreateSerialKey) against the saved
    /// tenant, using the given product's config + provider product code. Confirms all three APIs work.
    /// <paramref name="productConfigId"/> is a ProductSerialKeyConfig id to pull real parameters from;
    /// pass null to use minimal defaults.</summary>
    Task<RioChainTestSummary> TestFullChainAsync(Guid tenantId, Guid? productConfigId, CancellationToken ct = default);
}

/// <summary>Flattened chain-test result for the admin screen.</summary>
public class RioChainTestSummary
{
    public bool AllPassed { get; set; }
    public string? Error { get; set; }
    public List<RioChainStep> Steps { get; set; } = new();
}

public class RioChainStep
{
    public string Step { get; set; } = "";
    public bool Success { get; set; }
    public string? Detail { get; set; }
    public string? Raw { get; set; }
}

/// <summary>Result of the admin "Test Connection" button — surfaced directly on the tenants page.</summary>
public class RioConnectionTestResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string RequestUrl { get; set; } = "";
    public string? RequestHeaders { get; set; }
    public string? HttpVersion { get; set; }
    public string? ServerHeader { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
}

/// <summary>Decrypted credential bundle handed to the provider for a single call.
/// Not persisted, not logged.</summary>
public record RioPlayTenantCredentials(int TenantId, string Secret, string BaseUrl);
