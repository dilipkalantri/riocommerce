using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.SerialKeys;

public class SerialKeyListItem
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    /// <summary>Lecture mode the customer purchased (from OrderItem.ModeName), so admins can see which
    /// mode this key was generated for — e.g. "Recorded Lectures + Hardcopy Notes". Null for mode-less items.</summary>
    public string? ModeName { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string? SerialKey { get; set; }
    public SerialKeyStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public string? ErrorMessage { get; set; }

    // ── Provider reference + registration-style outcome (Superclass shows these; key-issuing
    //    providers leave the registration fields null). ExternalReference = Rio EntityId / Superclass
    //    student_id. On the list so the dedicated Superclass page can render them without a detail load.
    public string? ExternalReference { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? SubscriptionStatus { get; set; }
    public string? ProviderCourseRef { get; set; }
}

public class SerialKeyDetail : SerialKeyListItem
{
    public string RequestPayload { get; set; } = "{}";
    /// <summary>The actual JSON parameters sent to the provider's API on the last attempt (Rio's Entity
    /// body / Valence's form fields / Superclass's form fields, secrets masked). For parameter
    /// inspection. Null before any attempt.</summary>
    public string? ApiRequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public string? TenantRef { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    /// <summary>Which config produced this key: "Mode override (<mode>)" when a per-mode config resolved,
    /// or "Product default" when the fallback was used. Derived at read time by matching the item's mode
    /// against the product's saved configs. Purely informational for the admin.</summary>
    public string? ConfigSource { get; set; }
}

public class SerialKeyListFilter
{
    public SerialKeyStatus? Status { get; set; }
    public string? ProviderKey { get; set; }
    public Guid? OrderId { get; set; }
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class RioPlayTenantItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TenantId { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public bool HasSecret { get; set; }
}

public class RioPlayTenantEdit
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TenantId { get; set; }
    /// <summary>Leave null on edit to keep the existing secret; set to rotate.</summary>
    public string? Secret { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
}

/// <summary>Admin edit model for the singleton Superclass global settings. The API key follows the
/// same blank-on-edit-keeps-existing rule as the RioPlay secret and is never populated on read.</summary>
public class SuperclassSettingsEdit
{
    public string ApiBaseUrl { get; set; } = "https://lms.superclassapp.in";
    /// <summary>Leave null/blank on edit to keep the existing key; set to rotate. Never returned on read.</summary>
    public string? ApiKey { get; set; }
    /// <summary>True when a key is already stored (drives a "Set/Missing" chip in the UI).</summary>
    public bool HasApiKey { get; set; }
    public int DefaultClassId { get; set; } = 318;
    public string Environment { get; set; } = "Production";
    public bool LoggingEnabled { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-product config — what the admin Product Edit page binds to.
// All Rio API parameters live here so a non-technical admin can fill them in
// via labelled form inputs instead of editing raw JSON.
// ─────────────────────────────────────────────────────────────────────────────

public class ProductSerialKeyConfigItem
{
    public Guid? Id { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>Scope of this config. Null = the product-level config (fallback for any mode without its
    /// own override, and for mode-less products). Set = a per-mode override that applies only when that
    /// specific <see cref="RioCommerce.Core.Entities.ProductMode"/> is purchased.</summary>
    public Guid? ProductModeId { get; set; }

    public string ProviderKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string? ProviderProductCode { get; set; }
    public bool AutoActivate { get; set; } = true;
    public bool IsActive { get; set; } = true;

    // ── RioPlay API parameters (the ConfigJson blob, surfaced as typed fields) ──
    // Field set mirrors the nopCommerce plugin + the API doc one-for-one. Every
    // field is nullable so the admin can leave defaults blank — the provider
    // sends only the populated ones.
    public short? EOpens { get; set; }
    public short? EWatchTime { get; set; }
    public short? EWatchTimeType { get; set; }
    public short? EValidity { get; set; }
    public short? EValidityType { get; set; }
    public int? TenantIdInt { get; set; }
    public int? AccessStatus { get; set; }
    public int? TotalAllowedDuration { get; set; }
    public string AllowedDevices { get; set; } = "1";
    public int? WithinDeviceCount { get; set; }
    public short? EDeviceLockingType { get; set; }
    public bool AllowSecondScreen { get; set; }
    public short? EScreenResolution { get; set; }
    public int SwitichingDevice { get; set; }   // Rio's spelling
    public int? SwitchCount { get; set; }
    public bool AnalyticsRequired { get; set; }
    public bool IsLiveClassIncluded { get; set; }
    public bool IsTrial { get; set; }
    public bool WaterMarkRequired { get; set; }
    public short? WaterMarkText { get; set; }
    public int UpdateFrequencyInDays { get; set; }
    public DateTime? ValidTill { get; set; }
    public int? TemplateSerialKeyId { get; set; }
    public short? IsRioActive { get; set; } = 1;

    // ── Valence (Edubees) parameters (its own ConfigJson shape) ──
    public string? ValenceBaseUrl { get; set; }
    public string? ValencePathSegment { get; set; }
    /// <summary>Selected Valence pack id (sent as the <c>course</c> field). Also mirrored into
    /// <see cref="ProviderProductCode"/> on save so the provider's existing mapping works unchanged.</summary>
    public int? ValencePackId { get; set; }
    /// <summary>Comma-separated list of pack ids for a COMBO Valence product (e.g. "574,575").
    /// When 2+ values are present, the enqueue fans out one SerialKeyRecord per pack, mirroring
    /// how Rio's comma-separated ProviderProductCode works. Empty/null = single-pack behaviour.</summary>
    public string? ValencePackIdsCsv { get; set; }
    public string? ValenceClassId { get; set; }
    public int? ValenceKeyViews { get; set; }

    // ── Superclass (registration API) parameters (its own ConfigJson shape) ──

    /// <summary>
    /// Which kind of Superclass mapping this product uses. Decides which id fields the editor
    /// shows, which ones are required, and — because <c>/api/register</c> must carry a course id
    /// OR a combo id but never both — which one is sent.
    ///
    /// <para>NOT persisted: nothing is added to ConfigJson for it. The mode is derived from the ids
    /// already stored (see <c>SerialKeyService.DeriveSuperclassMode</c>), so existing rows keep
    /// their exact shape and no migration or backfill is needed. Null on the way in means "work it
    /// out from the ids I supplied", which keeps every pre-existing caller working unchanged.</para>
    /// </summary>
    public SuperclassMappingMode? SuperclassMappingMode { get; set; }

    /// <summary>Set on load when a legacy row carries BOTH a course id and a combo id — a shape the
    /// editor can no longer produce. Nothing is rewritten; the admin is told what will be sent.</summary>
    public string? SuperclassMappingWarning { get; set; }

    /// <summary>Superclass course id to assign (required for the single-course mode). Also mirrored
    /// into <see cref="ProviderProductCode"/> on save.</summary>
    public int? SuperclassCourseId { get; set; }
    /// <summary>Optional Superclass combo package id. Leave blank for an individual course.</summary>
    public int? SuperclassComboId { get; set; }
    /// <summary>Comma-separated course ids for a product that grants SEVERAL Superclass courses
    /// (e.g. "2722, 2723"). One registration is sent per id. Blank = the single
    /// <see cref="SuperclassCourseId"/>.</summary>
    public string? SuperclassCourseIdsCsv { get; set; }
    /// <summary>Comma-separated combo ids, for a product that grants several Superclass combo
    /// packages. One registration per id, exactly like <see cref="SuperclassCourseIdsCsv"/>.</summary>
    public string? SuperclassComboIdsCsv { get; set; }
    /// <summary>1 = View Based, 2 = Time Based.</summary>
    public int? SuperclassViewType { get; set; } = 1;
    /// <summary>Number of allowed views (view_type=1) or minutes (view_type=2).</summary>
    public int? SuperclassValue { get; set; }
    /// <summary>1 = Valid for a number of days, 2 = Fixed expiry date.</summary>
    public int? SuperclassValidityType { get; set; } = 1;
    /// <summary>Days of validity when <see cref="SuperclassValidityType"/> = 1.</summary>
    public int? SuperclassValidityDays { get; set; }
    /// <summary>Fixed expiry date when <see cref="SuperclassValidityType"/> = 2.</summary>
    public DateTime? SuperclassExpiryDate { get; set; }
    /// <summary>Optional per-product override of the global Class ID (defaults to the global 318).</summary>
    public int? SuperclassClassIdOverride { get; set; }
    /// <summary>Optional free-text purchase-option / plan label carried as metadata for Superclass mapping.</summary>
    public string? SuperclassPurchaseOption { get; set; }
}

public class ProviderInfo
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool SupportsActivate { get; set; }
}

/// <summary>One product row in a bulk serial-key mapping: which product, and its provider-specific
/// product code (Rio's product id, or ignored for Valence since Valence keys by our product id).</summary>
public class BulkConfigProductRow
{
    public Guid ProductId { get; set; }
    public string? ProviderProductCode { get; set; }
}

/// <summary>Request to map one provider + a shared config template onto many products at once.
/// The shared fields mirror ProductSerialKeyConfigItem; only ProviderProductCode varies per row.</summary>
public class BulkConfigRequest
{
    public string ProviderKey { get; set; } = string.Empty;
    public List<BulkConfigProductRow> Products { get; set; } = new();

    public bool AutoActivate { get; set; } = true;
    public bool IsActive { get; set; } = true;

    // Shared config applied to every product in the batch.
    // ── RioPlay ──
    public Guid? TenantId { get; set; }
    public short? EValidity { get; set; }
    public short? EValidityType { get; set; }
    public string AllowedDevices { get; set; } = "1";
    public int? TemplateSerialKeyId { get; set; }

    // ── Valence ──
    public string? ValenceBaseUrl { get; set; }
    public string? ValencePathSegment { get; set; }
    public int? ValencePackId { get; set; }
    public string? ValenceClassId { get; set; }
    public int? ValenceKeyViews { get; set; }
}

/// <summary>One selectable scope in the per-mode serial-key editor: either the product-level default
/// (<see cref="ProductModeId"/> null) or a specific lecture mode. <see cref="HasOwnConfig"/> tells the
/// UI whether this scope already has a saved override (vs inheriting the product default).</summary>
public class SerialKeyConfigScope
{
    /// <summary>Null = the product-level default scope; otherwise the ProductMode this scope edits.</summary>
    public Guid? ProductModeId { get; set; }
    /// <summary>Display label — "Product default" or the mode name.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>True when a saved config row exists for this exact scope (so the UI can show
    /// "overridden" vs "inherits product default").</summary>
    public bool HasOwnConfig { get; set; }
    /// <summary>Provider of the saved config for this scope, if any (for a quick badge in the list).</summary>
    public string? ProviderKey { get; set; }
}

/// <summary>Per-product outcome of a bulk mapping run.</summary>
public class BulkConfigItemResult
{
    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

public class BulkConfigResult
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<BulkConfigItemResult> Items { get; set; } = new();
}

/// <summary>Request to import product→provider serial-key mappings from the old ERP SQL Server.</summary>
public class ErpMappingImportRequest
{
    /// <summary>SQL Server connection string for the old ERP DB (erp_riocommerce_com). Not stored.</summary>
    public string SourceConnectionString { get; set; } = string.Empty;

    /// <summary>Preview only — read + match + validate, write nothing.</summary>
    public bool DryRun { get; set; } = true;

    // The 3 static Valence connection values, entered once, stamped onto every Valence product.
    public string? ValenceBaseUrl { get; set; }
    public string? ValencePathSegment { get; set; }
    public string? ValenceClassId { get; set; }
}

public class ErpMappingItemResult
{
    public string Sku { get; set; } = "";
    public string Provider { get; set; } = "";      // "rioplay" | "valence"
    public string Outcome { get; set; } = "";        // "created" | "updated" | "skipped" | "error"
    public string? Message { get; set; }
}

public class ErpMappingImportResult
{
    public bool Ok { get; set; }
    public bool DryRun { get; set; }
    public int Read { get; set; }
    public int RioMapped { get; set; }
    public int ValenceMapped { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public string? Error { get; set; }
    public List<ErpMappingItemResult> Items { get; set; } = new();
}

/// <summary>
/// How a product maps onto Superclass. <c>/api/register</c> carries exactly one <c>course_id</c> and
/// one <c>combo_id</c>, and a product is granted through one or the other — never both — so these
/// three modes are the complete set the API supports.
///
/// <para>Derived from the stored ids rather than persisted, so no existing ConfigJson changes shape.</para>
/// </summary>
public enum SuperclassMappingMode
{
    /// <summary>One course. <c>course_id</c> only; one registration.</summary>
    SingleCourse = 0,

    /// <summary>Several courses. <c>course_id</c> only; the enqueue sends one registration per id.</summary>
    MultipleCourses = 1,

    /// <summary>One or more combo packages. <c>combo_id</c> only; one registration per combo id.</summary>
    ComboPackages = 2,
}
