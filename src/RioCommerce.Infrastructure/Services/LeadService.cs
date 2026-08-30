using ClosedXML.Excel;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Leads;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class LeadService : ILeadService
{
    private readonly RioCommerceDbContext _db;
    public LeadService(RioCommerceDbContext db) => _db = db;

    // NOTE: website lead capture is deliberately untouched by the CRM work — a captured lead still
    // lands with exactly the same fields and Status.New. NextFollowUpAt/notes start empty and are
    // only ever populated from the admin panel.
    public async Task CaptureAsync(CaptureLeadRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.FullName) || string.IsNullOrWhiteSpace(r.Phone)) return;
        _db.Leads.Add(new Lead
        {
            FullName = r.FullName.Trim(),
            Phone = r.Phone.Trim(),
            City = string.IsNullOrWhiteSpace(r.City) ? null : r.City.Trim(),
            CourseInterest = r.CourseInterest,
            Message = string.IsNullOrWhiteSpace(r.Message) ? null : r.Message.Trim(),
            Source = string.IsNullOrWhiteSpace(r.Source) ? "website" : r.Source,
            LeadSource = r.LeadSource,
            // Only blog leads carry a post reference; anything else is forced null so a
            // mis-supplied id on a non-blog capture cannot create a bogus link.
            BlogPostId = r.LeadSource == LeadSource.Blog ? r.BlogPostId : null,
            Status = LeadStatus.New
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Shared predicate for the list and the export, so an exported file can never contain rows
    /// the grid was not showing. Every clause runs in SQL — nothing is filtered in memory.
    /// </summary>
    private IQueryable<Lead> Filtered(LeadFilter filter)
    {
        var q = _db.Leads.AsQueryable();

        if (filter.Source.HasValue) q = q.Where(l => l.LeadSource == filter.Source);
        if (filter.BlogPostId.HasValue) q = q.Where(l => l.BlogPostId == filter.BlogPostId);
        if (filter.Status.HasValue) q = q.Where(l => l.Status == filter.Status);

        if (!string.IsNullOrWhiteSpace(filter.CourseInterest))
        {
            var c = filter.CourseInterest.Trim();
            q = q.Where(l => l.CourseInterest == c);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            // Blog title is searchable too, via the navigation property — still one SQL join,
            // no rows pulled client-side.
            q = q.Where(l => EF.Functions.ILike(l.FullName, $"%{s}%")
                          || EF.Functions.ILike(l.Phone, $"%{s}%")
                          || (l.BlogPost != null && EF.Functions.ILike(l.BlogPost.Title, $"%{s}%")));
        }

        return q;
    }

    public async Task<LeadSourceCounts> CountsBySourceAsync(CancellationToken ct = default)
    {
        // One grouped round trip rather than four counts.
        var g = await _db.Leads.GroupBy(l => l.LeadSource)
            .Select(x => new { Source = x.Key, Count = x.Count() })
            .ToListAsync(ct);

        int N(LeadSource s) => g.FirstOrDefault(x => x.Source == s)?.Count ?? 0;
        return new LeadSourceCounts(N(LeadSource.HomePage), N(LeadSource.Blog),
                                    N(LeadSource.ContactPage), N(LeadSource.Other));
    }

    public async Task<List<BlogLeadSourceOption>> BlogsWithLeadsAsync(CancellationToken ct = default)
    {
        // Driven by leads that exist, not by the blog list, so the dropdown never offers a blog
        // with zero results — and a post that was later unpublished stays filterable.
        //
        // Two plain queries rather than one grouped join: grouping by a navigation property
        // (l.BlogPost.Title) is not translatable and EF throws at runtime. Counting by the raw
        // FK translates cleanly, then the titles are fetched by id.
        var counts = await _db.Leads
            .Where(l => l.BlogPostId != null)
            .GroupBy(l => l.BlogPostId!.Value)
            .Select(g => new { BlogPostId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        if (counts.Count == 0) return new List<BlogLeadSourceOption>();

        var ids = counts.Select(c => c.BlogPostId).ToList();
        var titles = await _db.BlogPosts
            .Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);

        return counts
            // A lead whose blog was deleted has its FK nulled by the FK's ON DELETE SET NULL, so a
            // missing title here would only mean a race; skip rather than show a blank option.
            .Where(c => titles.ContainsKey(c.BlogPostId))
            .Select(c => new BlogLeadSourceOption(c.BlogPostId, titles[c.BlogPostId], c.Count))
            .OrderBy(o => o.Title)
            .ToList();
    }

    public async Task<byte[]> ExportXlsxAsync(LeadFilter filter, CancellationToken ct = default)
    {
        // Same predicate as the grid — an export mirrors exactly what the admin is looking at.
        // Paging is deliberately NOT applied: the export covers the whole filtered set, capped
        // to keep one click from building an unbounded workbook.
        var rows = await Filtered(filter)
            .OrderByDescending(l => l.CreatedAt)
            .Take(10_000)
            .Select(l => new
            {
                l.CreatedAt, l.FullName, l.Phone, l.City, l.CourseInterest, l.Message,
                l.Source, l.LeadSource, l.Status,
                BlogTitle = l.BlogPost != null ? l.BlogPost.Title : null,
                BlogSlug = l.BlogPost != null ? l.BlogPost.Slug : null,
            })
            .ToListAsync(ct);

        var isBlog = filter.Source == LeadSource.Blog;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(isBlog ? "Blog Leads" : "Enquiries");

        // Blog exports add Blog Title + Blog Slug; everything else shares the base set.
        var headers = isBlog
            ? new[] { "Date", "Name", "Phone", "City", "Course / Interest", "Blog Title", "Blog Slug", "Lead Source", "Stage", "Created At" }
            : new[] { "Date", "Name", "Phone", "City", "Course / Interest", "Message", "Lead Source", "Stage", "Created At" };

        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

        var head = ws.Range(1, 1, 1, headers.Length);
        head.Style.Font.Bold = true;
        head.Style.Fill.BackgroundColor = XLColor.FromHtml("#0B45AB");
        head.Style.Font.FontColor = XLColor.White;

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = i + 2;
            var local = r.CreatedAt.ToLocalTime();
            var c = 1;

            ws.Cell(row, c++).Value = local.ToString("dd MMM yyyy");
            ws.Cell(row, c++).Value = r.FullName;
            // Text, not numeric — a leading zero on a phone number must survive.
            ws.Cell(row, c).SetValue(r.Phone).Style.NumberFormat.Format = "@";
            c++;
            ws.Cell(row, c++).Value = r.City ?? "";
            ws.Cell(row, c++).Value = r.CourseInterest ?? "";

            if (isBlog)
            {
                ws.Cell(row, c++).Value = r.BlogTitle ?? "";
                ws.Cell(row, c++).Value = r.BlogSlug ?? "";
            }
            else
            {
                ws.Cell(row, c++).Value = r.Message ?? "";
            }

            ws.Cell(row, c++).Value = SourceLabel(r.LeadSource);
            ws.Cell(row, c++).Value = r.Status.ToString();
            ws.Cell(row, c).SetValue(local.ToString("yyyy-MM-dd HH:mm")).Style.NumberFormat.Format = "@";
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(1, 60d);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string SourceLabel(LeadSource s) => s switch
    {
        LeadSource.HomePage => "Home Page",
        LeadSource.Blog => "Blog",
        LeadSource.ContactPage => "Contact Page",
        _ => "Other",
    };

    public async Task<PagedResult<LeadRow>> ListAsync(LeadFilter filter)
    {
        var q = Filtered(filter);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(l => l.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(l => new LeadRow
            {
                Id = l.Id, FullName = l.FullName, Phone = l.Phone, City = l.City,
                CourseInterest = l.CourseInterest, Message = l.Message, Source = l.Source, Status = l.Status, CreatedAt = l.CreatedAt,
                NextFollowUpAt = l.NextFollowUpAt,
                LeadSource = l.LeadSource, BlogPostId = l.BlogPostId,
                BlogTitle = l.BlogPost != null ? l.BlogPost.Title : null,
                BlogSlug = l.BlogPost != null ? l.BlogPost.Slug : null,
                // Correlated subqueries — one row each, no N+1 round trip.
                LatestNote = l.Notes.OrderByDescending(n => n.CreatedAt).Select(n => n.Body).FirstOrDefault(),
                LatestNoteAt = l.Notes.OrderByDescending(n => n.CreatedAt).Select(n => (DateTime?)n.CreatedAt).FirstOrDefault(),
                NoteCount = l.Notes.Count()
            }).ToListAsync();
        return new PagedResult<LeadRow> { Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize };
    }

    public async Task UpdateStatusAsync(Guid id, LeadStatus status)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id);
        if (lead == null) return;
        lead.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task<int> NewCountAsync() => await _db.Leads.CountAsync(l => l.Status == LeadStatus.New);

    // ── Admin CRM ────────────────────────────────────────────────────────────────────────────

    public async Task<LeadDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead == null) return null;

        // Newest first — the timeline reads top-down as most-recent conversation first.
        var notes = await _db.LeadNotes.AsNoTracking()
            .Where(n => n.LeadId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new LeadNoteItem(n.Id, n.Body, n.CreatedByName, n.CreatedAt, n.UpdatedAt))
            .ToListAsync(ct);

        return new LeadDetail
        {
            Id = lead.Id,
            FullName = lead.FullName,
            Phone = lead.Phone,
            City = lead.City,
            CourseInterest = lead.CourseInterest,
            Message = lead.Message,
            Source = lead.Source,
            Status = lead.Status,
            CreatedAt = lead.CreatedAt,
            NextFollowUpAt = lead.NextFollowUpAt,
            Notes = notes
        };
    }

    public async Task UpdateFollowUpAsync(Guid id, DateTime? nextFollowUpAt, CancellationToken ct = default)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead == null) return;
        // Stored as UTC; the UI binds a local date and converts on the way in.
        lead.NextFollowUpAt = nextFollowUpAt;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Guid?> AddNoteAsync(Guid leadId, AddLeadNoteRequest req, Guid? actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Body)) return null;
        if (!await _db.Leads.AnyAsync(l => l.Id == leadId, ct)) return null;

        var note = new LeadNote
        {
            LeadId = leadId,
            Body = req.Body.Trim(),
            CreatedByUserId = actorId,
            CreatedByName = string.IsNullOrWhiteSpace(actorName) ? "system" : actorName
        };
        _db.LeadNotes.Add(note);
        await _db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task<bool> UpdateNoteAsync(Guid noteId, UpdateLeadNoteRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Body)) return false;
        var note = await _db.LeadNotes.FirstOrDefaultAsync(n => n.Id == noteId, ct);
        if (note == null) return false;
        note.Body = req.Body.Trim();   // UpdatedAt is stamped by the DbContext SaveChanges override.
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _db.LeadNotes.FirstOrDefaultAsync(n => n.Id == noteId, ct);
        if (note == null) return false;
        _db.LeadNotes.Remove(note);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
