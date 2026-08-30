using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Per-product Superclass configuration, stored as the opaque <c>ConfigJson</c> on
/// <c>ProductSerialKeyConfig</c> and deserialised here. Superclass credentials are GLOBAL
/// (see <c>SuperclassSettings</c>) — this blob only holds the course mapping + validity that
/// vary per product.
///
/// <para>Example ConfigJson:</para>
/// <code>
/// {
///   "CourseId": 2403,
///   "ComboId": null,
///   "ViewType": 2,
///   "Value": 4500,
///   "ValidityType": 2,
///   "ValidityDays": null,
///   "ExpiryDate": "2027-07-22",
///   "ClassIdOverride": null,
///   "PurchaseOption": "Annual"
/// }
/// </code>
/// </summary>
public class SuperclassProductConfig
{
    /// <summary>Superclass <c>course_id</c> to assign. Required.</summary>
    public int? CourseId { get; set; }

    /// <summary>Optional Superclass <c>combo_id</c>. Null/blank = assign an individual course.</summary>
    public int? ComboId { get; set; }

    /// <summary>Optional comma-separated <c>course_id</c> list for a product that grants SEVERAL
    /// Superclass courses. Superclass's /api/register takes exactly one course per call, so the
    /// enqueue fans out one registration per id — each fanned-out record carries a single
    /// <see cref="CourseId"/> and no CSV. Empty/null = single-course behaviour driven by
    /// <see cref="CourseId"/>. Mirrors Rio's comma-separated ProviderProductCode and Valence's
    /// PackIdsCsv, so a combo behaves the same way whichever vendor is behind it.</summary>
    public string? CourseIdsCsv { get; set; }

    /// <summary>Optional comma-separated <c>combo_id</c> list, for a product that grants several
    /// Superclass combo packages. Fans out exactly like <see cref="CourseIdsCsv"/>.</summary>
    public string? ComboIdsCsv { get; set; }

    /// <summary><c>view_type</c>: 1 = View Based, 2 = Time Based.</summary>
    public int? ViewType { get; set; }

    /// <summary><c>value</c>: number of allowed views (ViewType=1) or minutes (ViewType=2).</summary>
    public int? Value { get; set; }

    /// <summary><c>validity_type</c>: 1 = Days, 2 = Fixed expiry date.</summary>
    public int? ValidityType { get; set; }

    /// <summary>Days of validity when <see cref="ValidityType"/> = 1.</summary>
    public int? ValidityDays { get; set; }

    /// <summary>Fixed expiry date when <see cref="ValidityType"/> = 2 (sent as yyyy-MM-dd).</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Optional per-product override of the global <c>class_id</c> (defaults to 318).</summary>
    public int? ClassIdOverride { get; set; }

    /// <summary>Optional free-text plan/purchase-option label kept for Superclass mapping/audit.</summary>
    public string? PurchaseOption { get; set; }
}

/// <summary>
/// Superclass <c>/api/register</c> JSON response envelope.
///
/// <para>The live API returns <c>status</c> as a NUMERIC HTTP-style code (<c>200</c> on success,
/// <c>402</c>/<c>4xx</c> on failure) — NOT a boolean — alongside a human <c>message</c> and a
/// <c>data</c> element that is an object on success but an empty array on failure. The
/// <c>student_id</c> may be top-level or nested inside <c>data</c>. Earlier this type declared
/// <c>status</c> as a <c>bool</c>, so a numeric status threw during deserialisation and turned
/// EVERY response — success and error alike — into a generic "bad_response" that hid the vendor's
/// real message. <see cref="FlexibleStatusConverter"/> now normalises every observed <c>status</c>
/// shape (number, quoted number, word, bool) to an int so detection can't break on a shape change.</para>
/// </summary>
public class SuperclassRegisterResponse
{
    /// <summary>Normalised numeric status: 200/201 = success, ≥400 = error. See <see cref="FlexibleStatusConverter"/>.</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(FlexibleStatusConverter))]
    public int StatusCode { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("student_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? StudentId { get; set; }

    [JsonPropertyName("course_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? CourseId { get; set; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    /// <summary>Raw <c>data</c> payload — an object carrying <c>student_id</c> on success, an empty
    /// array on error. The provider recovers a nested id from here when it isn't top-level.</summary>
    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}

/// <summary>
/// Tolerates every <c>status</c> shape Superclass has been observed to (or might) emit — a JSON
/// number (<c>200</c>), a quoted number (<c>"200"</c>), a word (<c>"success"</c>/<c>"error"</c>),
/// or a boolean — and maps it to an int code. Success words → 200, failure words → 400,
/// missing/null → 0. This is the guard that stops a numeric <c>status</c> from throwing during
/// deserialisation (which previously masked the vendor's real error message).
/// </summary>
public sealed class FlexibleStatusConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var n) ? n : (int)reader.GetDouble();
            case JsonTokenType.True:
                return 200;
            case JsonTokenType.False:
                return 400;
            case JsonTokenType.String:
                var s = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(s)) return 0;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
                return s.ToLowerInvariant() switch
                {
                    "true" or "success" or "ok" or "active" => 200,
                    "false" or "error" or "fail" or "failed" => 400,
                    _ => 0,
                };
            case JsonTokenType.Null:
                return 0;
            default:
                reader.Skip();
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
