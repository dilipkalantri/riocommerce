namespace RioCommerce.Core.Interfaces;

public record StoredFile(string StoragePath, string StoredFileName, long Size, string Sha256);

// Storage abstraction — LocalFileStorageService now; an S3/Azure Blob implementation can drop in unchanged.
public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(string folder, string extension, Stream content, CancellationToken ct = default);

    /// <summary>Opens a stored file for reading; returns null if missing or the path escapes the storage root.</summary>
    Stream? Open(string storagePath);

    void Delete(string storagePath);
}
