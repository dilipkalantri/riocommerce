using System.Text.Json;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class UserPreferenceService : IUserPreferenceService
{
    private readonly RioCommerceDbContext _db;
    public UserPreferenceService(RioCommerceDbContext db) => _db = db;

    public Task<string?> GetRawAsync(Guid userId, string key) =>
        _db.UserPreferences.Where(p => p.UserId == userId && p.Key == key).Select(p => p.ValueJson).FirstOrDefaultAsync();

    public async Task<T?> GetAsync<T>(Guid userId, string key)
    {
        var raw = await GetRawAsync(userId, key);
        if (string.IsNullOrEmpty(raw)) return default;
        try { return JsonSerializer.Deserialize<T>(raw); } catch { return default; }
    }

    public async Task SetAsync<T>(Guid userId, string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var pref = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key);
        if (pref == null)
        {
            pref = new Core.Entities.UserPreference { UserId = userId, Key = key };
            _db.UserPreferences.Add(pref);
        }
        pref.ValueJson = json;
        await _db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid userId, string key)
    {
        var pref = await _db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key);
        if (pref != null) { _db.UserPreferences.Remove(pref); await _db.SaveChangesAsync(); }
    }
}
