using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §9 — GST Report. Invoice-wise, as the section specifies: one row per issued invoice, keyed on
/// invoice number and invoice date rather than order date.
///
/// <para><b>The tax figures are computed by <see cref="GstRowCalculator"/>, not read off the
/// invoice row.</b> That is not a preference — the invoice snapshot is internally inconsistent for
/// franchise orders and cannot be read directly. <c>InvoiceService</c> stores
/// <c>TaxableAmount</c> and the CGST/SGST/IGST split on the GROSS basis while storing
/// <c>TotalAmount</c> as the NET the franchisee is actually billed. On FRN-1007 that is taxable
/// ₹100 + tax ₹18 = ₹118 against a total of ₹94.40 — a row that does not add up. The calculator
/// scales the tax to what was invoiced (₹80.00 + ₹14.40 = ₹94.40), which is what the order screen,
/// the tax invoice and the receipt all show, so all four agree (§26, §28, §36.14/15).</para>
///
/// <para>The row carries the full bifurcation — gross, franchisee discount, invoice value, taxable,
/// the tax split — so the arithmetic is checkable without leaving the report:</para>
/// <code>
///   Gross − Franchisee Discount = Invoice Amount
///   Taxable + CGST + SGST + IGST = Invoice Amount
/// </code>
///
/// <para>Cancelled invoices are excluded outright. A cancelled tax invoice carries no output tax,
/// and a GST return that included one would overstate the liability.</para>
/// </summary>
internal sealed class GstReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.Gst;

    private readonly RioCommerceDbContext _db;
    public GstReportBuilder(RioCommerceDbContext db) => _db = db;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Invoice No.",        ReportColumnKind.Text,   "invoice",   1.7f),
        new("Invoice Date",       ReportColumnKind.Date,   "date",      1.1f),
        new("Order No.",          ReportColumnKind.Text,   "order",     1.3f),
        new("Customer",           ReportColumnKind.Text,   "customer",  2.1f),
        new("GSTIN",              ReportColumnKind.Text,   "gstin",     1.6f),
        new("Class",              ReportColumnKind.Text,   "class",     0.7f),
        new("Place of Supply",    ReportColumnKind.Text,   "pos",       1.4f),
        // ── Bifurcation, in the same reading order as the order totals card ──
        new("Gross (₹)",          ReportColumnKind.Money,  "gross",     1.2f),
        new("Franchisee Disc (₹)",ReportColumnKind.Money,  "fdisc",     1.4f),
        new("Invoice Amt (₹)",    ReportColumnKind.Money,  "total",     1.3f),
        new("Taxable (₹)",        ReportColumnKind.Money,  "taxable",   1.2f),
        new("CGST (₹)",           ReportColumnKind.Money,  "cgst",      1f),
        new("SGST (₹)",           ReportColumnKind.Money,  "sgst",      1f),
        new("IGST (₹)",           ReportColumnKind.Money,  "igst",      1f),
        new("Total Tax (₹)",      ReportColumnKind.Money,  "gst",       1.2f),
        new("Refunded (₹)",       ReportColumnKind.Money,  "refunded",  1.1f),
        new("Franchisee",         ReportColumnKind.Text,   "franchise", 1.4f),
        new("Status",             ReportColumnKind.Status, "status",    0.9f)
    };

    private sealed record Row(
        string InvoiceNumber, DateOnly InvoiceDate, string OrderNumber, string Customer,
        string Gstin, string Classification, string PlaceOfSupply,
        decimal Gross, decimal FranchiseDiscount, decimal InvoiceValue,
        decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Gst,
        decimal Refunded, decimal RefundedGst,
        string Franchise, InvoiceStatus Status);

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var (from, to) = ReportSupport.ResolveDateOnlyRange(q);

        var inv = _db.Invoices.AsNoTracking()
            .Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            // Cancelled invoices carry no output tax — including them would overstate the liability.
            .Where(i => i.Status == InvoiceStatus.Active);

        if (q.FranchiseScopeId is { } scope)
            inv = inv.Where(i => i.Order != null && i.Order.FranchiseId == scope);
        else if (q.FranchiseIds.Count > 0)
            inv = inv.Where(i => i.Order != null && i.Order.FranchiseId != null
                                 && q.FranchiseIds.Contains(i.Order.FranchiseId.Value));

        if (q.PaymentModes.Count > 0)
            inv = inv.Where(i => i.PaymentMode != null && q.PaymentModes.Contains(i.PaymentMode.Value));
        if (q.OrderStatuses.Count > 0)
            inv = inv.Where(i => i.Order != null && q.OrderStatuses.Contains(i.Order.Status));
        if (q.Sources.Count > 0)
            inv = inv.Where(i => i.Order != null && q.Sources.Contains(i.Order.Source));

        // Product / subject / faculty narrow the invoice by what was on the underlying order.
        if (q.ProductIds.Count > 0)
            inv = inv.Where(i => _db.OrderItems.Any(oi => oi.OrderId == i.OrderId && q.ProductIds.Contains(oi.ProductId)));
        if (q.SubjectIds.Count > 0)
            inv = inv.Where(i => _db.OrderItems.Any(oi => oi.OrderId == i.OrderId
                && (_db.ProductSubjects.Any(ps => ps.ProductId == oi.ProductId && q.SubjectIds.Contains(ps.SubjectId))
                    || (oi.Product.SubjectId != null && q.SubjectIds.Contains(oi.Product.SubjectId.Value)))));
        if (q.FacultyIds.Count > 0)
            inv = inv.Where(i => _db.OrderItems.Any(oi => oi.OrderId == i.OrderId
                && _db.ProductFaculty.Any(pf => pf.ProductId == oi.ProductId && q.FacultyIds.Contains(pf.FacultyId))));

        if (ReportSupport.Term(q.Search) is { } term)
        {
            var like = $"%{term}%";
            inv = inv.Where(i =>
                EF.Functions.ILike(i.InvoiceNumber, like)
                || EF.Functions.ILike(i.OrderNumber, like)
                || (i.CustomerName != null && EF.Functions.ILike(i.CustomerName, like))
                || (i.CustomerGstin != null && EF.Functions.ILike(i.CustomerGstin, like))
                || (i.CustomerPhone != null && EF.Functions.ILike(i.CustomerPhone, like))
                || (i.CustomerEmail != null && EF.Functions.ILike(i.CustomerEmail, like)));
        }

        // The order comes back with the invoice: GstRowCalculator needs the order's own recorded tax
        // split and billing state to decide the intra/inter-state treatment and to scale correctly.
        var raw = await inv
            .Include(i => i.Order)
            .Select(i => new
            {
                Invoice = i,
                i.Order,
                Franchise = i.Order != null && i.Order.Franchise != null ? i.Order.Franchise.Name : null,
                OrderStudent = i.Order != null ? i.Order.StudentName : null,
                OrgName = i.Order != null ? i.Order.OrgName : null
            })
            .ToListAsync(ct);

        // Succeeded refunds only — a pending or failed refund has not moved money, so it cannot
        // reduce output tax.
        var orderIds = raw.Select(x => x.Invoice.OrderId).Distinct().ToList();
        var refunds = await _db.Refunds.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && r.Status == RefundStatus.Succeeded)
            .GroupBy(r => r.OrderId)
            .Select(g => new { OrderId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Amount, ct);

        var rows = raw.Select(x =>
        {
            var i = x.Invoice;
            refunds.TryGetValue(i.OrderId, out var refunded);

            var g = Figures(x.Order, i, refunded);

            var customer = !string.IsNullOrWhiteSpace(i.CustomerName) ? i.CustomerName!
                : !string.IsNullOrWhiteSpace(x.OrgName) ? x.OrgName!
                : x.OrderStudent ?? "—";

            return new Row(
                i.InvoiceNumber, i.InvoiceDate, i.OrderNumber, customer,
                i.CustomerGstin ?? "",
                string.IsNullOrWhiteSpace(i.GstClassification) ? "B2C" : i.GstClassification,
                i.BillingState ?? "",
                g.InvoiceValue + g.FranchiseShare, g.FranchiseShare, g.InvoiceValue,
                g.Taxable, g.Cgst, g.Sgst, g.Igst, g.Gst,
                g.Refunded, g.RefundedGst,
                x.Franchise ?? "—", i.Status);
        }).ToList();

        var b2b = rows.Count(r => r.Classification == "B2B");
        var franchiseDiscount = rows.Sum(r => r.FranchiseDiscount);
        var gst = rows.Sum(r => r.Gst);
        var refundedGst = rows.Sum(r => r.RefundedGst);

        var totals = new ReportTotals
        {
            TotalOrders = rows.Count,
            GrossAmount = rows.Sum(r => r.Gross),
            TaxableAmount = rows.Sum(r => r.Taxable),
            Cgst = rows.Sum(r => r.Cgst),
            Sgst = rows.Sum(r => r.Sgst),
            Igst = rows.Sum(r => r.Igst),
            TotalGst = gst,
            TotalInvoiceAmount = rows.Sum(r => r.InvoiceValue),
            Refunded = rows.Sum(r => r.Refunded)
        };
        // Only worth a line when franchise invoices are in scope — otherwise it is always ₹0.
        if (franchiseDiscount > 0)
            totals.FranchiseDiscount = franchiseDiscount;

        // Net GST is what actually falls due: output tax less the tax reversed by refunds.
        totals.Extra.Add(("Less: GST on refunds", $"−₹{refundedGst:N2}"));
        totals.Extra.Add(("Net GST", $"₹{(gst - refundedGst):N2}"));
        totals.Extra.Add(("B2B invoices", b2b.ToString()));
        totals.Extra.Add(("B2C invoices", (rows.Count - b2b).ToString()));

        var sorted = Sort(rows, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "gst",
            Title = "GST Report",
            Subtitle = ReportSupport.Subtitle(q, "invoice date"),
            Columns = Cols,
            TotalRows = rows.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(r => new[]
            {
                r.InvoiceNumber,
                ReportSupport.Date(r.InvoiceDate),
                r.OrderNumber,
                r.Customer,
                ReportSupport.Text(r.Gstin),
                r.Classification,
                ReportSupport.Text(r.PlaceOfSupply),
                ReportSupport.Money(r.Gross),
                // Blank rather than 0.00 on a non-franchise invoice: a column of zeroes reads as
                // data, a blank reads as not-applicable.
                ReportSupport.MoneyOrBlank(r.FranchiseDiscount),
                ReportSupport.Money(r.InvoiceValue),
                ReportSupport.Money(r.Taxable),
                ReportSupport.Money(r.Cgst),
                ReportSupport.Money(r.Sgst),
                ReportSupport.Money(r.Igst),
                ReportSupport.Money(r.Gst),
                ReportSupport.MoneyOrBlank(r.Refunded),
                r.Franchise,
                r.Status.ToString()
            }).ToList()
        };
    }

    /// <summary>
    /// Tax figures for one invoice, scaled to what was actually billed.
    ///
    /// <para>Delegates to <see cref="GstRowCalculator"/> — the same code the tax invoice, the
    /// receipt and the legacy GST export all use — so the four cannot disagree. Falls back to the
    /// invoice's own columns only for an orphaned invoice whose order is gone, where there is
    /// nothing left to scale against.</para>
    /// </summary>
    private static GstRowCalculator.GstFigures Figures(Order? order, Invoice i, decimal refunded)
    {
        if (order is not null)
            return GstRowCalculator.For(order, refunded, invoicedValue: i.TotalAmount);

        var tax = i.CgstAmount + i.SgstAmount + i.IgstAmount;
        return new GstRowCalculator.GstFigures(
            Taxable: Math.Round(i.TotalAmount - tax, 2),
            Cgst: i.CgstAmount, Sgst: i.SgstAmount, Igst: i.IgstAmount,
            Refunded: refunded, RefundedGst: 0m,
            InvoiceValue: i.TotalAmount, Gst: tax,
            FranchiseShare: i.FranchiseShareAmount);
    }

    private static IEnumerable<Row> Sort(List<Row> rows, ReportQuery q) => q.SortBy switch
    {
        "invoice" => ReportSupport.Order(rows, r => r.InvoiceNumber, q.SortDesc),
        "order" => ReportSupport.Order(rows, r => r.OrderNumber, q.SortDesc),
        "customer" => ReportSupport.Order(rows, r => r.Customer, q.SortDesc),
        "gstin" => ReportSupport.Order(rows, r => r.Gstin, q.SortDesc),
        "class" => ReportSupport.Order(rows, r => r.Classification, q.SortDesc),
        "pos" => ReportSupport.Order(rows, r => r.PlaceOfSupply, q.SortDesc),
        "gross" => ReportSupport.Order(rows, r => r.Gross, q.SortDesc),
        "fdisc" => ReportSupport.Order(rows, r => r.FranchiseDiscount, q.SortDesc),
        "total" => ReportSupport.Order(rows, r => r.InvoiceValue, q.SortDesc),
        "taxable" => ReportSupport.Order(rows, r => r.Taxable, q.SortDesc),
        "cgst" => ReportSupport.Order(rows, r => r.Cgst, q.SortDesc),
        "sgst" => ReportSupport.Order(rows, r => r.Sgst, q.SortDesc),
        "igst" => ReportSupport.Order(rows, r => r.Igst, q.SortDesc),
        "gst" => ReportSupport.Order(rows, r => r.Gst, q.SortDesc),
        "refunded" => ReportSupport.Order(rows, r => r.Refunded, q.SortDesc),
        "franchise" => ReportSupport.Order(rows, r => r.Franchise, q.SortDesc),
        "status" => ReportSupport.Order(rows, r => r.Status, q.SortDesc),
        _ => ReportSupport.Order(rows, r => r.InvoiceDate, q.SortDesc)
    };
}
