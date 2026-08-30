using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Superclass LMS provider. Unlike RioPlay/Valence this vendor does NOT issue a serial key — it
/// registers (or detects an existing) student and assigns the purchased course in a single
/// <c>multipart/form-data</c> POST to <c>{BaseUrl}/api/register</c>, then sends its own welcome
/// email + WhatsApp. So <see cref="IssuesSerialKey"/> is <c>false</c>: a successful result carries a
/// <c>student_id</c> (→ <c>ExternalReference</c>) and subscription/expiry data instead of a key.
///
/// <para>Credentials + defaults are GLOBAL (<c>SuperclassSettings</c>, resolved via
/// <see cref="ISuperclassSettingsService"/>); only the course mapping + validity are per-product
/// (<see cref="SuperclassProductConfig"/> in the record's ConfigJson).</para>
///
/// <para>Auth is header-only (<c>Authorization: Bearer</c> and <c>X-API-Key</c>); the key never
/// appears in the form body nor in the audited <c>RawRequest</c>. Per the <see cref="ISerialKeyProvider"/>
/// contract this provider NEVER throws — all HTTP/parse failures become a non-success result.</para>
/// </summary>
public class SuperclassSerialKeyProvider : ISerialKeyProvider
{
    public string Key => "superclass";
    public string DisplayName => "Superclass LMS";

    // Registration-style: no key, no separate activate/status endpoints. Provisions in one call.
    public bool IssuesSerialKey => false;
    public bool SupportsActivate => false;
    public bool SupportsStatusLookup => false;

    private const string RegisterPath = "/api/register";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ISuperclassSettingsService _settings;
    private readonly ILogger<SuperclassSerialKeyProvider> _log;
    private readonly JsonSerializerOptions _json;

    public SuperclassSerialKeyProvider(
        IHttpClientFactory httpFactory,
        ISuperclassSettingsService settings,
        ILogger<SuperclassSerialKeyProvider> log)
    {
        _httpFactory = httpFactory;
        _settings = settings;
        _log = log;
        _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest req, CancellationToken ct)
    {
        // ── Global credentials ──────────────────────────────────────────────
        var creds = await _settings.ResolveAsync(ct);
        if (creds == null || string.IsNullOrWhiteSpace(creds.ApiBaseUrl))
        {
            _log.LogError("Superclass GENERATE missing global settings orderItemId={OrderItemId}", req.OrderItemId);
            return GenerateKeyResult.Fail("no_config",
                "Superclass global settings are not configured (Base URL / API key). Configure them under Superclass Settings.");
        }

        // ── Per-product config ──────────────────────────────────────────────
        var cfg = ParseConfig(req.ConfigJson);

        // ── Course OR combo, never both ─────────────────────────────────────
        // A registration grants access through a course id or through a combo package id. Sending
        // both used to be possible — Course ID was mandatory in the editor, so a combo product had
        // the same number typed into both fields and both went on the wire. The combo wins where
        // one is present, and the course id is left blank rather than tagging along.
        var comboIdValue = cfg.ComboId is > 0 ? cfg.ComboId!.Value : (int?)null;
        var courseIdValue = comboIdValue is null
            ? cfg.CourseId ?? (int.TryParse(req.ProviderProductCode, out var pc) ? pc : (int?)null)
            : null;

        if (courseIdValue is not > 0 && comboIdValue is not > 0)
        {
            _log.LogError("Superclass GENERATE missing course and combo id orderItemId={OrderItemId}", req.OrderItemId);
            return GenerateKeyResult.Fail("no_course",
                "Superclass requires a positive Course ID (or a Combo ID) in the product config.");
        }
        var viewType = cfg.ViewType is 1 or 2 ? cfg.ViewType!.Value : 1;
        if (cfg.Value is not > 0)
            return GenerateKeyResult.Fail("no_value", "Superclass requires a positive Value (views or minutes).");
        var validityType = cfg.ValidityType is 1 or 2 ? cfg.ValidityType!.Value : 1;

        // validity_days_date + the expiry we persist locally.
        string validityDaysDate;
        DateTime? expiresAt;
        if (validityType == 2)
        {
            if (cfg.ExpiryDate is not { } exp)
                return GenerateKeyResult.Fail("no_validity", "Superclass Validity Type 'Fixed Expiry Date' requires an expiry date.");
            validityDaysDate = exp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            expiresAt = DateTime.SpecifyKind(exp.Date, DateTimeKind.Utc);
        }
        else
        {
            if (cfg.ValidityDays is not > 0)
                return GenerateKeyResult.Fail("no_validity", "Superclass Validity Type 'Days' requires a positive number of days.");
            validityDaysDate = cfg.ValidityDays!.Value.ToString(CultureInfo.InvariantCulture);
            expiresAt = DateTime.UtcNow.AddDays(cfg.ValidityDays!.Value);
        }

        // ── Customer fields ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(req.CustomerName)
            || string.IsNullOrWhiteSpace(req.CustomerEmail)
            || string.IsNullOrWhiteSpace(req.CustomerPhone))
        {
            _log.LogError("Superclass GENERATE missing customer fields orderItemId={OrderItemId}", req.OrderItemId);
            return GenerateKeyResult.Fail("missing_customer",
                "Superclass requires fullname, email and mobile — one or more is empty on the order.");
        }
        var (countryCode, mobile) = NormalisePhone(req.CustomerCountryCode, req.CustomerPhone!);
        var classId = cfg.ClassIdOverride is > 0 ? cfg.ClassIdOverride!.Value : creds.DefaultClassId;
        // Exactly one of these is ever non-empty — see the course-or-combo block above.
        var comboId = comboIdValue is > 0 ? comboIdValue!.Value.ToString(CultureInfo.InvariantCulture) : "";
        var courseId = courseIdValue is > 0 ? courseIdValue!.Value.ToString(CultureInfo.InvariantCulture) : "";
        var orderId = BuildOrderId(req);

        var url = creds.ApiBaseUrl.TrimEnd('/') + RegisterPath;

        // Audit string of what we sent. Auth is header-only, so no credential appears here.
        var rawRequest = JsonSerializer.Serialize(new
        {
            url,
            auth = "<redacted header: Authorization/X-API-Key>",
            class_id = classId,
            course_id = courseId,
            combo_id = comboId,
            view_type = viewType,
            value = cfg.Value,
            validity_type = validityType,
            validity_days_date = validityDaysDate,
            fullname = req.CustomerName,
            email = req.CustomerEmail,
            country_code = countryCode,
            mobile,
            pincode = req.CustomerPincode ?? "",
            order_id = orderId,
        });

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(classId.ToString(CultureInfo.InvariantCulture)), "class_id" },
                { new StringContent(courseId), "course_id" },
                { new StringContent(comboId), "combo_id" },
                { new StringContent(viewType.ToString(CultureInfo.InvariantCulture)), "view_type" },
                { new StringContent(cfg.Value!.Value.ToString(CultureInfo.InvariantCulture)), "value" },
                { new StringContent(validityType.ToString(CultureInfo.InvariantCulture)), "validity_type" },
                { new StringContent(validityDaysDate), "validity_days_date" },
                { new StringContent(req.CustomerName), "fullname" },
                { new StringContent(req.CustomerEmail!), "email" },
                { new StringContent(countryCode), "country_code" },
                { new StringContent(mobile), "mobile" },
                { new StringContent(req.CustomerPincode ?? ""), "pincode" },
                { new StringContent(orderId), "order_id" },
            };

            var client = _httpFactory.CreateClient("superclass");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            var authToken = ResolveAuthToken(creds.ApiKey, classId);
            if (!string.IsNullOrWhiteSpace(authToken))
                httpReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {authToken}");

            using var resp = await client.SendAsync(httpReq, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            return ParseResult(resp.IsSuccessStatusCode, (int)resp.StatusCode, body, rawRequest,
                expiresAt, courseId, comboId, req);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine cancellation
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Superclass GENERATE exception orderItemId={OrderItemId}", req.OrderItemId);
            return new GenerateKeyResult
            {
                Success = false,
                ErrorCode = "exception",
                ErrorMessage = ex.Message,
                RawRequest = rawRequest,
            };
        }
    }

    public Task<ActivateKeyResult> ActivateAsync(string serialKey, string? tenantRef, CancellationToken ct)
        => Task.FromResult(ActivateKeyResult.Fail("not_supported",
            "Superclass provisions access at registration time; there is no separate activation endpoint."));

    public Task<KeyStatusResult> GetStatusAsync(string serialKey, string? tenantRef, CancellationToken ct)
        => Task.FromResult(KeyStatusResult.Fail("not_supported", "Superclass does not expose a status endpoint."));

    // ── Auth ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The configured Superclass secret is EITHER a ready-to-use API token (sent verbatim) OR the
    /// RSA private key Superclass issues per institute. In the private-key case their auth
    /// middleware expects a per-request RS256-signed JWT as the Bearer token — it decodes the JWT
    /// payload, so sending the raw PEM yields the vendor's <c>"Failed to decode token payload"</c>
    /// (HTTP 402). We detect a PEM private key and mint a short-lived signed JWT; anything else is
    /// treated as a literal token so a future "paste a ready token" setup keeps working unchanged.
    /// </summary>
    private string ResolveAuthToken(string? secret, int classId)
    {
        if (string.IsNullOrWhiteSpace(secret)) return string.Empty;
        if (!secret.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            return secret.Trim();   // literal API token / pre-issued JWT

        try
        {
            return BuildSignedJwt(secret, classId);
        }
        catch (Exception ex)
        {
            // Don't throw — the provider contract is never-throw. Fall back to the raw secret so the
            // vendor's own error still surfaces (rather than us swallowing it as an exception).
            _log.LogError(ex, "Superclass JWT signing from private key failed; sending raw secret as fallback.");
            return secret.Trim();
        }
    }

    /// <summary>Mint the RS256 JWT Superclass expects: header <c>{"alg":"RS256","typ":"JWT"}</c> and
    /// a single string claim <c>{"class_id":"&lt;id&gt;"}</c> — NO expiry or other claims. This exactly
    /// reproduces the vendor's issued token (verified byte-for-byte against their sample), so it is a
    /// static, long-lived credential per class id. Hand-rolled to avoid a JWT package dependency.</summary>
    private static string BuildSignedJwt(string pemPrivateKey, int classId)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pemPrivateKey);

        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT" };
        var payload = new Dictionary<string, object> { ["class_id"] = classId.ToString(CultureInfo.InvariantCulture) };

        var signingInput = B64Url(JsonSerializer.SerializeToUtf8Bytes(header))
                         + "." + B64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + B64Url(signature);
    }

    private static string B64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ── Helpers ─────────────────────────────────────────────────────────────

    private GenerateKeyResult ParseResult(bool httpOk, int statusCode, string body, string rawRequest,
        DateTime? expiresAt, string courseId, string comboId, GenerateKeyRequest req)
    {
        var raw = SafeRaw(body);

        SuperclassRegisterResponse? parsed = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith("{"))
                parsed = JsonSerializer.Deserialize<SuperclassRegisterResponse>(body, _json);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Superclass response JSON parse failed orderItemId={OrderItemId}; raw kept.", req.OrderItemId);
        }

        var courseRef = string.IsNullOrEmpty(comboId) ? courseId : $"combo {comboId}";

        if (parsed != null)
        {
            var studentId = ResolveStudentId(parsed);
            var isError = parsed.StatusCode >= 400;
            // Success = an explicit 2xx, or (defensively) any non-error status that still returned a
            // student id — that id IS the access grant, so treat its presence as success even if a
            // future env uses a different positive status code.
            var isSuccess = !isError && (parsed.StatusCode is 200 or 201 || studentId != null);

            if (isSuccess)
            {
                _log.LogInformation("Superclass GENERATE OK orderItemId={OrderItemId} status={Status} studentId={StudentId}",
                    req.OrderItemId, parsed.StatusCode, studentId);
                return GenerateKeyResult.OkRegistration(
                    externalRef: studentId,
                    subscriptionStatus: "active",
                    expiresAt: expiresAt,
                    courseRef: courseRef,
                    rawRequest: rawRequest,
                    rawResponse: raw);
            }

            var msg = string.IsNullOrWhiteSpace(parsed.Message)
                ? $"Superclass returned status {parsed.StatusCode}."
                : parsed.Message!;

            // Duplicate order_id ⇒ the student was already registered for this course on a prior attempt
            // whose response we lost. Treat as already-provisioned success (order_id is unique per course),
            // so we don't dead-letter a registration that actually happened.
            if (LooksLikeDuplicateOrder(msg))
            {
                _log.LogWarning("Superclass GENERATE duplicate order_id treated as already-provisioned orderItemId={OrderItemId} msg={Msg}",
                    req.OrderItemId, msg);
                return GenerateKeyResult.OkRegistration(
                    externalRef: studentId,
                    subscriptionStatus: "already_provisioned",
                    expiresAt: expiresAt,
                    courseRef: courseRef,
                    rawRequest: rawRequest,
                    rawResponse: raw);
            }

            // Real vendor error — surface THEIR message + status code so the admin sees the actual
            // cause (e.g. "Failed to decode token payload") instead of a generic parse failure.
            _log.LogError("Superclass GENERATE error orderItemId={OrderItemId} status={Status} msg={Msg}",
                req.OrderItemId, parsed.StatusCode, msg);
            return new GenerateKeyResult
            {
                Success = false,
                ErrorCode = parsed.StatusCode > 0 ? $"superclass_{parsed.StatusCode}" : "superclass_error",
                ErrorMessage = msg,
                RawRequest = rawRequest,
                RawResponse = raw,
            };
        }

        // Body wasn't JSON at all (empty / HTML error page / gateway timeout). If the HTTP call
        // itself failed, report that; else report an unparseable body.
        var code = httpOk ? "bad_response" : $"http_{statusCode}";
        var message = httpOk
            ? "Superclass response could not be parsed (not JSON). Inspect RawResponse."
            : $"Superclass returned HTTP {statusCode}.";
        _log.LogError("Superclass GENERATE {Code} orderItemId={OrderItemId}", code, req.OrderItemId);
        return new GenerateKeyResult
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            RawRequest = rawRequest,
            RawResponse = raw,
        };
    }

    /// <summary>student_id may be top-level or nested under "data" (an object, or the first element
    /// of an array) depending on Superclass environment. Returns null when absent.</summary>
    private static string? ResolveStudentId(SuperclassRegisterResponse parsed)
    {
        if (parsed.StudentId is { } top) return top.ToString(CultureInfo.InvariantCulture);

        var data = parsed.Data;
        if (data.ValueKind == JsonValueKind.Object && TryGetStudentId(data, out var id)) return id;
        if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 && TryGetStudentId(data[0], out var id2)) return id2;
        return null;
    }

    private static bool TryGetStudentId(JsonElement el, out string? id)
    {
        id = null;
        if (el.ValueKind != JsonValueKind.Object) return false;
        if (!el.TryGetProperty("student_id", out var s)) return false;
        id = s.ValueKind switch
        {
            JsonValueKind.Number => s.GetRawText(),
            JsonValueKind.String => s.GetString(),
            _ => null,
        };
        return !string.IsNullOrEmpty(id);
    }

    /// <summary>Unique, stable-across-retries order reference. Composite of the human order number and a
    /// short slice of the order-item id so two different courses in the SAME order get distinct order_ids
    /// (Superclass dedupes on order_id). Stable across retries because both inputs are fixed per record.</summary>
    private static string BuildOrderId(GenerateKeyRequest req)
    {
        var itemPart = req.OrderItemId.ToString("N")[..8];
        return string.IsNullOrWhiteSpace(req.OrderNumber)
            ? $"ORD-{req.OrderItemId:N}"
            : $"{req.OrderNumber}-{itemPart}";
    }

    /// <summary>Splits a stored phone into (country_code, mobile). Uses the request's country code when
    /// present (default "91"). Strips non-digits from the phone; if it still carries the country code
    /// prefix (length &gt; 10), keeps the last 10 digits as the local mobile number.</summary>
    private static (string countryCode, string mobile) NormalisePhone(string? countryCode, string phone)
    {
        var cc = string.IsNullOrWhiteSpace(countryCode) ? "91" : new string(countryCode.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(cc)) cc = "91";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length > 10 && digits.StartsWith(cc)) digits = digits[cc.Length..];
        if (digits.Length > 10) digits = digits[^10..];
        return (cc, digits);
    }

    private static bool LooksLikeDuplicateOrder(string message)
    {
        var m = message.ToLowerInvariant();
        return m.Contains("duplicate") && m.Contains("order");
    }

    private SuperclassProductConfig ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new SuperclassProductConfig();
        try
        {
            return JsonSerializer.Deserialize<SuperclassProductConfig>(json, _json) ?? new SuperclassProductConfig();
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Superclass config JSON parse failed; using defaults.");
            return new SuperclassProductConfig();
        }
    }

    /// <summary>Keeps the persisted RawResponse valid for a jsonb column (wrap non-JSON as a JSON string).</summary>
    private static string SafeRaw(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "null";
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return body;
        return JsonSerializer.Serialize(body);
    }
}
