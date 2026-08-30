using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.SerialKeys.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.SerialKeys;

/// <summary>
/// Syncs Valence packs from the LMS <c>get_packs</c> endpoint into the local <c>valence_packs</c>
/// table and lists them for the admin UI. Manual-refresh only (no scheduler) per product decision.
/// </summary>
public class ValencePackService : IValencePackService
{
    private const string GetPacksPathFormat = "/index.php/{0}/get_packs";
    private const string SaveProductPackPathFormat = "/index.php/{0}/save_product_pack";

    private readonly RioCommerceDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ValencePackService> _log;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ValencePackService(
        RioCommerceDbContext db,
        IHttpClientFactory httpFactory,
        ILogger<ValencePackService> log)
    {
        _db = db;
        _httpFactory = httpFactory;
        _log = log;
    }

    public async Task<List<ValencePackItem>> ListAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        var q = _db.ValencePacks.AsNoTracking();
        if (activeOnly) q = q.Where(p => p.IsActive);
        return await q
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.PackName)
            .Select(p => new ValencePackItem
            {
                ExternalId = p.ExternalId,
                PackName = p.PackName,
                Tags = p.Tags,
                IsActive = p.IsActive,
                LastSyncedAt = p.LastSyncedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<ValencePackSyncResult> SyncAsync(string? baseUrl, string? pathSegment, CancellationToken ct = default)
    {
        // Resolve credentials: explicit args win; otherwise pull from any active Valence product
        // config's ConfigJson (BaseUrl + PathSegment).
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(pathSegment))
        {
            var (cfgBase, cfgPath) = await ResolveFromExistingConfigAsync(ct);
            baseUrl ??= cfgBase;
            pathSegment ??= cfgPath;
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(pathSegment))
            return ValencePackSyncResult.Fail(
                "No Valence BaseUrl/PathSegment available. Provide them, or save a Valence product config first.");

        // GetPacksPathFormat starts with "/index.php/", so the base must not end with it — see ValenceUrl.
        var url = ValenceUrl.Base(baseUrl) + string.Format(GetPacksPathFormat, pathSegment);

        ValencePacksResponse? parsed;
        string body;
        try
        {
            var client = _httpFactory.CreateClient("valence");
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            using var resp = await client.SendAsync(req, ct);
            body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Valence get_packs http {Status}", (int)resp.StatusCode);
                return ValencePackSyncResult.Fail($"Valence returned HTTP {(int)resp.StatusCode}.");
            }

            parsed = JsonSerializer.Deserialize<ValencePacksResponse>(body, _json);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Valence get_packs request/parse failed.");
            return ValencePackSyncResult.Fail($"Sync failed: {ex.Message}");
        }

        if (parsed is null || !parsed.IsSuccess)
            return ValencePackSyncResult.Fail(parsed?.Message ?? "Valence returned a non-success status.");

        var items = parsed.Data ?? new List<ValencePackDto>();
        var now = DateTime.UtcNow;
        var result = new ValencePackSyncResult { Success = true, Fetched = items.Count };

        // Load existing once for an in-memory upsert (pack counts are small).
        var existing = await _db.ValencePacks.ToDictionaryAsync(p => p.ExternalId, ct);
        var seen = new HashSet<int>();

        foreach (var dto in items)
        {
            if (dto.Id == 0) continue; // skip unparseable id
            seen.Add(dto.Id);

            if (existing.TryGetValue(dto.Id, out var row))
            {
                row.PackName = dto.PackName ?? row.PackName;
                row.Tags = dto.Tags;
                row.IsActive = true;
                row.LastSyncedAt = now;
                row.UpdatedAt = now;
                result.Updated++;
            }
            else
            {
                _db.ValencePacks.Add(new ValencePack
                {
                    Id = Guid.NewGuid(),
                    ExternalId = dto.Id,
                    PackName = dto.PackName ?? $"Pack {dto.Id}",
                    Tags = dto.Tags,
                    IsActive = true,
                    LastSyncedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                result.Inserted++;
            }
        }

        // Deactivate packs that vanished from this sync (keep history, don't delete).
        foreach (var (extId, row) in existing)
        {
            if (!seen.Contains(extId) && row.IsActive)
            {
                row.IsActive = false;
                row.UpdatedAt = now;
                result.Deactivated++;
            }
        }

        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Valence pack sync: fetched={Fetched} inserted={Ins} updated={Upd} deactivated={Deact}",
            result.Fetched, result.Inserted, result.Updated, result.Deactivated);
        return result;
    }

    public async Task<(bool ok, string? error)> MapProductToPackAsync(
        string baseUrl, string pathSegment, string courseId, string productName, int packExternalId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(pathSegment))
            return (false, "Valence BaseUrl/PathSegment are required to map the product.");

        // THE 404 FIX. This line used to concatenate the base verbatim, so a Base URL ending in
        // "/index.php" produced ".../index.php/index.php/{seg}/save_product_pack" → HTTP 404, and the
        // product↔pack mapping could never be created. Registration normalised the base; this did not.
        var url = ValenceUrl.Base(baseUrl) + string.Format(SaveProductPackPathFormat, pathSegment);
        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(productName ?? string.Empty), "course_name" },
                { new StringContent(courseId), "course_id" },
                { new StringContent(packExternalId.ToString()), "pack_id" },
            };

            var client = _httpFactory.CreateClient("valence");
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            using var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                _log.LogError("Valence save_product_pack http {Status} product={Course}", (int)resp.StatusCode, courseId);
                return (false, $"Valence returned HTTP {(int)resp.StatusCode} mapping the product.");
            }

            ValenceResponse? parsed = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(body) && body.TrimStart().StartsWith("{"))
                    parsed = JsonSerializer.Deserialize<ValenceResponse>(body, _json);
            }
            catch (JsonException) { /* fall through — handled below */ }

            var status = parsed?.Status;
            var message = parsed?.Message;

            // Success, or the idempotent "already mapped" case.
            if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message, "This combination already exists", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogInformation("Valence product-pack mapped course={Course} pack={Pack}", courseId, packExternalId);
                return (true, null);
            }

            _log.LogError("Valence save_product_pack failed course={Course} msg={Msg}", courseId, message);
            return (false, message ?? "Valence rejected the product-pack mapping.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Valence save_product_pack exception course={Course}", courseId);
            return (false, $"Mapping request failed: {ex.Message}");
        }
    }

    private async Task<(string? BaseUrl, string? PathSegment)> ResolveFromExistingConfigAsync(CancellationToken ct)
    {
        var configs = await _db.ProductSerialKeyConfigs
            .AsNoTracking()
            .Where(c => c.ProviderKey == "valence" && c.IsActive)
            .Select(c => c.ConfigJson)
            .ToListAsync(ct);

        foreach (var json in configs)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}") continue;
            try
            {
                var cfg = JsonSerializer.Deserialize<ValenceProductConfig>(json, _json);
                if (!string.IsNullOrWhiteSpace(cfg?.BaseUrl) && !string.IsNullOrWhiteSpace(cfg.PathSegment))
                    return (cfg.BaseUrl, cfg.PathSegment);
            }
            catch (JsonException) { /* skip malformed config */ }
        }
        return (null, null);
    }
}
