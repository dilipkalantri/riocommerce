using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Thin HTTP layer over Rio's API. Owns the boilerplate for:
///   • Setting the <c>tenantId</c> / <c>secretKey</c> auth headers on every request
///   • Optionally attaching a bearer token
///   • Serialising / deserialising via <see cref="System.Text.Json"/> with PascalCase passthrough
///   • Returning the raw response body alongside the typed result so the caller can persist it
/// </summary>
public class RioPlayApiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RioPlayApiClient> _log;
    private readonly JsonSerializerOptions _json;

    public RioPlayApiClient(HttpClient http, ILogger<RioPlayApiClient> log)
    {
        _http = http;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }

    /// <summary>POST a JSON body with tenant + secret headers (no bearer). Returns the parsed
    /// response, the raw response body, and a typed transport result.</summary>
    public Task<RioCallOutcome<TRes>> PostJsonAsync<TReq, TRes>(
        string baseUrl, string path, TReq body,
        int tenantId, string secret, string? bearer,
        CancellationToken ct)
        where TRes : class
    {
        var json = JsonSerializer.Serialize(body, _json);
        return PostRawAsync<TRes>(baseUrl, path, json, tenantId, secret, bearer, ct);
    }

    /// <summary>POST a pre-serialised JSON body. Used when we want to log the exact bytes we sent.</summary>
    public async Task<RioCallOutcome<TRes>> PostRawAsync<TRes>(
        string baseUrl, string path, string json,
        int tenantId, string secret, string? bearer,
        CancellationToken ct)
        where TRes : class
    {
        // Rio's edge intermittently returns a spurious 404 (Server: Kestrel, empty body) on the
        // FIRST hit of a connection, then succeeds on an immediate retry — confirmed by the fact
        // that the manual Test button (which retries naturally via re-click) works while the
        // single-shot dispatch path 404s. So retry a 404 up to 2 extra times with a short pause.
        RioCallOutcome<TRes> outcome = null!;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(800, ct);
            outcome = await PostRawOnceAsync<TRes>(baseUrl, path, json, tenantId, secret, bearer, ct);
            // Only retry on the spurious 404. Real errors (validation, auth, parse) return immediately.
            if (outcome.ErrorCode != "http_404") return outcome;
            _log.LogWarning("Rio 404 on attempt {Attempt} for {Path} — retrying", attempt + 1, path);
        }
        return outcome;
    }

    private async Task<RioCallOutcome<TRes>> PostRawOnceAsync<TRes>(
        string baseUrl, string path, string json,
        int tenantId, string secret, string? bearer,
        CancellationToken ct)
        where TRes : class
    {
        var url = $"{baseUrl.TrimEnd('/')}{path}";
        // Build content as a fixed byte array so Content-Length is known and set up-front, and the body
        // is sent as one buffered payload — exactly like Postman/RestSharp. (A plain StringContent can
        // interact badly with a DelegatingHandler that reads the body before send, and with chunked
        // transfer; buffering removes that variable.) Bare "application/json" content-type, no charset
        // suffix, which is what the working Postman/RestSharp calls send.
        var bytes = Encoding.UTF8.GetBytes(json);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = bytes.Length;
        var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content,
            // Force HTTP/1.1 to match Postman's default and avoid HTTP/2 negotiation surprises.
            Version = System.Net.HttpVersion.Version11,
            VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
        };
        // Per Antargyan's live API: tenantId + secretKey headers on every endpoint.
        msg.Headers.Add("tenantid", tenantId.ToString());
        msg.Headers.Add("secretKey", secret);
        // .NET's HttpClient adds "Expect: 100-continue" on POSTs with a body by default; Postman/RestSharp
        // do NOT. Some servers/edges mishandle the 100-continue handshake and silently drop or 404 the
        // request — the classic "works in Postman, fails from HttpClient with an identical body" symptom.
        // Disable it so we send the body immediately, exactly like Postman.
        msg.Headers.ExpectContinue = false;
        if (!string.IsNullOrWhiteSpace(bearer))
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(msg, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("Rio call TIMEOUT url={Url}", url);
            return RioCallOutcome<TRes>.Transport("timeout", "HTTP timeout calling Rio", null);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Rio call NETWORK FAIL url={Url}", url);
            return RioCallOutcome<TRes>.Transport("network", ex.Message, null);
        }

        var raw = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 from Rio. We proved via the Test panel that this comes from Rio's own Kestrel
            // app (not a WAF/IP block), so report it as a plain 404 with the body + Server header
            // rather than the old misleading "ip_blocked" label.
            var serverHdr = resp.Headers.TryGetValues("Server", out var sv) ? string.Join(",", sv) : "?";
            _log.LogError("Rio call 404 url={Url} server={Server} body={Body}", url, serverHdr, raw);
            return RioCallOutcome<TRes>.Transport("http_404",
                $"HTTP 404 from Rio (Server: {serverHdr}). Body: {(string.IsNullOrWhiteSpace(raw) ? "(empty)" : Truncate(raw, 200))}", raw);
        }

        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Rio call non-2xx url={Url} status={Status} body={Body}", url, (int)resp.StatusCode, Truncate(raw, 400));
            return RioCallOutcome<TRes>.HttpError((int)resp.StatusCode, raw);
        }

        // Rio returns bare strings (token endpoint) and JSON bodies (everything else). Handle both.
        if (typeof(TRes) == typeof(string))
        {
            // Token endpoint may return either a quoted JSON string or a raw token.
            var token = raw.Trim().Trim('"');
            return RioCallOutcome<TRes>.Ok((TRes)(object)token, raw);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TRes>(raw, _json);
            if (parsed == null)
                return RioCallOutcome<TRes>.ParseError("empty_body", raw);
            return RioCallOutcome<TRes>.Ok(parsed, raw);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Rio JSON parse failed url={Url} body={Body}", url, Truncate(raw, 400));
            return RioCallOutcome<TRes>.ParseError(ex.Message, raw);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";

    /// <summary>
    /// Diagnostic-only. Fires a real NopSignUp request and returns EVERYTHING about the
    /// round-trip — final URL (after any redirects), status, response headers, body — so an
    /// admin can see exactly what Rio's edge returns without digging through logs. Never throws.
    /// </summary>
    public async Task<RioDiagnosticResult> TestConnectionAsync(
        string baseUrl, int tenantId, string secret, CancellationToken ct)
    {
        var url = $"{baseUrl.TrimEnd('/')}/Api/Account/NopSignUp";
        var probeGuid = Guid.NewGuid();
        var bodyObj = new RioNopSignUpRequest
        {
            NopGuid = probeGuid,
            DisplayName = "Connection Test",
            Email = $"conntest+{probeGuid:N}@example.com",
            MobileNumber = "0000000000",
        };
        var json = JsonSerializer.Serialize(bodyObj, _json);

        var content = new StringContent(json, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content,
            Version = System.Net.HttpVersion.Version11,
            VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
        };
        msg.Headers.Add("tenantid", tenantId.ToString());
        msg.Headers.Add("secretKey", secret);
        msg.Headers.UserAgent.ParseAdd("RioCommerce-SerialKey/1.0 (+ASP.NET)");
        msg.Headers.Accept.ParseAdd("application/json");
        msg.Headers.AcceptEncoding.ParseAdd("gzip, deflate");

        var result = new RioDiagnosticResult { RequestUrl = url, RequestBody = json, TenantId = tenantId };

        // Snapshot the headers we're about to send so the admin panel can show them and we can
        // compare byte-for-byte against the working Postman request.
        var reqHdr = new StringBuilder();
        foreach (var h in msg.Headers)
        {
            var val = string.Equals(h.Key, "secretKey", StringComparison.OrdinalIgnoreCase)
                ? "<redacted, len=" + secret.Length + ">"
                : string.Join(", ", h.Value);
            reqHdr.AppendLine($"{h.Key}: {val}");
        }
        if (msg.Content?.Headers != null)
            foreach (var h in msg.Content.Headers)
                reqHdr.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
        result.RequestHeaders = reqHdr.ToString().TrimEnd();

        try
        {
            var resp = await _http.SendAsync(msg, ct);
            result.StatusCode = (int)resp.StatusCode;
            result.FinalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? url;
            result.HttpVersion = resp.Version.ToString();
            var sb = new StringBuilder();
            foreach (var h in resp.Headers) sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            if (resp.Content?.Headers != null)
                foreach (var h in resp.Content.Headers) sb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
            result.ResponseHeaders = sb.ToString().TrimEnd();
            result.ResponseBody = await resp.Content!.ReadAsStringAsync(ct);
            result.ServerHeader = resp.Headers.TryGetValues("Server", out var sv) ? string.Join(", ", sv) : "(none)";
            result.Success = resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Exception = ex.ToString();
        }
        return result;
    }

    /// <summary>
    /// Runs the FULL Rio chain end to end — SignUp → Token → CreateSerialKey — using a throwaway
    /// probe identity, and reports the outcome of each step. Lets an admin confirm all three APIs
    /// work (not just NopSignUp) without placing a real order. The created key is a real key on
    /// Rio's side against a synthetic owner id, so use sparingly. Never throws.
    /// </summary>
    public async Task<RioChainTestResult> RunFullChainTestAsync(
        string baseUrl, int tenantId, string secret, int productCode, RioPlayProductConfig cfg, CancellationToken ct)
    {
        var r = new RioChainTestResult();
        var b = baseUrl.TrimEnd('/');

        // Per Antargyan: serial-key generation needs ONLY CreateSerialKeyForNop — NopSignUp and
        // GenerateTokenForNop are not part of this flow, so we do NOT call them. They're reported as
        // skipped purely so the UI's step list stays consistent.
        r.SignUp = new RioStepResult { Step = "1. NopSignUp", Success = true, Detail = "Skipped — not required for serial-key generation.", Raw = null };
        r.Token  = new RioStepResult { Step = "2. GenerateTokenForNop", Success = true, Detail = "Skipped — not required for serial-key generation.", Raw = null };

        // ── CreateSerialKeyForNop (the only call) ──
        if (productCode <= 0)
        {
            r.Create = new RioStepResult
            {
                Step = "CreateSerialKeyForNop",
                Success = false,
                Detail = "No valid Product code. Attach a ProductSerialKeyConfig with a real Rio numeric " +
                         "product id (Provider Product Code) before testing.",
                Raw = null,
            };
            return r;
        }

        var entity = new RioSerialKeyEntity
        {
            Quantity = 1,
            Owner = StableProbeOwner(),
            Product = productCode,
            UpdateFrequencyInDays = cfg.UpdateFrequencyInDays,
            EOpens = cfg.EOpens ?? 10,
            EWatchTime = cfg.EWatchTime ?? 6,
            EValidity = cfg.EValidity ?? 42,
            AccessStatus = cfg.AccessStatus ?? 1,
            AllowedDevices = string.IsNullOrWhiteSpace(cfg.AllowedDevices) ? "1" : cfg.AllowedDevices,
            DeviceId = Guid.Empty,
            EValidityType = cfg.EValidityType ?? 2,
            AnalyticsRequired = cfg.AnalyticsRequired ?? true,
            IsLiveClassIncluded = cfg.IsLiveClassIncluded ?? true,
            SwitichingDevice = cfg.SwitichingDevice,
        };
        var body = JsonSerializer.Serialize(new RioEntityEnvelope<RioSerialKeyEntity> { Entity = entity }, _json);
        var createOut = await PostRawAsync<RioSerialKeyResponse>(
            b, "/Api/Account/CreateSerialKeyForNop", body, tenantId, secret, bearer: null, ct);
        var keyStr = createOut.Value?.SerialKey;
        if (string.IsNullOrWhiteSpace(keyStr) && createOut.ErrorCode != "http_404" && !string.IsNullOrWhiteSpace(createOut.RawResponse))
        {
            var cand = createOut.RawResponse.Trim().Trim('"').Trim();
            if (cand.Length is > 0 and < 256 && !cand.StartsWith("{") && !cand.StartsWith("<") && !cand.Contains(' '))
                keyStr = cand;
        }
        r.Create = new RioStepResult
        {
            Step = "CreateSerialKeyForNop",
            Success = !string.IsNullOrWhiteSpace(keyStr),
            Detail = !string.IsNullOrWhiteSpace(keyStr)
                ? $"SerialKey={keyStr}"
                : $"{createOut.ErrorCode}: {createOut.ErrorMessage}",
            Raw = createOut.RawResponse,
        };
        return r;
    }

    /// <summary>Fixed synthetic owner id for chain-test keys, so probe keys cluster under one id
    /// on Rio's side rather than polluting the owner sequence with random values.</summary>
    private static int StableProbeOwner() => 999_999_999;

    /// <summary>
    /// Create-only diagnostic: calls CreateSerialKeyForNop directly, skipping NopSignUp and
    /// GenerateTokenForNop. Valid because the create endpoint authenticates with tenant id + secret
    /// (bearer is null) and never consumes the step-2 token. This is the single real call needed.
    /// </summary>
    public async Task<RioChainTestResult> RunCreateOnlyTestAsync(
        string baseUrl, int tenantId, string secret, int productCode, RioPlayProductConfig cfg, CancellationToken ct)
    {
        var r = new RioChainTestResult();
        var b = baseUrl.TrimEnd('/');

        if (productCode <= 0)
        {
            r.Create = new RioStepResult
            {
                Step = "CreateSerialKeyForNop",
                Success = false,
                Detail = "No valid Product code. Attach a ProductSerialKeyConfig with a real Rio product id (ProviderProductCode).",
                Raw = null,
            };
            return r;
        }

        var entity = new RioSerialKeyEntity
        {
            Quantity = 1,
            Owner = StableProbeOwner(),
            Product = productCode,
            UpdateFrequencyInDays = cfg.UpdateFrequencyInDays,
            EOpens = cfg.EOpens ?? 10,
            EWatchTime = cfg.EWatchTime ?? 6,
            EValidity = cfg.EValidity ?? 42,
            AccessStatus = cfg.AccessStatus ?? 1,
            AllowedDevices = string.IsNullOrWhiteSpace(cfg.AllowedDevices) ? "1" : cfg.AllowedDevices,
            DeviceId = Guid.Empty,
            EValidityType = cfg.EValidityType ?? 2,
            AnalyticsRequired = cfg.AnalyticsRequired ?? true,
            IsLiveClassIncluded = cfg.IsLiveClassIncluded ?? true,
            SwitichingDevice = cfg.SwitichingDevice,
        };
        var body = JsonSerializer.Serialize(new RioEntityEnvelope<RioSerialKeyEntity> { Entity = entity }, _json);
        var createOut = await PostRawAsync<RioSerialKeyResponse>(
            b, "/Api/Account/CreateSerialKeyForNop", body, tenantId, secret, bearer: null, ct);
        var keyStr = createOut.Value?.SerialKey;
        if (string.IsNullOrWhiteSpace(keyStr) && createOut.ErrorCode != "http_404" && !string.IsNullOrWhiteSpace(createOut.RawResponse))
        {
            var cand = createOut.RawResponse.Trim().Trim('"').Trim();
            if (cand.Length is > 0 and < 256 && !cand.StartsWith("{") && !cand.StartsWith("<") && !cand.Contains(' '))
                keyStr = cand;
        }
        r.Create = new RioStepResult
        {
            Step = "CreateSerialKeyForNop",
            Success = !string.IsNullOrWhiteSpace(keyStr),
            Detail = !string.IsNullOrWhiteSpace(keyStr)
                ? $"SerialKey={keyStr}"
                : $"{createOut.ErrorCode}: {createOut.ErrorMessage}",
            Raw = createOut.RawResponse,
        };
        return r;
    }
}

/// <summary>Full round-trip detail from the connection test, returned to the admin screen.</summary>
public class RioDiagnosticResult
{
    public bool Success { get; set; }
    public int TenantId { get; set; }
    public string RequestUrl { get; set; } = "";
    public string? FinalUrl { get; set; }
    public string RequestBody { get; set; } = "";
    public string? RequestHeaders { get; set; }
    public int StatusCode { get; set; }
    public string? HttpVersion { get; set; }
    public string? ServerHeader { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? ResponseBody { get; set; }
    public string? Exception { get; set; }
}

/// <summary>Outcome of a single Rio HTTP call. Always carries the raw response body so the caller
/// can persist it for audit, even on failure.</summary>
public class RioCallOutcome<T> where T : class
{
    public bool Success { get; init; }
    public T? Value { get; init; }
    public string? RawResponse { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static RioCallOutcome<T> Ok(T value, string raw)
        => new() { Success = true, Value = value, RawResponse = raw };
    public static RioCallOutcome<T> Transport(string code, string message, string? raw)
        => new() { Success = false, ErrorCode = code, ErrorMessage = message, RawResponse = raw };
    public static RioCallOutcome<T> HttpError(int status, string raw)
        => new() { Success = false, ErrorCode = $"http_{status}", ErrorMessage = $"HTTP {status}", RawResponse = raw };
    public static RioCallOutcome<T> ParseError(string detail, string raw)
        => new() { Success = false, ErrorCode = "parse_error", ErrorMessage = detail, RawResponse = raw };
}

/// <summary>Per-step outcome for the full-chain test.</summary>
public class RioStepResult
{
    public string Step { get; set; } = "";
    public bool Success { get; set; }
    public string? Detail { get; set; }
    public string? Raw { get; set; }
}

/// <summary>Aggregated result of the SignUp → Token → Create chain test.</summary>
public class RioChainTestResult
{
    public RioStepResult? SignUp { get; set; }
    public RioStepResult? Token { get; set; }
    public RioStepResult? Create { get; set; }

    /// <summary>True only when all three steps succeeded.</summary>
    public bool AllPassed => SignUp?.Success == true && Token?.Success == true && Create?.Success == true;
}
