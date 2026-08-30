namespace RioCommerce.Core.DTOs.Orders;

public record NoteAttachmentDto(
    Guid Id, Guid OrderNoteId, string OriginalFileName, string ContentType, long FileSize,
    string Kind, string UploadedByName, DateTime CreatedAt);

public record NoteAttachmentDownload(string OriginalFileName, string ContentType, string StoragePath);
