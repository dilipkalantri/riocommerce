using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Leads;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

public interface ILeadService
{
    Task CaptureAsync(CaptureLeadRequest req);
    Task<PagedResult<LeadRow>> ListAsync(LeadFilter filter);
    Task UpdateStatusAsync(Guid id, LeadStatus status);
    Task<int> NewCountAsync();

    // ── Admin: source separation + export ────────────────────────────────────────────────────
    /// <summary>Totals per origin, for the tab badges.</summary>
    Task<LeadSourceCounts> CountsBySourceAsync(CancellationToken ct = default);

    /// <summary>Blogs that have produced at least one lead, newest-titled first. Drives the Blog
    /// filter dropdown, so it can never offer an option that returns nothing.</summary>
    Task<List<BlogLeadSourceOption>> BlogsWithLeadsAsync(CancellationToken ct = default);

    /// <summary>Excel workbook of the leads matching <paramref name="filter"/> — the same filter the
    /// grid is showing, so an export always mirrors what is on screen. Column set adapts to the
    /// source (blog exports add Blog Title/Slug).</summary>
    Task<byte[]> ExportXlsxAsync(LeadFilter filter, CancellationToken ct = default);

    // ── Admin CRM ────────────────────────────────────────────────────────────────────────────
    /// <summary>Full lead record plus its complete note timeline. Null when the lead isn't found.</summary>
    Task<LeadDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);

    /// <summary>Schedules (or clears, when null) the next follow-up date.</summary>
    Task UpdateFollowUpAsync(Guid id, DateTime? nextFollowUpAt, CancellationToken ct = default);

    /// <summary>Appends a note. Never mutates existing notes. Returns the new note id, or null if the
    /// lead is missing or the body is blank.</summary>
    Task<Guid?> AddNoteAsync(Guid leadId, AddLeadNoteRequest req, Guid? actorId, string actorName, CancellationToken ct = default);

    /// <summary>Rewrites one note's body and stamps UpdatedAt. Returns false if not found/blank.</summary>
    Task<bool> UpdateNoteAsync(Guid noteId, UpdateLeadNoteRequest req, CancellationToken ct = default);

    /// <summary>Removes a single note. Returns false when it no longer exists.</summary>
    Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken ct = default);
}
