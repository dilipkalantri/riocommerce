using RioCommerce.Core.DTOs.Orders;
namespace RioCommerce.Core.Interfaces;

public interface INoteAttachmentService
{
    Task<(bool ok, string? error, Guid? id)> AttachAsync(
        Guid orderId, Guid noteId, string fileName, string? contentType, long size, Stream content,
        Guid? actorId, string actorName);

    Task<List<NoteAttachmentDto>> ListForOrderAsync(Guid orderId);
    Task<NoteAttachmentDownload?> GetForDownloadAsync(Guid attachmentId);
    Task<bool> DeleteAsync(Guid attachmentId, Guid? actorId, string actorName);
}
