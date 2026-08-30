using System.Text.Json.Serialization;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

// ─────────────────────────────────────────────────────────────────────────────
// Wire-format models for RioPlay. These mirror the PDF contract literally —
// any drift between us and Rio shows up here first. PascalCase property names
// match what Rio returns and what its sample bodies show.
//
// JsonPropertyName attributes are explicit so a future System.Text.Json policy
// change (e.g. camelCase) can't silently break the wire format.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>POST /Api/Account/NopSignUp — idempotent on Rio's side; ignore "already exists" responses.</summary>
internal class RioNopSignUpRequest
{
    [JsonPropertyName("NopGuid")]      public Guid NopGuid { get; set; }
    [JsonPropertyName("DisplayName")]  public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("Email")]        public string Email { get; set; } = string.Empty;
    [JsonPropertyName("MobileNumber")] public string MobileNumber { get; set; } = string.Empty;
    [JsonPropertyName("Countrycode")]  public int? Countrycode { get; set; }
}

internal class RioNopSignUpResponse
{
    [JsonPropertyName("EntityId")] public int? EntityId { get; set; }
    [JsonPropertyName("Error")]    public string? Error { get; set; }
}

/// <summary>Token-endpoint response shape per the live Rio API: <c>{"token":"eyJhbGci..."}</c>.
/// The docs implied a bare string but the deployed server wraps it. Both fields are tolerated
/// — case-insensitive deserialization handles "token" or "Token".</summary>
internal class RioGenerateTokenResponse
{
    [JsonPropertyName("token")] public string? Token { get; set; }
}

/// <summary>POST /Api/Account/GenerateTokenForNop — simple flow per the PDF.
/// Returns a bare string token in the response body.</summary>
internal class RioGenerateTokenRequest
{
    [JsonPropertyName("NopGuid")] public Guid NopGuid { get; set; }
}

/// <summary>Wraps the create / activate payload. Rio expects everything under an "Entity" envelope —
/// flat bodies are silently rejected (the production plugin uses this wrapper too).</summary>
internal class RioEntityEnvelope<T>
{
    [JsonPropertyName("Entity")] public T Entity { get; set; } = default!;
}

/// <summary>POST body for both CreateSerialKeyForNop (no bearer) and GenerateSerialKeyForNop (bearer).
/// Field set + nullability mirrors the PDF — mandatory fields are non-null on the wire.</summary>
internal class RioSerialKeyEntity
{
    [JsonPropertyName("Quantity")]               public int Quantity { get; set; } = 1;
    [JsonPropertyName("NopGuid")]                public Guid? NopGuid { get; set; }
    [JsonPropertyName("Owner")]                  public int Owner { get; set; }
    [JsonPropertyName("Product")]                public int? Product { get; set; }
    [JsonPropertyName("TemplateSerialKeyId")]    public int? TemplateSerialKeyId { get; set; }
    [JsonPropertyName("UpdateFrequencyInDays")]  public int UpdateFrequencyInDays { get; set; }
    [JsonPropertyName("AllowSecondScreen")]      public bool? AllowSecondScreen { get; set; }
    [JsonPropertyName("ConfStyle")]              public short? ConfStyle { get; set; }
    [JsonPropertyName("EOpens")]                 public short? EOpens { get; set; }
    [JsonPropertyName("EWatchTime")]             public short? EWatchTime { get; set; }
    [JsonPropertyName("EValidity")]              public short? EValidity { get; set; }
    [JsonPropertyName("TenantId")]               public int? TenantId { get; set; }
    [JsonPropertyName("AccessStatus")]           public int? AccessStatus { get; set; }
    [JsonPropertyName("TotalAllowedDuration")]   public int? TotalAllowedDuration { get; set; }
    [JsonPropertyName("AllowedDevices")]         public string AllowedDevices { get; set; } = "1";
    [JsonPropertyName("IsFranchisee")]           public bool? IsFranchisee { get; set; }
    [JsonPropertyName("IsPublisher")]            public bool? IsPublisher { get; set; }
    [JsonPropertyName("EPlayerEdition")]         public short? EPlayerEdition { get; set; }
    [JsonPropertyName("WithinDeviceCount")]      public int? WithinDeviceCount { get; set; }
    [JsonPropertyName("ActivatedBy")]            public int? ActivatedBy { get; set; }
    [JsonPropertyName("EOpCode")]                public short? EOpCode { get; set; }
    [JsonPropertyName("EValidityType")]          public short? EValidityType { get; set; }
    [JsonPropertyName("AnalyticsRequired")]      public bool? AnalyticsRequired { get; set; }
    [JsonPropertyName("IsLiveClassIncluded")]    public bool? IsLiveClassIncluded { get; set; }
    [JsonPropertyName("EDeviceLockingType")]     public short? EDeviceLockingType { get; set; }
    [JsonPropertyName("SwitichingDevice")]       public int SwitichingDevice { get; set; } // (sic) Rio's spelling
    /// <summary>Sent as an all-zeros GUID in the working sample — Rio expects this field present.</summary>
    [JsonPropertyName("DeviceId")]               public Guid DeviceId { get; set; } = Guid.Empty;
    [JsonPropertyName("LastLogin")]              public DateTime? LastLogin { get; set; }
    [JsonPropertyName("DeviceType")]             public string? DeviceType { get; set; }
    [JsonPropertyName("MachineCode")]            public string? MachineCode { get; set; }
    [JsonPropertyName("SysInfo")]                public string? SysInfo { get; set; }
    [JsonPropertyName("ValidTill")]              public DateTime? ValidTill { get; set; }
    [JsonPropertyName("EWatchTimeType")]         public short? EWatchTimeType { get; set; }
    [JsonPropertyName("EScreenResolution")]      public short? EScreenResolution { get; set; }
    [JsonPropertyName("IsTrial")]                public bool? IsTrial { get; set; }
    [JsonPropertyName("WaterMarkRequired")]      public bool? WaterMarkRequired { get; set; }
    [JsonPropertyName("WaterMarkText")]          public short? WaterMarkText { get; set; }
    [JsonPropertyName("SwitchCount")]            public int? SwitchCount { get; set; }
    [JsonPropertyName("UsbKey")]                 public string? UsbKey { get; set; }
    [JsonPropertyName("IsActive")]               public short? IsActive { get; set; }
}

internal class RioSerialKeyResponse
{
    [JsonPropertyName("SerialKey")]      public string? SerialKey { get; set; }
    /// <summary>2 = only created, 3 = created and activated. Per the PDF.</summary>
    [JsonPropertyName("SerialkeyStatus")] public short? SerialkeyStatus { get; set; }
}

/// <summary>
/// The ACTUAL request body shape the live CreateSerialKeyForNop endpoint expects (confirmed by
/// Antargyan). This differs substantially from the public PDF: it's wrapped in an "Entity"
/// envelope and uses a different field set (ESerialkeyType, EViewsType, ViewsPerContent,
/// WatchTime, ValidityInDays, EStatus, PlaylistId GUID, GUID TenantId, CSV AllowedDevices, …).
///
/// Example body Antargyan provided:
/// <code>
/// {"Entity":{"ESerialkeyType":1,"EViewsType":1,"ViewsPerContent":1000,"WatchTime":1.5,
///   "ESubscriptionType":0,"ValidityInDays":180,
///   "AllowedDevices":"windowslaptop,android,ios,maccatalyst","TotalAllowedDuration":0,
///   "EAnalyticsType":1,"InsertUserId":3,"IsTemplate":false,
///   "TenantId":"d8319038-4dfa-4e3a-963a-d24c537bf45c","Note":"15",
///   "PlaylistId":"5a460d89-48fd-44b3-a118-95fda84c7dbf","Quantity":1,"Version":1,
///   "EStatus":2,"EWatchTimetype":1,"AllowedSecondScreen":false,"IsActive":1}}
/// </code>
/// </summary>
internal class RioCreateSerialKeyEntity
{
    [JsonPropertyName("ESerialkeyType")]    public int ESerialkeyType { get; set; } = 1;
    [JsonPropertyName("EViewsType")]        public int EViewsType { get; set; } = 1;
    [JsonPropertyName("ViewsPerContent")]   public int ViewsPerContent { get; set; } = 1000;
    [JsonPropertyName("WatchTime")]         public double WatchTime { get; set; } = 1.5;
    [JsonPropertyName("ESubscriptionType")] public int ESubscriptionType { get; set; } = 0;
    [JsonPropertyName("ValidityInDays")]    public int ValidityInDays { get; set; } = 180;
    [JsonPropertyName("AllowedDevices")]    public string AllowedDevices { get; set; } = "windowslaptop,android,ios,maccatalyst";
    [JsonPropertyName("TotalAllowedDuration")] public int TotalAllowedDuration { get; set; } = 0;
    [JsonPropertyName("EAnalyticsType")]    public int EAnalyticsType { get; set; } = 1;
    [JsonPropertyName("InsertUserId")]      public int InsertUserId { get; set; } = 3;
    [JsonPropertyName("IsTemplate")]        public bool IsTemplate { get; set; } = false;
    /// <summary>Rio tenant GUID (NOT the numeric 817 id). Provided by Antargyan per product/playlist.</summary>
    [JsonPropertyName("TenantId")]          public string TenantId { get; set; } = string.Empty;
    [JsonPropertyName("Note")]              public string? Note { get; set; }
    /// <summary>The Rio playlist/content GUID this key unlocks. Per product.</summary>
    [JsonPropertyName("PlaylistId")]        public string PlaylistId { get; set; } = string.Empty;
    [JsonPropertyName("Quantity")]          public int Quantity { get; set; } = 1;
    [JsonPropertyName("Version")]           public int Version { get; set; } = 1;
    [JsonPropertyName("EStatus")]           public int EStatus { get; set; } = 2;
    [JsonPropertyName("EWatchTimetype")]    public int EWatchTimetype { get; set; } = 1;
    [JsonPropertyName("AllowedSecondScreen")] public bool AllowedSecondScreen { get; set; } = false;
    [JsonPropertyName("IsActive")]          public int IsActive { get; set; } = 1;
}

internal class RioActivateRequest
{
    [JsonPropertyName("SerialKey")] public string SerialKey { get; set; } = string.Empty;
}

internal class RioActivateResponse
{
    [JsonPropertyName("EntityId")] public int? EntityId { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Per-product Rio config — the typed shape that ProductSerialKeyConfig.ConfigJson
// deserialises to. Every field here is optional with a sensible default in the
// provider, so an admin can leave anything blank and still get a key issued.
// ─────────────────────────────────────────────────────────────────────────────

public class RioPlayProductConfig
{
    public short? EOpens { get; set; }
    public short? EWatchTime { get; set; }
    public short? EWatchTimeType { get; set; }
    public short? EValidity { get; set; }
    public short? EValidityType { get; set; }
    public int? TenantId { get; set; }
    public int? AccessStatus { get; set; } = 1;
    public int? TotalAllowedDuration { get; set; }
    public string AllowedDevices { get; set; } = "1";
    public int? WithinDeviceCount { get; set; }
    public short? EDeviceLockingType { get; set; }
    public bool? AllowSecondScreen { get; set; }
    public short? EScreenResolution { get; set; }
    public int SwitichingDevice { get; set; }
    public int? SwitchCount { get; set; }
    public bool? AnalyticsRequired { get; set; }
    public bool? IsLiveClassIncluded { get; set; }
    public bool? IsTrial { get; set; }
    public bool? WaterMarkRequired { get; set; }
    public short? WaterMarkText { get; set; }
    public int UpdateFrequencyInDays { get; set; }
    public DateTime? ValidTill { get; set; }
    public int? TemplateSerialKeyId { get; set; }
    public short? IsActive { get; set; } = 1;
}
