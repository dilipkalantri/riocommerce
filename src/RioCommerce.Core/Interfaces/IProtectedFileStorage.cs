namespace RioCommerce.Core.Interfaces;

// Persists files OUTSIDE wwwroot so static-file middleware can't reach them.
// The only legitimate read path is a controller that resolves the relative
// path against the configured private root after authorising the request
// (see BookPreviewController for the canonical use case).
public interface IProtectedFileStorage
{
    /// <summary>Save the stream and return a relative path (e.g. "book-previews/&lt;guid&gt;.pdf").</summary>
    Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken ct = default);

    /// <summary>Resolve the absolute physical path from a relative one. Returns null if the path escapes the private root.</summary>
    string? Resolve(string relativePath);

    /// <summary>Delete a stored file by its relative path. Silently no-ops if the path is outside the private root.</summary>
    void Delete(string? relativePath);
}
