namespace RioCommerce.Core.Interfaces;

// Validates uploads (extension/content-type whitelist + size cap) and classifies files for the UI.
public interface IAttachmentSecurityService
{
    (bool ok, string? error) Validate(string fileName, string? contentType, long size);

    /// <summary>image | pdf | doc | sheet | archive | file — drives icons/preview behaviour.</summary>
    string Kind(string fileName, string? contentType);

    long MaxBytes { get; }
}
