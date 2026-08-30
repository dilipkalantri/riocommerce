using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// Raises franchisee re-invoices against issued customer invoices (§13).
///
/// <para>The tax split is derived with <see cref="GstRowCalculator.IsIntraState"/> — the same rule
/// the customer invoice and the receipt renderer apply — so the re-invoice and the original agree
/// about whether a supply is CGST+SGST or IGST (§26, §36.14). Line values are apportioned from the
/// document total so the lines always add up to the header.</para>
/// </summary>
public class FranchiseReInvoiceService : IFranchiseReInvoiceService
{
    private readonly RioCommerceDbContext _db;
    public FranchiseReInvoiceService(RioCommerceDbContext db) => _db = db;

    public async Task<List<ReInvoiceCandidate>> ListCandidatesAsync(
        DateTime? from, DateTime? to, Guid? franchiseId, bool onlyPending, CancellationToken ct = default)
    {
        var fromDate = DateOnly.FromDateTime((from ?? DateTime.UtcNow.AddMonths(-3)).Date);
        var toDate = DateOnly.FromDateTime((to ?? DateTime.UtcNow).Date);

        // Only invoices on FRANCHISE orders can be re-invoiced — there is no franchisee to bill
        // otherwise.
        var q = _db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Active)
            .Where(i => i.InvoiceDate >= fromDate && i.InvoiceDate <= toDate)
            .Where(i => i.Order != null && i.Order.FranchiseId != null);

        if (franchiseId is { } fid) q = q.Where(i => i.Order!.FranchiseId == fid);

        var rows = await q.Select(i => new
        {
            i.Id,
            i.InvoiceNumber,
            i.InvoiceDate,
            i.OrderNumber,
            FranchiseId = i.Order!.FranchiseId!.Value,
            FranchiseName = i.Order.Franchise != null ? i.Order.Franchise.Name : "—",
            i.TotalAmount,
            i.FranchiseShareAmount,
            OrderShare = i.Order.FranchiseShareAmount,
            Existing = _db.FranchiseReInvoices
                .Where(r => r.OriginalInvoiceId == i.Id && r.Status != ReInvoiceStatus.Cancelled)
                .Select(r => r.ReInvoiceNumber)
                .FirstOrDefault()
        })
        .OrderByDescending(x => x.InvoiceDate)
        .ToListAsync(ct);

        var result = rows.Select(x => new ReInvoiceCandidate(
            x.Id, x.InvoiceNumber, x.InvoiceDate, x.OrderNumber,
            x.FranchiseName, x.FranchiseId, x.TotalAmount,
            // The invoice snapshot is authoritative, but legacy rows left it at zero — fall back to
            // the order's recorded share so the picker still shows the right figure.
            x.FranchiseShareAmount > 0 ? x.FranchiseShareAmount : x.OrderShare,
            x.Existing != null, x.Existing));

        if (onlyPending) result = result.Where(c => !c.AlreadyReInvoiced);
        return result.ToList();
    }

    public async Task<ReInvoiceDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.FranchiseReInvoices.AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return r == null ? null : ToDetail(r);
    }

    public async Task<(ReInvoiceDetail? doc, string? error)> IssueAsync(
        Guid originalInvoiceId, DateOnly? date, string? notes,
        Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        var inv = await _db.Invoices.AsNoTracking()
            .Include(i => i.Order).ThenInclude(o => o!.Franchise)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == originalInvoiceId, ct);

        if (inv == null) return (null, "Invoice not found.");
        if (inv.Status != InvoiceStatus.Active) return (null, "This invoice is cancelled and cannot be re-invoiced.");
        if (inv.Order?.FranchiseId == null) return (null, "This invoice is not on a franchise order, so there is no franchisee to re-invoice.");

        var already = await _db.FranchiseReInvoices
            .Where(r => r.OriginalInvoiceId == originalInvoiceId && r.Status != ReInvoiceStatus.Cancelled)
            .Select(r => r.ReInvoiceNumber)
            .FirstOrDefaultAsync(ct);
        if (already != null)
            return (null, $"Invoice {inv.InvoiceNumber} already has re-invoice {already}. Cancel it before raising another.");

        var order = inv.Order!;
        var franchise = order.Franchise;

        // Place of supply drives the split. The franchisee's own state governs this supply — the
        // institute is billing them, not the student — falling back to the invoice's billing state
        // for franchises with no state on record.
        var placeOfSupply = FirstNonBlank(franchise?.State, inv.BillingState, order.BillingState);
        var intraState = GstRowCalculator.IsIntraState(placeOfSupply);

        // The re-invoice is raised for what the franchisee actually owes: the invoice total, which
        // for a franchise order is already the net of their commission.
        var total = Math.Round(inv.TotalAmount, 2);
        var taxable = Math.Round(inv.TaxableAmount, 2);
        var gst = Math.Round(inv.CgstAmount + inv.SgstAmount + inv.IgstAmount, 2);

        // Legacy invoices recorded a total and a taxable value but no split — derive the tax from
        // the difference rather than issuing a document with zero GST on it.
        if (gst <= 0m) gst = Math.Max(0m, Math.Round(total - taxable, 2));
        if (taxable <= 0m) taxable = Math.Round(total - gst, 2);

        var (cgst, sgst, igst) = Split(gst, intraState);
        var rate = taxable > 0 ? Math.Round(gst / taxable * 100m, 2) : 0m;

        var share = inv.FranchiseShareAmount > 0 ? inv.FranchiseShareAmount : order.FranchiseShareAmount;

        var reInvoice = new FranchiseReInvoice
        {
            ReInvoiceNumber = await NextNumberAsync(ct),
            ReInvoiceDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
            OriginalInvoiceId = inv.Id,
            OriginalInvoiceNumber = inv.InvoiceNumber,
            OriginalInvoiceDate = inv.InvoiceDate,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            FranchiseId = order.FranchiseId.Value,
            FranchiseName = franchise?.Name ?? "—",
            FranchiseCode = franchise?.Code,
            FranchiseGstin = franchise?.Gstin,
            PlaceOfSupply = placeOfSupply,
            TaxableAmount = taxable,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            TotalGst = gst,
            TotalAmount = total,
            GstRate = rate,
            FranchiseShareAmount = Math.Round(share, 2),
            Status = ReInvoiceStatus.Issued,
            IssuedById = actorId,
            IssuedByName = actorName,
            Notes = notes
        };

        await AddLinesAsync(reInvoice, order.Id, taxable, gst, intraState, ct);

        _db.FranchiseReInvoices.Add(reInvoice);
        await _db.SaveChangesAsync(ct);

        return (ToDetail(reInvoice), null);
    }

    public async Task<(bool ok, string? error)> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var r = await _db.FranchiseReInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r == null) return (false, "Re-invoice not found.");
        if (r.Status == ReInvoiceStatus.Cancelled) return (false, "This re-invoice is already cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) return (false, "A cancellation reason is required.");

        r.Status = ReInvoiceStatus.Cancelled;
        r.CancelledAt = DateTime.UtcNow;
        r.CancelledReason = reason.Trim();
        r.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    // ─────────────── internals ───────────────

    /// <summary>
    /// Builds the product lines, apportioning the document's taxable value and tax across them
    /// pro-rata by line value so the lines always sum to the header.
    ///
    /// <para>Lines come from the order rather than the original invoice's line items, because the
    /// legacy invoice flow never wrote those — an invoice raised by <c>OrderAdminService</c> has
    /// totals but no lines, and a re-invoice with no products on it would fail §13.</para>
    /// </summary>
    private async Task AddLinesAsync(
        FranchiseReInvoice re, Guid orderId, decimal taxable, decimal gst, bool intraState, CancellationToken ct)
    {
        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .Select(i => new
            {
                i.Id,
                i.ProductId,
                i.ProductTitle,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal,
                SubjectId = i.Product.SubjectId,
                SubjectName = i.Product.Subject != null ? i.Product.Subject.Name : null
            })
            .ToListAsync(ct);

        if (items.Count == 0) return;

        var basis = items.Sum(i => i.LineTotal);
        decimal accTaxable = 0, accGst = 0;

        for (var idx = 0; idx < items.Count; idx++)
        {
            var i = items[idx];
            var isLast = idx == items.Count - 1;
            var share = basis > 0 ? i.LineTotal / basis : 1m / items.Count;

            // Rounding remainder lands on the last line, so the lines tie to the header exactly.
            var lineTaxable = isLast ? taxable - accTaxable : Math.Round(taxable * share, 2);
            var lineGst = isLast ? gst - accGst : Math.Round(gst * share, 2);
            if (!isLast) { accTaxable += lineTaxable; accGst += lineGst; }

            var (c, s, ig) = Split(lineGst, intraState);

            re.Items.Add(new FranchiseReInvoiceItem
            {
                ProductId = i.ProductId,
                ProductTitle = i.ProductTitle,
                SubjectId = i.SubjectId,
                SubjectName = i.SubjectName,
                OrderItemId = i.Id,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                TaxableAmount = lineTaxable,
                GstRate = lineTaxable > 0 ? Math.Round(lineGst / lineTaxable * 100m, 2) : 0m,
                CgstAmount = c,
                SgstAmount = s,
                IgstAmount = ig,
                GstAmount = lineGst,
                LineTotal = Math.Round(lineTaxable + lineGst, 2)
            });
        }
    }

    /// <summary>Intra-state halves the tax into CGST and SGST with the rounding remainder on SGST;
    /// inter-state is entirely IGST.</summary>
    private static (decimal Cgst, decimal Sgst, decimal Igst) Split(decimal gst, bool intraState)
    {
        if (!intraState) return (0m, 0m, gst);
        var cgst = Math.Round(gst / 2m, 2);
        return (cgst, gst - cgst, 0m);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>
    /// Next number in the <c>RIO-RINV-YYYYMM-NNNN</c> series, resetting monthly — the same shape
    /// and reset cadence as the customer invoice series, on a distinct prefix so the two can never
    /// be mistaken for one another.
    /// </summary>
    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var period = DateTime.UtcNow.ToString("yyyyMM");
        var prefix = $"RIO-RINV-{period}-";

        var last = await _db.FranchiseReInvoices
            .Where(r => r.ReInvoiceNumber.StartsWith(prefix))
            .OrderByDescending(r => r.ReInvoiceNumber)
            .Select(r => r.ReInvoiceNumber)
            .FirstOrDefaultAsync(ct);

        var next = 1;
        if (last != null && int.TryParse(last[prefix.Length..], out var n)) next = n + 1;
        return prefix + next.ToString("D4");
    }

    private static ReInvoiceDetail ToDetail(FranchiseReInvoice r) => new(
        r.Id, r.ReInvoiceNumber, r.ReInvoiceDate,
        r.OriginalInvoiceNumber, r.OriginalInvoiceDate, r.OrderNumber,
        r.FranchiseName, r.FranchiseGstin, r.PlaceOfSupply,
        r.TaxableAmount, r.CgstAmount, r.SgstAmount, r.IgstAmount, r.TotalGst,
        r.TotalAmount, r.FranchiseShareAmount, r.Status.ToString(), r.Notes,
        r.Items.Select(i => new ReInvoiceLine(
            i.ProductTitle, i.SubjectName, i.Quantity, i.UnitPrice,
            i.TaxableAmount, i.GstRate, i.GstAmount, i.LineTotal)).ToList());
}
