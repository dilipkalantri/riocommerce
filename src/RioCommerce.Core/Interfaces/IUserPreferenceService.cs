namespace RioCommerce.Core.Interfaces;

// Per-user key→JSON preference store (grid layouts, saved filters, page size, …).
public interface IUserPreferenceService
{
    Task<T?> GetAsync<T>(Guid userId, string key);
    Task<string?> GetRawAsync(Guid userId, string key);
    Task SetAsync<T>(Guid userId, string key, T value);
    Task RemoveAsync(Guid userId, string key);
}
