using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

// Writes files to {contentRoot}/secure-files/<folder>/<guid><ext>.
// This path is DELIBERATELY outside wwwroot so the static-file pipeline can't
// reach it — every read has to go through a controller that signs/validates a
// short-lived token (see BookPreviewController).
public sealed class LocalProtectedFileStorage : IProtectedFileStorage
{
    public const string BaseSegment = "secure-files";

    private readonly string _root;

    public LocalProtectedFileStorage(string contentRoot)
    {
        _root = Path.GetFullPath(Path.Combine(contentRoot, BaseSegment));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken ct = default)
    {
        var safeFolder = Sanitize(folder);
        var name = $"{Guid.NewGuid():N}{NormalizeExt(extension)}";
        var dir = Path.Combine(_root, safeFolder);
        Directory.CreateDirectory(dir);
        await using var fs = File.Create(Path.Combine(dir, name));
        await content.CopyToAsync(fs, ct);
        return $"{safeFolder}/{name}";   // relative — never carries the absolute root
    }

    public string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var combined = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        // Path-traversal guard: refuse anything that escapes the configured root.
        return combined.StartsWith(_root, StringComparison.Ordinal) && File.Exists(combined) ? combined : null;
    }

    public void Delete(string? relativePath)
    {
        var full = string.IsNullOrWhiteSpace(relativePath) ? null : Resolve(relativePath);
        if (full != null) File.Delete(full);
    }

    private static string Sanitize(string s) =>
        string.Concat((s ?? "").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')) is { Length: > 0 } v ? v : "misc";
    private static string NormalizeExt(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return "";
        ext = ext.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return string.Concat(ext.Where(c => char.IsLetterOrDigit(c) || c == '.'));
    }
}
