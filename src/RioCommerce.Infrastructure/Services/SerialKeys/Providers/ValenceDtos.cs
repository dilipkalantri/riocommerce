using System.Text.Json;
using System.Text.Json.Serialization;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Per-product Valence (Edubees) configuration, stored as the opaque <c>ConfigJson</c> on
/// <c>ProductSerialKeyConfig</c> and deserialised here. Valence has no separate tenant table
/// like Rio — the base URL and the obfuscated controller path segment ARE the access secret,
/// so they live in config alongside the per-product course routing.
///
/// <para>Example ConfigJson:</para>
/// <code>
/// {
///   "BaseUrl": "https://edubeessecurelms.com/edubeessecurelms",
///   "PathSegment": "ajsfkjsfdjsf",
///   "ClassId": "5702",
///   "KeyViews": 2
/// }
/// </code>
/// </summary>
public class ValenceProductConfig
{
    /// <summary>Scheme + host + app base, no trailing slash, e.g.
    /// <c>https://edubeessecurelms.com/edubeessecurelms</c>. The provider appends
    /// <c>/index.php/{PathSegment}/register_student_with_course</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The obfuscated controller segment that gates access (the working sample used
    /// <c>ajsfkjsfdjsf</c>). Treated as a secret — never logged in full.</summary>
    public string? PathSegment { get; set; }

    /// <summary>Valence <c>class_id</c> form field. Identifies the batch/class within the course.</summary>
    public string? ClassId { get; set; }

    /// <summary>Valence <c>key_views</c> form field — how many views/devices the key permits.
    /// Defaults to 1 when not configured.</summary>
    public int? KeyViews { get; set; }

    /// <summary>The Valence pack's numeric id this product is mapped to (used for the
    /// save_product_pack mapping call). The <c>course</c> value sent at registration is our own
    /// product id, not this.</summary>
    public int? PackId { get; set; }

    /// <summary>Optional comma-separated list of pack ids for a COMBO product. When set with 2+
    /// values, the enqueue fans out one SerialKeyRecord per pack (each record's persisted
    /// ConfigJson carries a single PackId — this field is only populated at the parent-config
    /// level, never at the per-record level). Empty/null = single-pack behaviour driven by
    /// <see cref="PackId"/>. Mirrors Rio's comma-separated ProviderProductCode.</summary>
    public string? PackIdsCsv { get; set; }

    /// <summary>Optional override of the <c>course</c> form field. Normally the provider uses
    /// <c>GenerateKeyRequest.ProviderProductCode</c>; set this only to force a value.</summary>
    public string? CourseOverride { get; set; }
}

/// <summary>
/// Valence's JSON response, parsed leniently. The only confirmed shape so far is the error
/// envelope <c>{"status":"error","message":"Missing parameters"}</c>. The success shape is not
/// yet confirmed, so every plausible key-bearing field is mapped; whichever is populated wins.
/// The raw body is always persisted regardless, so nothing is lost if the real field differs.
/// </summary>
public class ValenceResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // ── Candidate key-bearing fields (success shape unconfirmed) ──
    // TODO: once a real success response is captured, collapse these to the actual field.
    [JsonPropertyName("serial_key")]
    public string? SerialKey { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("serial")]
    public string? Serial { get; set; }

    [JsonPropertyName("license_key")]
    public string? LicenseKey { get; set; }

    [JsonPropertyName("activation_key")]
    public string? ActivationKey { get; set; }

    // Candidate external-reference fields.
    [JsonPropertyName("student_id")]
    public string? StudentId { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("registration_id")]
    public string? RegistrationId { get; set; }

    /// <summary>True when Valence signals success. Confirmed values: status == "error" means
    /// failure. Anything that isn't an explicit error, paired with a located key, is treated
    /// as success.</summary>
    [JsonIgnore]
    public bool IsExplicitError =>
        string.Equals(Status, "error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "false", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "fail", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "failure", StringComparison.OrdinalIgnoreCase);

    /// <summary>First non-empty candidate key field, or null if none present.</summary>
    [JsonIgnore]
    public string? AnyKey =>
        FirstNonEmpty(SerialKey, Key, Serial, LicenseKey, ActivationKey);

    /// <summary>First non-empty candidate reference field, or null.</summary>
    [JsonIgnore]
    public string? AnyReference =>
        FirstNonEmpty(StudentId, RegistrationId, Id);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

/// <summary>
/// Response envelope for Valence's <c>get_packs</c> endpoint:
/// <c>{ "status": "success", "message": ..., "data": [ { "id", "pack_name", "tags" }, ... ] }</c>.
/// </summary>
public class ValencePacksResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<ValencePackDto>? Data { get; set; }

    [JsonIgnore]
    public bool IsSuccess => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One pack item from <c>get_packs</c> → <c>data[]</c>. Valence sends <c>id</c> as a
/// number, but some PHP APIs serialise ids as strings, so it's accepted as either.</summary>
public class ValencePackDto
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int Id { get; set; }

    [JsonPropertyName("pack_name")]
    public string? PackName { get; set; }

    [JsonPropertyName("tags")]
    public string? Tags { get; set; }
}

/// <summary>Reads an integer whether the JSON value is a number or a quoted string.</summary>
public class FlexibleIntConverter : System.Text.Json.Serialization.JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetInt32();
            case JsonTokenType.String:
                var s = reader.GetString();
                return int.TryParse(s, out var v) ? v : 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
