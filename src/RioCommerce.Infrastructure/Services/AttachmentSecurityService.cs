using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

public class AttachmentSecurityService : IAttachmentSecurityService
{
    public long MaxBytes => 10 * 1024 * 1024;   // 10 MB

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt", ".zip"
    };

    public (bool ok, string? error) Validate(string fileName, string? contentType, long size)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return (false, "File has no name.");
        if (size <= 0) return (false, "File is empty.");
        if (size > MaxBytes) return (false, $"File exceeds the {MaxBytes / (1024 * 1024)} MB limit.");
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !Allowed.Contains(ext))
            return (false, $"File type '{ext}' is not allowed.");
        return (true, null);
    }

    public string Kind(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "image",
            ".pdf" => "pdf",
            ".doc" or ".docx" or ".txt" => "doc",
            ".xls" or ".xlsx" or ".csv" => "sheet",
            ".zip" => "archive",
            _ => "file"
        };
    }
}
