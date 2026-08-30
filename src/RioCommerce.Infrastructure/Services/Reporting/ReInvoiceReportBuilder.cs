using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §13 — Franchisee Re-Invoice Report. One row per re-invoice product line, carrying both
/// documents' identifiers side by side so it is immediately readable which original invoice
/// produced which re-invoice — the relationship the section exists to establish.
///
/// <para>Line-level rather than document-level because §13 asks for product and quantity detail.
/// The original invoice's number and date repeat down a multi-line re-invoice, which is what makes
/// each row independently traceable when the grid is sorted or exported.</para>
/// </summary>
internal sealed class ReInvoiceReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.FranchiseeReInvoice;

    private readonly RioCommerceDbContext _db;
    public ReInvoiceReportBuilder(RioCommerceDbContext db) => _db = db;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Franchisee",        ReportColumnKind.Text,    "franchise", 1.9f),
        new("Original Inv. No.", ReportColumnKind.Text,    "origno",    1.7f),
        new("Original Date",     ReportColumnKind.Date,    "origdate",  1.1f),
        new("Re-Invoice No.",    ReportColumnKind.Text,    "rino",      1.7f),
        new("Re-Invoice Date",   ReportColumnKind.Date,    "ridate",    1.1f),
        new("Product/Course",    ReportColumnKind.Text,    "product",   2.2f),
        new("Subject",           ReportColumnKind.Text,    "subject",   1.3f),
        new("Qty",               ReportColumnKind.Integer, "qty",       0.6f),
        new("Taxable (₹)",       ReportColumnKind.Money,   "taxable",   1.2f),
        new("CGST (₹)",          ReportColumnKind.Money,   "cgst",      1f),
        new("SGST (₹)",          ReportColumnKind.Money,   "sgst",      1f),
        new("IGST (₹)",          ReportColumnKind.Money,   "igst",      1f),
        new("Total GST (₹)",     ReportColumnKind.Money,   "gst",       1.2f),
        new("Total Amount (₹)",  ReportColumnKind.Money,   "total",     1.3f),
        new("Status",            ReportColumnKind.Status,  "status",    0.9f),
        // Audit pair — WHY the document exists and WHO put it there. Appended after Status rather
        // than woven into the money block so the existing column order (and every saved export
        // consumed downstream) stays byte-stable.
        new("Reason for Change", ReportColumnKind.Text,    "reason",    2.4f),
        new("Raised By",         ReportColumnKind.Text,    "raisedby",  1.8f)
    };

    private sealed record Row(
        string Franchise, string OrigNo, DateOnly OrigDate, string ReNo, DateOnly ReDate,
        string Product, string Subject, int Qty,
        decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Gst, decimal Total,
        ReInvoiceStatus Status, string? Notes, string? CancelledReason,
        string? IssuedByName, Guid? IssuedById);

    /// <summary>
    /// Why this document exists. A re-invoice is never edited — a mistake is corrected by cancelling
    /// and raising a new one — so the reason lives in one of two fields depending on which end of
    /// that lifecycle the row sits at: <c>Notes</c> captured when it was raised, or
    /// <c>CancelledReason</c> captured when it was withdrawn.
    ///
    /// <para>A cancelled row shows the cancellation reason with an explicit prefix. Without it the
    /// two texts are indistinguishable in the column, and "wrong GSTIN" reads identically whether it
    /// explains why the document was raised or why it was killed — the opposite conclusions.</para>
    /// </summary>
    private static string Reason(Row r) =>
        r.Status == ReInvoiceStatus.Cancelled && !string.IsNullOrWhiteSpace(r.CancelledReason)
            ? $"Cancelled — {r.CancelledReason!.Trim()}"
            : ReportSupport.Text(r.Notes);

    /// <summary>Who raised the document. The name is a snapshot taken at issue, so it stays correct
    /// even if the user is renamed or deactivated later; the short id disambiguates two admins who
    /// share a display name. Falls back to the bare id when no name was captured, because an
    /// unattributed row is worse than an ugly one.</summary>
    private static string RaisedBy(Row r)
    {
        var name = string.IsNullOrWhiteSpace(r.IssuedByName) ? null : r.IssuedByName!.Trim();
        var id = r.IssuedById is { } g && g != Guid.Empty ? g.ToString()[..8] : null;

        if (name is null && id is null) return "—";
        if (name is null) return $"#{id}";
        return id is null ? name : $"{name} (#{id})";
    }

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var (from, to) = ReportSupport.ResolveDateOnlyRange(q);

        var ri = _db.FranchiseReInvoices.AsNoTracking()
            .Where(r => r.ReInvoiceDate >= from && r.ReInvoiceDate <= to);

        // Cancelled re-invoices stay out of the default view but remain reachable through the
        // status filter — they are part of the audit trail even though they carry no live tax.
        if (q.ReInvoiceStatuses.Count > 0) ri = ri.Where(r => q.ReInvoiceStatuses.Contains(r.Status));
        else ri = ri.Where(r => r.Status != ReInvoiceStatus.Cancelled);

        if (q.FranchiseScopeId is { } scope) ri = ri.Where(r => r.FranchiseId == scope);
        else if (q.FranchiseIds.Count > 0) ri = ri.Where(r => q.FranchiseIds.Contains(r.FranchiseId));

        var lines = from r in ri
                    join i in _db.FranchiseReInvoiceItems.AsNoTracking() on r.Id equals i.ReInvoiceId
                    select new { R = r, I = i };

        if (q.ProductIds.Count > 0) lines = lines.Where(x => q.ProductIds.Contains(x.I.ProductId));
        if (q.SubjectIds.Count > 0)
            lines = lines.Where(x => x.I.SubjectId != null && q.SubjectIds.Contains(x.I.SubjectId.Value));
        if (q.FacultyIds.Count > 0)
            lines = lines.Where(x => _db.ProductFaculty.Any(pf => pf.ProductId == x.I.ProductId
                && q.FacultyIds.Contains(pf.FacultyId)));

        if (ReportSupport.Term(q.Search) is { } term)
        {
            var like = $"%{term}%";
            lines = lines.Where(x =>
                EF.Functions.ILike(x.R.ReInvoiceNumber, like)
                || EF.Functions.ILike(x.R.OriginalInvoiceNumber, like)
                || EF.Functions.ILike(x.R.OrderNumber, like)
                || EF.Functions.ILike(x.R.FranchiseName, like)
                || EF.Functions.ILike(x.I.ProductTitle, like));
        }

        var rows = await lines.Select(x => new Row(
            x.R.FranchiseName,
            x.R.OriginalInvoiceNumber,
            x.R.OriginalInvoiceDate,
            x.R.ReInvoiceNumber,
            x.R.ReInvoiceDate,
            x.I.ProductTitle,
            x.I.SubjectName ?? "—",
            x.I.Quantity,
            x.I.TaxableAmount,
            x.I.CgstAmount,
            x.I.SgstAmount,
            x.I.IgstAmount,
            x.I.GstAmount,
            x.I.LineTotal,
            x.R.Status,
            x.R.Notes,
            x.R.CancelledReason,
            x.R.IssuedByName,
            x.R.IssuedById)).ToListAsync(ct);

        var totals = new ReportTotals
        {
            TotalQuantity = rows.Sum(r => r.Qty),
            TaxableAmount = rows.Sum(r => r.Taxable),
            Cgst = rows.Sum(r => r.Cgst),
            Sgst = rows.Sum(r => r.Sgst),
            Igst = rows.Sum(r => r.Igst),
            TotalGst = rows.Sum(r => r.Gst),
            TotalInvoiceAmount = rows.Sum(r => r.Total)
        };
        totals.Extra.Add(("Re-invoices", rows.Select(r => r.ReNo).Distinct().Count().ToString()));
        totals.Extra.Add(("Originals covered", rows.Select(r => r.OrigNo).Distinct().Count().ToString()));
        totals.Extra.Add(("Franchisees", rows.Select(r => r.Franchise).Distinct().Count().ToString()));

        var sorted = Sort(rows, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "franchisee-reinvoice",
            Title = "Franchisee Re-Invoice Report",
            Subtitle = ReportSupport.Subtitle(q, "re-invoice date"),
            Columns = Cols,
            TotalRows = rows.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(r => new[]
            {
                r.Franchise,
                r.OrigNo,
                ReportSupport.Date(r.OrigDate),
                r.ReNo,
                ReportSupport.Date(r.ReDate),
                r.Product,
                r.Subject,
                ReportSupport.Int(r.Qty),
                ReportSupport.Money(r.Taxable),
                ReportSupport.Money(r.Cgst),
                ReportSupport.Money(r.Sgst),
                ReportSupport.Money(r.Igst),
                ReportSupport.Money(r.Gst),
                ReportSupport.Money(r.Total),
                r.Status.ToString(),
                Reason(r),
                RaisedBy(r)
            }).ToList()
        };
    }

    private static IEnumerable<Row> Sort(List<Row> rows, ReportQuery q) => q.SortBy switch
    {
        "franchise" => ReportSupport.Order(rows, r => r.Franchise, q.SortDesc),
        "origno" => ReportSupport.Order(rows, r => r.OrigNo, q.SortDesc),
        "origdate" => ReportSupport.Order(rows, r => r.OrigDate, q.SortDesc),
        "rino" => ReportSupport.Order(rows, r => r.ReNo, q.SortDesc),
        "product" => ReportSupport.Order(rows, r => r.Product, q.SortDesc),
        "subject" => ReportSupport.Order(rows, r => r.Subject, q.SortDesc),
        "qty" => ReportSupport.Order(rows, r => r.Qty, q.SortDesc),
        "taxable" => ReportSupport.Order(rows, r => r.Taxable, q.SortDesc),
        "cgst" => ReportSupport.Order(rows, r => r.Cgst, q.SortDesc),
        "sgst" => ReportSupport.Order(rows, r => r.Sgst, q.SortDesc),
        "igst" => ReportSupport.Order(rows, r => r.Igst, q.SortDesc),
        "gst" => ReportSupport.Order(rows, r => r.Gst, q.SortDesc),
        "total" => ReportSupport.Order(rows, r => r.Total, q.SortDesc),
        "status" => ReportSupport.Order(rows, r => r.Status, q.SortDesc),
        // Sorted on the rendered text, not the raw fields, so the order matches what the column
        // actually shows — a cancelled row sorts under its "Cancelled — …" label, where the reader
        // is looking for it, rather than under a note it never displays.
        "reason" => ReportSupport.Order(rows, Reason, q.SortDesc),
        "raisedby" => ReportSupport.Order(rows, RaisedBy, q.SortDesc),
        _ => ReportSupport.Order(rows, r => r.ReDate, q.SortDesc)
    };
}
