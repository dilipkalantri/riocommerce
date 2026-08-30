using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

// Generic, cached, strongly-typed configuration provider over the AppSettings key-value store.
// Reads are served from an in-process singleton cache (loaded once, invalidated on write). Writes are
// audited and recorded in SettingHistory; secret values are encrypted at rest and masked in history.
public interface ISettingService
{
    Task<string?> GetStringAsync(string key);
    Task<decimal> GetDecimalAsync(string key, decimal fallback = 0m);
    Task<int> GetIntAsync(string key, int fallback = 0);
    Task<bool> GetBoolAsync(string key, bool fallback = false);
    Task<TEnum> GetEnumAsync<TEnum>(string key, TEnum fallback) where TEnum : struct, Enum;

    // Decrypts a value stored with secret:true.
    Task<string?> GetSecretAsync(string key);

    Task SetAsync(string key, string? value, string category, Guid? actorId, string actorName, bool secret = false);

    // Batched write: one SaveChanges, one aggregate audit entry, per-key history rows. Returns changed key count.
    Task<int> SetManyAsync(IEnumerable<SettingWrite> writes, string category, Guid? actorId, string actorName);

    Task<List<SettingHistoryDto>> HistoryAsync(string key, int take = 20);
    Task<List<SettingHistoryDto>> HistoryByCategoryAsync(string category, int take = 50);

    void InvalidateCache();
}

// One pending write for SetManyAsync.
public readonly record struct SettingWrite(string Key, string? Value, bool Secret = false);
