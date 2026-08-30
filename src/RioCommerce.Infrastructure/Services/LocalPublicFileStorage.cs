using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

// Writes public assets under {webRoot}/{baseSegment}/<folder>/<guid><ext> and serves them at
// /{baseSegment}/<folder>/<file> via the app's static-file middleware. Swap for S3/CDN later unchanged.
public class LocalPublicFileStorage : IPublicFileStorage
{
    private readonly string _webRoot;
    private readonly string _baseSegment;   // e.g. "uploads"

    public LocalPublicFileStorage(string webRoot, string baseSegment = "uploads")
    {
        _webRoot = Path.GetFullPath(webRoot);
        _baseSegment = baseSegment.Trim('/');
        Directory.CreateDirectory(Path.Combine(_webRoot, _baseSegment));
    }

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken ct = default)
    {
        var safeFolder = Sanitize(folder);
        var name = $"{Guid.NewGuid():N}{NormalizeExt(extension)}";
        var dir = Path.Combine(_webRoot, _baseSegment, safeFolder);
        Directory.CreateDirectory(dir);
        await using (var fs = File.Create(Path.Combine(dir, name)))
            await content.CopyToAsync(fs, ct);
        return $"/{_baseSegment}/{safeFolder}/{name}";
    }

    public void Delete(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var prefix = $"/{_baseSegment}/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal)) return;   // only manage our own uploads
        var relative = url[1..].Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_webRoot, relative));
        var root = Path.Combine(_webRoot, _baseSegment);
        if (full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full)) File.Delete(full);
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
