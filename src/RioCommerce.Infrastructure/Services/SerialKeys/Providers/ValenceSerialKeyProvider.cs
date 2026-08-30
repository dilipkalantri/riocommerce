using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Valence (Edubees) serial-key provider. Registers a student against a course on the Edubees
/// secure LMS in a single multipart/form-data POST; the LMS provisions access and returns a
/// key in the response body.
///
/// <para>Confirmed request (from a working RestSharp sample):</para>
/// <code>
/// POST {BaseUrl}/index.php/{PathSegment}/register_student_with_course
/// Content-Type: multipart/form-data
///   full_name = {customer name}
///   mobile    = {customer phone}
///   email     = {customer email}
///   course    = {provider product code}
///   class_id  = {config ClassId}
///   key_views = {config KeyViews}
/// </code>
///
/// <para>Auth: there is no token/header auth in the sample — the obfuscated <c>{PathSegment}</c>
/// is itself the access secret, so it is held in product config and never logged in full.</para>
///
/// <para>The success response shape is NOT yet confirmed (only the error envelope
/// <c>{"status":"error","message":"..."}</c> has been observed). This provider therefore parses
/// leniently via <see cref="ValenceResponse"/>: it treats an explicit <c>status:"error"</c> as a
/// failure, otherwise looks for a key across several candidate fields. The full raw body is always
/// captured on the result so audit/debugging loses nothing, and so the parse can be tightened to
/// the real field name once a success sample is available (see the TODOs in <see cref="ValenceResponse"/>).</para>
///
/// <para>Per the <see cref="ISerialKeyProvider"/> contract this provider NEVER throws — all HTTP
/// and parse failures become a non-success <see cref="GenerateKeyResult"/>.</para>
/// </summary>
public class ValenceSerialKeyProvider : ISerialKeyProvider
{
    public string Key => "valence";
    public string DisplayName => "Valence (Edubees)";

    // No documented activate or status endpoint yet. Generation creates+provisions in one call,
    // so AutoActivate is effectively always true and there is no separate activation step.
    public bool SupportsActivate => false;
    public bool SupportsStatusLookup => false;

    private const string RegisterPathFormat = "/index.php/{0}/register_student_with_course";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ValenceSerialKeyProvider> _log;
    private readonly JsonSerializerOptions _json;

    public ValenceSerialKeyProvider(IHttpClientFactory httpFactory, ILogger<ValenceSerialKeyProvider> log)
    {
        _httpFactory = httpFactory;
        _log = log;
        _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest req, CancellationToken ct)
    {
        var cfg = ParseConfig(req.ConfigJson);

        // ── Validate config ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.PathSegment))
        {
            _log.LogError("Valence GENERATE missing BaseUrl/PathSegment orderItemId={OrderItemId}", req.OrderItemId);
            return GenerateKeyResult.Fail("no_config",
                "Valence product config is missing BaseUrl or PathSegment.");
        }

        var course = !string.IsNullOrWhiteSpace(cfg.CourseOverride)
            ? cfg.CourseOverride!
            : req.ProviderProductCode;
        if (string.IsNullOrWhiteSpace(course))
        {
            _log.LogError("Valence GENERATE missing course code orderItemId={OrderItemId}", req.OrderItemId);
            return GenerateKeyResult.Fail("no_course",
                "Valence requires a course code (ProviderProductCode or config CourseOverride).");
        }

        // Valence's sample shows required full_name/mobile/email — guard so we fail fast with a
        // clear reason rather than triggering the API's generic "Missing parameters".
        if (string.IsNullOrWhiteSpace(req.CustomerName)
            || string.IsNullOrWhiteSpace(req.CustomerEmail)
            || string.IsNullOrWhiteSpace(req.CustomerPhone))
        {
            _log.LogError("Valence GENERATE missing customer fields orderItemId={OrderItemId} name={HasName} email={HasEmail} phone={HasPhone}",
                req.OrderItemId,
                !string.IsNullOrWhiteSpace(req.CustomerName),
                !string.IsNullOrWhiteSpace(req.CustomerEmail),
                !string.IsNullOrWhiteSpace(req.CustomerPhone));
            return GenerateKeyResult.Fail("missing_customer",
                "Valence requires full_name, email and mobile — one or more is empty on the order.");
        }

        var keyViews = (cfg.KeyViews is > 0 ? cfg.KeyViews.Value : 1).ToString();

        // ── Build the multipart request (matches the working sample) ─────────
        // RegisterPathFormat already includes "/index.php/", so the base must not end with it —
        // see ValenceUrl for why this normalisation is shared rather than written inline here.
        var baseUrl = ValenceUrl.Base(cfg.BaseUrl!);
        var path = string.Format(RegisterPathFormat, cfg.PathSegment);
        var url = baseUrl + path;

        // Audit string of what we sent — never includes the secret path segment in clear text.
        var rawRequest = JsonSerializer.Serialize(new
        {
            url = baseUrl + string.Format(RegisterPathFormat, "***"),
            full_name = req.CustomerName,
            mobile = req.CustomerPhone,
            email = req.CustomerEmail,
            course,
            class_id = cfg.ClassId,
            key_views = keyViews,
        });

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(req.CustomerName), "full_name" },
                { new StringContent(req.CustomerPhone!), "mobile" },
                { new StringContent(req.CustomerEmail!), "email" },
                { new StringContent(course), "course" },
                { new StringContent(keyViews), "key_views" },
            };
            if (!string.IsNullOrWhiteSpace(cfg.ClassId))
                form.Add(new StringContent(cfg.ClassId!), "class_id");

            var client = _httpFactory.CreateClient("valence");
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };

            using var resp = await client.SendAsync(httpReq, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Valence GENERATE http {Status} orderItemId={OrderItemId}", (int)resp.StatusCode, req.OrderItemId);
                return new GenerateKeyResult
                {
                    Success = false,
                    ErrorCode = $"http_{(int)resp.StatusCode}",
                    ErrorMessage = $"Valence returned HTTP {(int)resp.StatusCode}.",
                    RawRequest = rawRequest,
                    RawResponse = SafeRaw(body),
                };
            }

            return ParseResult(body, rawRequest, req);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine cancellation — let the orchestrator handle it.
        }
        catch (Exception ex)
        {
            // Contract: never throw out of the provider — normalise to a failure result.
            _log.LogError(ex, "Valence GENERATE exception orderItemId={OrderItemId}", req.OrderItemId);
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
            "Valence provisions access at registration time; there is no separate activation endpoint."));

    public Task<KeyStatusResult> GetStatusAsync(string serialKey, string? tenantRef, CancellationToken ct)
        => Task.FromResult(KeyStatusResult.Fail("not_supported",
            "Valence does not expose a status endpoint."));

    // ── Helpers ─────────────────────────────────────────────────────────────

    private GenerateKeyResult ParseResult(string body, string rawRequest, GenerateKeyRequest req)
    {
        var raw = SafeRaw(body);

        ValenceResponse? parsed = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith("{"))
                parsed = JsonSerializer.Deserialize<ValenceResponse>(body, _json);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Valence response JSON parse failed orderItemId={OrderItemId}; raw kept.", req.OrderItemId);
        }

        // Explicit error envelope → failure with Valence's own message.
        if (parsed?.IsExplicitError == true)
        {
            _log.LogError("Valence GENERATE error orderItemId={OrderItemId} msg={Msg}", req.OrderItemId, parsed.Message);
            return new GenerateKeyResult
            {
                Success = false,
                ErrorCode = "valence_error",
                ErrorMessage = parsed.Message ?? "Valence returned an error.",
                RawRequest = rawRequest,
                RawResponse = raw,
            };
        }

        // Locate a key among the candidate fields.
        var key = parsed?.AnyKey;

        // Fallback: a bare-string key (non-JSON body), mirroring how Rio can return a bare key.
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(body))
        {
            var candidate = body.Trim().Trim('"').Trim();
            // Reject obvious non-keys: generic status words that Valence may echo identically for every
            // registration (which would collide on our unique SerialKey index). A real key is expected to
            // be a longer, opaque token — require some length and reject known status words.
            var looksLikeStatusWord = candidate.Length < 8
                || candidate.Equals("success", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("ok", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("true", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("done", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("registered", StringComparison.OrdinalIgnoreCase);
            if (candidate.Length is > 0 and < 256
                && !looksLikeStatusWord
                && !candidate.StartsWith("{") && !candidate.StartsWith("[")
                && !candidate.StartsWith("<") && !candidate.Contains(' '))
            {
                key = candidate;
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            // No recognisable key and no explicit error — surface as a failure so the orchestrator
            // retries/dead-letters rather than recording a blank key. The raw body is attached so
            // the actual success field can be identified and the parser tightened.
            _log.LogError("Valence GENERATE no key located orderItemId={OrderItemId}; raw kept for inspection.", req.OrderItemId);
            return new GenerateKeyResult
            {
                Success = false,
                ErrorCode = "no_key",
                ErrorMessage = "Valence response contained no recognisable key field. Inspect RawResponse and map the correct field.",
                RawRequest = rawRequest,
                RawResponse = raw,
            };
        }

        _log.LogInformation("Valence GENERATE OK orderItemId={OrderItemId}", req.OrderItemId);
        return new GenerateKeyResult
        {
            Success = true,
            SerialKey = key,
            ExternalReference = parsed?.AnyReference,
            Activated = true, // single-call registration provisions immediately.
            RawRequest = rawRequest,
            RawResponse = raw,
        };
    }

    private ValenceProductConfig ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new ValenceProductConfig();
        try
        {
            return JsonSerializer.Deserialize<ValenceProductConfig>(json, _json) ?? new ValenceProductConfig();
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Valence config JSON parse failed; using defaults.");
            return new ValenceProductConfig();
        }
    }

    /// <summary>Ensures the persisted RawResponse is valid for a jsonb column: if the body isn't
    /// JSON, wrap it as a JSON string. Mirrors the orchestrator's jsonb-safety requirement.</summary>
    private static string SafeRaw(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "null";
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return body;
        return JsonSerializer.Serialize(body); // wrap plain text / HTML as a JSON string literal.
    }
}
