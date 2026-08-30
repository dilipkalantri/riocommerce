using System.Text.Json;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class NoteAttachmentService : INoteAttachmentService
{
    private const string EntityType = "Order";
    private readonly RioCommerceDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly IAttachmentSecurityService _security;
    private readonly IAuditService _audit;
    private readonly IRealtimeBus _bus;

    public NoteAttachmentService(RioCommerceDbContext db, IFileStorageService storage, IAttachmentSecurityService security,
        IAuditService audit, IRealtimeBus bus)
    {
        _db = db; _storage = storage; _security = security; _audit = audit; _bus = bus;
    }

    public async Task<(bool ok, string? error, Guid? id)> AttachAsync(
        Guid orderId, Guid noteId, string fileName, string? contentType, long size, Stream content,
        Guid? actorId, string actorName)
    {
        var (ok, error) = _security.Validate(fileName, contentType, size);
        if (!ok) return (false, error, null);

        var note = await _db.OrderNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.OrderId == orderId);
        if (note == null) return (false, "Note not found.", null);

        var stored = await _storage.SaveAsync(orderId.ToString("N"), Path.GetExtension(fileName), content);
        var att = new NoteAttachment
        {
            OrderNoteId = noteId,
            OrderId = orderId,
            FileName = stored.StoredFileName,
            OriginalFileName = Path.GetFileName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSize = stored.Size,
            StoragePath = stored.StoragePath,
            HashChecksum = stored.Sha256,
            UploadedByName = actorName,
            UploadedById = actorId
        };
        _db.NoteAttachments.Add(att);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "AttachmentUploaded", EntityType, orderId.ToString(),
            JsonSerializer.Serialize(new { att.OriginalFileName, att.FileSize, att.HashChecksum }));
        _bus.PublishDataChanged(new RealtimeEvent("order", orderId.ToString()));
        return (true, null, att.Id);
    }

    public async Task<List<NoteAttachmentDto>> ListForOrderAsync(Guid orderId)
    {
        var rows = await _db.NoteAttachments.Where(a => a.OrderId == orderId && !a.IsDeleted)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.OrderNoteId, a.OriginalFileName, a.ContentType, a.FileSize, a.UploadedByName, a.CreatedAt })
            .ToListAsync();
        return rows.Select(a => new NoteAttachmentDto(
            a.Id, a.OrderNoteId, a.OriginalFileName, a.ContentType, a.FileSize,
            _security.Kind(a.OriginalFileName, a.ContentType), a.UploadedByName, a.CreatedAt)).ToList();
    }

    public async Task<NoteAttachmentDownload?> GetForDownloadAsync(Guid attachmentId)
    {
        var a = await _db.NoteAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && !x.IsDeleted);
        return a == null ? null : new NoteAttachmentDownload(a.OriginalFileName, a.ContentType, a.StoragePath);
    }

    public async Task<bool> DeleteAsync(Guid attachmentId, Guid? actorId, string actorName)
    {
        var a = await _db.NoteAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId && !x.IsDeleted);
        if (a == null) return false;
        a.IsDeleted = true;
        await _db.SaveChangesAsync();
        _storage.Delete(a.StoragePath);
        await _audit.LogAsync(actorId, actorName, "AttachmentDeleted", EntityType, a.OrderId.ToString(),
            JsonSerializer.Serialize(new { a.OriginalFileName }));
        _bus.PublishDataChanged(new RealtimeEvent("order", a.OrderId.ToString()));
        return true;
    }
}
