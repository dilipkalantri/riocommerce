namespace RioCommerce.Core.Entities;

// A file attached to an order note (payment proof, invoice, refund document, …).
// Files are stored outside wwwroot and served only via an authenticated streaming endpoint.
public class NoteAttachment : BaseEntity
{
    public Guid OrderNoteId { get; set; }
    public Guid OrderId { get; set; }                 // denormalised for fast per-order lookups + access checks
    public string FileName { get; set; } = string.Empty;          // stored (GUID) name on disk — never the user's name
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSize { get; set; }
    public string StoragePath { get; set; } = string.Empty;       // relative path within the storage root
    public string? HashChecksum { get; set; }                     // SHA-256 of the bytes
    public string VirusScanStatus { get; set; } = "NotScanned";   // future-ready malware-scan hook
    public bool IsDeleted { get; set; }
    public string UploadedByName { get; set; } = "system";
    public Guid? UploadedById { get; set; }
    public OrderNote OrderNote { get; set; } = null!;
}
