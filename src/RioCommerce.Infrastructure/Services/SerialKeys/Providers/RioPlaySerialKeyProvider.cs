using System.Security.Cryptography;
using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// RioPlay (Antargyan) provider. Uses the <b>simple</b> documented token flow per the PDF:
/// signup → token → create-or-create+activate. Drops the OAuth-style code-exchange dance
/// that the nopCommerce plugin used.
///
/// <para>Call sequence on <see cref="GenerateAsync"/>:</para>
/// <list type="number">
///   <item><c>POST /Api/Account/NopSignUp</c> — idempotent on Rio's side. We send a random
///         password we never persist; Rio's user lookup is by <c>NopGuid</c> anyway.</item>
///   <item><c>POST /Api/Account/GenerateTokenForNop</c> — returns a bearer token.</item>
///   <item>If product is auto-activate → <c>POST /Api/Account/GenerateSerialKeyForNop</c> with bearer.<br/>
///         Else → <c>POST /Api/Account/CreateSerialKeyForNop</c> (no bearer).</item>
/// </list>
/// Body for steps 3a/3b is always wrapped: <c>{ "Entity": { ...fields... } }</c>. Flat bodies
/// are silently rejected by Rio.
/// </summary>
public class RioPlaySerialKeyProvider : ISerialKeyProvider
{
    public string Key => "rioplay";
    public string DisplayName => "RioPlay (Antargyan)";
    public bool SupportsActivate => true;
    public bool SupportsStatusLookup => false;   // Rio's PDF doesn't expose a status endpoint.

    private const string PathSignUp     = "/Api/Account/NopSignUp";
    private const string PathToken      = "/Api/Account/GenerateTokenForNop";
    private const string PathCreate     = "/Api/Account/CreateSerialKeyForNop";
    private const string PathCreateAct  = "/Api/Account/GenerateSerialKeyForNop";
    private const string PathActivate   = "/Api/Services/Workspace/ActivationLog/ActivateSerialKeyForNop";

    private readonly RioPlayApiClient _api;
    private readonly IRioPlayTenantService _tenants;
    private readonly ILogger<RioPlaySerialKeyProvider> _log;
    private readonly JsonSerializerOptions _json;

    public RioPlaySerialKeyProvider(
        RioPlayApiClient api,
        IRioPlayTenantService tenants,
        ILogger<RioPlaySerialKeyProvider> log)
    {
        _api = api;
        _tenants = tenants;
        _log = log;
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public async Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest req, CancellationToken ct)
    {
        // ── Resolve tenant credentials ──────────────────────────────────────
        Guid? tid = Guid.TryParse(req.TenantRef, out var parsed) ? parsed : null;
        var creds = await _tenants.ResolveCredentialsAsync(tid, ct);
        if (creds == null)
        {
            _log.LogError("Rio GENERATE no tenant orderItemId={OrderItemId} tenantRef={TenantRef}", req.OrderItemId, req.TenantRef);
            return GenerateKeyResult.Fail("no_tenant", "No active RioPlay tenant configured.");
        }

        var nopGuid = req.UserId ?? Guid.NewGuid();

        // ── Single call: CreateSerialKeyForNop ──────────────────────────────
        // Body matches the confirmed working RestSharp sample from Antargyan: the flat PDF field
        // set WRAPPED in an { "Entity": { ... } } envelope, headers tenantid + secretKey only,
        // no bearer. Includes DeviceId as an all-zeros GUID (present in the working sample).
        var cfg = ParseConfig(req.ConfigJson);
        var entity = BuildEntity(req, nopGuid, cfg, creds.TenantId);
        var envelope = new RioEntityEnvelope<RioSerialKeyEntity> { Entity = entity };
        var rawBody = JsonSerializer.Serialize(envelope, _json);

        var keyOutcome = await _api.PostRawAsync<RioSerialKeyResponse>(
            creds.BaseUrl, PathCreate, rawBody, creds.TenantId, creds.Secret,
            bearer: null, ct);

        // CreateSerialKeyForNop's documented response is a BARE string (the serial key). Accept
        // both that and the JSON-object shape.
        string? serialKey = keyOutcome.Value?.SerialKey;
        short? statusCode = keyOutcome.Value?.SerialkeyStatus;
        if (string.IsNullOrWhiteSpace(serialKey)
            && !string.IsNullOrWhiteSpace(keyOutcome.RawResponse)
            && keyOutcome.ErrorCode != "http_404"
            && (keyOutcome.Success || keyOutcome.ErrorCode == "parse_error"))
        {
            var candidate = keyOutcome.RawResponse.Trim().Trim('"').Trim();
            if (candidate.Length is > 0 and < 256
                && !candidate.StartsWith("{") && !candidate.StartsWith("[")
                && !candidate.StartsWith("<") && !candidate.Contains(' '))
            {
                serialKey = candidate;
                statusCode ??= 2;
                _log.LogInformation("Rio CREATE returned bare-string key orderItemId={OrderItemId}", req.OrderItemId);
            }
        }

        if (string.IsNullOrWhiteSpace(serialKey))
        {
            _log.LogError("Rio CREATE failed orderItemId={OrderItemId} code={Code} msg={Msg}",
                req.OrderItemId, keyOutcome.ErrorCode, keyOutcome.ErrorMessage);
            return new GenerateKeyResult
            {
                Success = false,
                ErrorCode = keyOutcome.ErrorCode ?? "create_failed",
                ErrorMessage = keyOutcome.ErrorMessage ?? "Rio returned no SerialKey.",
                RawRequest = rawBody,
                RawResponse = keyOutcome.RawResponse,
            };
        }

        var activated = statusCode == 3;
        _log.LogInformation("Rio CREATE OK orderItemId={OrderItemId} key={Key} status={Status}",
            req.OrderItemId, serialKey, statusCode);

        return new GenerateKeyResult
        {
            Success = true,
            SerialKey = serialKey,
            ExternalReference = statusCode?.ToString(),
            Activated = activated,
            RawRequest = rawBody,
            RawResponse = keyOutcome.RawResponse,
        };
    }

    public async Task<ActivateKeyResult> ActivateAsync(string serialKey, string? tenantRef, CancellationToken ct)
    {
        Guid? tid = Guid.TryParse(tenantRef, out var parsed) ? parsed : null;
        var creds = await _tenants.ResolveCredentialsAsync(tid, ct);
        if (creds == null)
            return ActivateKeyResult.Fail("no_tenant", "No active RioPlay tenant configured.");

        // Activate also requires a bearer token. Rio doesn't say which user's token, so we use
        // a fresh anonymous-ish signup keyed off a deterministic guid derived from the key —
        // gives idempotent token retrieval without polluting Rio with one new user per call.
        var nopGuid = DeterministicGuid("rio-activate-" + serialKey);
        var tokenReq = new RioGenerateTokenRequest { NopGuid = nopGuid };
        // Signup first (idempotent), then token. Same dance as Generate.
        await _api.PostJsonAsync<RioNopSignUpRequest, RioNopSignUpResponse>(
            creds.BaseUrl, PathSignUp,
            new RioNopSignUpRequest
            {
                NopGuid = nopGuid,
                DisplayName = "Activator",
                Email = $"activator+{nopGuid:N}@example.com",
                MobileNumber = "0000000000",
            },
            creds.TenantId, creds.Secret, bearer: null, ct);
        var tokenOutcome = await _api.PostJsonAsync<RioGenerateTokenRequest, RioGenerateTokenResponse>(
            creds.BaseUrl, PathToken, tokenReq, creds.TenantId, creds.Secret, bearer: null, ct);
        if (!tokenOutcome.Success || string.IsNullOrWhiteSpace(tokenOutcome.Value?.Token))
            return ActivateKeyResult.Fail(tokenOutcome.ErrorCode ?? "token_failed", tokenOutcome.ErrorMessage ?? "Rio token request failed.", tokenOutcome.RawResponse);

        var outcome = await _api.PostJsonAsync<RioActivateRequest, RioActivateResponse>(
            creds.BaseUrl, PathActivate,
            new RioActivateRequest { SerialKey = serialKey },
            creds.TenantId, creds.Secret, bearer: tokenOutcome.Value!.Token, ct);

        if (!outcome.Success || outcome.Value == null)
            return ActivateKeyResult.Fail(outcome.ErrorCode ?? "activate_failed", outcome.ErrorMessage ?? "Rio activate returned no EntityId.", outcome.RawResponse);

        return ActivateKeyResult.Ok(outcome.Value.EntityId?.ToString(), outcome.RawResponse ?? "");
    }

    public Task<KeyStatusResult> GetStatusAsync(string serialKey, string? tenantRef, CancellationToken ct)
        => Task.FromResult(KeyStatusResult.Fail("not_supported", "RioPlay does not expose a status endpoint."));

    // ── Helpers ─────────────────────────────────────────────────────────────

    private RioPlayProductConfig ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return new RioPlayProductConfig();
        try
        {
            return JsonSerializer.Deserialize<RioPlayProductConfig>(json, _json) ?? new RioPlayProductConfig();
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Rio config JSON parse failed; falling back to defaults. raw={Raw}", json);
            return new RioPlayProductConfig();
        }
    }

    private static RioSerialKeyEntity BuildEntity(GenerateKeyRequest req, Guid nopGuid, RioPlayProductConfig cfg, int fallbackTenantId)
        => new()
        {
            // Field set matches the confirmed-working RestSharp sample from Antargyan:
            //   { Quantity, Owner, Product, UpdateFrequencyInDays, EOpens, EWatchTime, EValidity,
            //     AccessStatus, AllowedDevices, DeviceId, EValidityType, AnalyticsRequired,
            //     IsLiveClassIncluded, SwitichingDevice }
            // Note: the working sample does NOT send TenantId in the body — Rio resolves the tenant
            // from the tenantid header. So we leave TenantId null (omitted via WhenWritingNull).
            Quantity = Math.Max(1, req.Quantity),
            Owner = req.SerialOrderId,
            Product = int.TryParse(req.ProviderProductCode, out var pc) ? pc : null,
            UpdateFrequencyInDays = cfg.UpdateFrequencyInDays,
            EOpens = cfg.EOpens ?? 10,
            EWatchTime = cfg.EWatchTime ?? 6,
            EValidity = cfg.EValidity ?? 42,
            AccessStatus = cfg.AccessStatus ?? 1,
            AllowedDevices = string.IsNullOrWhiteSpace(cfg.AllowedDevices) ? "1" : cfg.AllowedDevices,
            DeviceId = Guid.Empty,
            EValidityType = cfg.EValidityType ?? 2,
            // EWatchTimeType (Rio enum: 1=RealTime, 2=AcceleratedTime). Only sent when the admin
            // sets it in config; left null (omitted) otherwise to match the working sample exactly.
            EWatchTimeType = cfg.EWatchTimeType,
            AnalyticsRequired = cfg.AnalyticsRequired ?? true,
            IsLiveClassIncluded = cfg.IsLiveClassIncluded ?? true,
            SwitichingDevice = cfg.SwitichingDevice,
            // TenantId intentionally left null — not in the working body.
        };

    /// <summary>Deterministic Guid from a string — same input always yields the same Guid, so the
    /// "anonymous activator" user we sign up is created once and reused.</summary>
    private static Guid DeterministicGuid(string input)
    {
        var bytes = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        // RFC 4122 v5-ish: take 16 bytes, set version/variant nibbles.
        var g = new byte[16];
        Array.Copy(bytes, g, 16);
        g[6] = (byte)((g[6] & 0x0F) | 0x50);
        g[8] = (byte)((g[8] & 0x3F) | 0x80);
        return new Guid(g);
    }
}
