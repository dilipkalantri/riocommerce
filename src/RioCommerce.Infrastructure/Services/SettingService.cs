using System.Globalization;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

// Cached, typed, audited configuration provider over AppSettings. Single source of truth — SiteSettings /
// IntegrationSettings / FinanceSettings all persist into the same app_settings table; this layer adds the
// cache, typed accessors, encryption, change history and audit that the rest of the platform reads through.
public class SettingService : ISettingService
{
    private const string Mask = "••••••";
    private readonly RioCommerceDbContext _db;
    private readonly SettingsCache _cache;
    private readonly IAuditService _audit;
    private readonly IRealtimeBus _bus;
    private readonly IDataProtector _protector;

    public SettingService(RioCommerceDbContext db, SettingsCache cache, IAuditService audit, IRealtimeBus bus, IDataProtectionProvider dp)
    {
        _db = db; _cache = cache; _audit = audit; _bus = bus;
        _protector = dp.CreateProtector("RioCommerce.Settings.Secrets.v1");
    }

    private async Task EnsureLoadedAsync()
    {
        if (_cache.Loaded) return;
        var all = await _db.AppSettings.Select(a => new { a.Key, a.Value }).ToListAsync();
        _cache.Load(all.ToDictionary(a => a.Key, a => (string?)a.Value));
    }

    public async Task<string?> GetStringAsync(string key)
    {
        await EnsureLoadedAsync();
        return _cache.TryGet(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback = 0m)
        => decimal.TryParse(await GetStringAsync(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    public async Task<int> GetIntAsync(string key, int fallback = 0)
        => int.TryParse(await GetStringAsync(key), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    public async Task<bool> GetBoolAsync(string key, bool fallback = false)
    {
        var s = await GetStringAsync(key);
        return s == null ? fallback : s == "true";
    }

    public async Task<TEnum> GetEnumAsync<TEnum>(string key, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(await GetStringAsync(key), out var e) ? e : fallback;

    public async Task<string?> GetSecretAsync(string key) => Dec(await GetStringAsync(key));

    public Task SetAsync(string key, string? value, string category, Guid? actorId, string actorName, bool secret = false)
        => SetManyAsync(new[] { new SettingWrite(key, value, secret) }, category, actorId, actorName);

    public async Task<int> SetManyAsync(IEnumerable<SettingWrite> writes, string category, Guid? actorId, string actorName)
    {
        await EnsureLoadedAsync();
        var changed = new List<string>();

        foreach (var w in writes)
        {
            var existing = await _db.AppSettings.FirstOrDefaultAsync(a => a.Key == w.Key);
            var oldStored = existing?.Value;
            var oldLogical = w.Secret ? Dec(oldStored) : oldStored;
            var newLogical = w.Value;
            if (string.Equals(oldLogical ?? "", newLogical ?? "", StringComparison.Ordinal)) continue;   // no-op

            var newStored = w.Secret ? Enc(newLogical) : newLogical;
            if (existing == null)
                _db.AppSettings.Add(new AppSetting { Key = w.Key, Value = newStored ?? "", Category = category, UpdatedAt = DateTime.UtcNow });
            else { existing.Value = newStored ?? ""; existing.Category = category; existing.UpdatedAt = DateTime.UtcNow; }

            _db.SettingHistory.Add(new SettingHistory
            {
                Key = w.Key, Category = category, WasSecret = w.Secret,
                OldValue = w.Secret ? (string.IsNullOrEmpty(oldLogical) ? "" : Mask) : oldLogical,
                NewValue = w.Secret ? (string.IsNullOrEmpty(newLogical) ? "" : Mask) : newLogical,
                ChangedById = actorId, ChangedByName = actorName
            });
            _cache.Set(w.Key, newStored);
            changed.Add(w.Key);
        }

        if (changed.Count == 0) return 0;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "SettingsUpdated", "AppSetting", category,
            JsonSerializer.Serialize(new { category, keys = changed }));
        _bus.PublishDataChanged(new RealtimeEvent("settings"));
        return changed.Count;
    }

    public async Task<List<SettingHistoryDto>> HistoryAsync(string key, int take = 20)
        => await _db.SettingHistory.Where(h => h.Key == key).OrderByDescending(h => h.CreatedAt).Take(take)
            .Select(h => new SettingHistoryDto(h.Key, h.OldValue, h.NewValue, h.WasSecret, h.ChangedByName, h.CreatedAt))
            .ToListAsync();

    public async Task<List<SettingHistoryDto>> HistoryByCategoryAsync(string category, int take = 50)
        => await _db.SettingHistory.Where(h => h.Category == category).OrderByDescending(h => h.CreatedAt).Take(take)
            .Select(h => new SettingHistoryDto(h.Key, h.OldValue, h.NewValue, h.WasSecret, h.ChangedByName, h.CreatedAt))
            .ToListAsync();

    public void InvalidateCache() => _cache.Clear();

    private string? Enc(string? v) => string.IsNullOrEmpty(v) ? v : _protector.Protect(v);
    private string? Dec(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        try { return _protector.Unprotect(v); }
        catch { return v; }   // tolerate legacy plaintext
    }
}
