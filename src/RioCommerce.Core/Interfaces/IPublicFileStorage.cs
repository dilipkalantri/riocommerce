namespace RioCommerce.Core.Interfaces;

// Storage for publicly-served assets (product pictures, etc.) — written under wwwroot and returned as a
// web URL. Distinct from IFileStorageService, which keeps private files outside wwwroot behind auth.
public interface IPublicFileStorage
{
    /// <summary>Saves a file under the given folder and returns its public URL (e.g. /uploads/products/ab12.jpg).</summary>
    Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken ct = default);

    /// <summary>Deletes a previously-saved public file by its URL. No-op if it isn't a managed upload.</summary>
    void Delete(string? url);
}
