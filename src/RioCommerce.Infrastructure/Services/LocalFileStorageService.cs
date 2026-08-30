using System.Security.Cryptography;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

// Stores files under a private root (outside wwwroot). Swap for an S3/Azure implementation with no caller changes.
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;
    public LocalFileStorageService(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredFile> SaveAsync(string folder, string extension, Stream content, CancellationToken ct = default)
    {
        // Buffer (uploads are capped) so we can hash + write atomically; never trust the client filename on disk.
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var safeFolder = SanitizeSegment(folder);
        var storedName = $"{Guid.NewGuid():N}{NormalizeExt(extension)}";
        var relative = $"{safeFolder}/{storedName}";
        var full = Path.Combine(_root, safeFolder, storedName);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes, ct);
        return new StoredFile(relative, storedName, bytes.Length, sha);
    }

    public Stream? Open(string storagePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storagePath));
        if (!full.StartsWith(_root, StringComparison.Ordinal)) return null;   // path-traversal guard
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    public void Delete(string storagePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, storagePath));
        if (full.StartsWith(_root, StringComparison.Ordinal) && File.Exists(full)) File.Delete(full);
    }

    private static string SanitizeSegment(string s) =>
        string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
    private static string NormalizeExt(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return "";
        ext = ext.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return string.Concat(ext.Where(c => char.IsLetterOrDigit(c) || c == '.'));
    }
}
