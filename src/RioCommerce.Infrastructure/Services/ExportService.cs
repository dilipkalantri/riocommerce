using ClosedXML.Excel;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace RioCommerce.Infrastructure.Services;

// Excel (ClosedXML) + PDF (QuestPDF) report exports.
// QuestPDF is configured for the Community license at type-init.
public class ExportService : IExportService
{
    static ExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // Order.GstClassification values, set at checkout from whether a GSTIN was supplied.
    private const string B2B = "B2B";
    private const string B2C = "B2C";

    private readonly RioCommerceDbContext _db;
    private readonly ISiteSettingsService _site;
    public ExportService(RioCommerceDbContext db, ISiteSettingsService site) { _db = db; _site = site; }

    // ─────────────── Public report endpoints ───────────────

    public async Task<(byte[] bytes, string filename)> OrdersAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildOrdersAsync(f, scope);
        return Render(data, excel, "orders");
    }

    public async Task<(byte[] bytes, string filename)> CommissionAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildCommissionAsync(f, scope);
        return Render(data, excel, "commission");
    }

    public async Task<(byte[] bytes, string filename)> FacultyShareAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildFacultyShareAsync(f, scope);
        return Render(data, excel, "faculty-share");
    }

    public async Task<(byte[] bytes, string filename)> WalletAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildWalletAsync(f, scope);
        return Render(data, excel, "wallet");
    }

    public async Task<(byte[] bytes, string filename)> CustomersAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildCustomersAsync(f, scope);
        return Render(data, excel, "customers");
    }

    public async Task<(byte[] bytes, string filename)> GstAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildGstAsync(f, scope);
        return Render(data, excel, "gst");
    }

    public async Task<(byte[] bytes, string filename)> B2bAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildB2bAsync(f, scope);
        return Render(data, excel, "gst-b2b");
    }

    public async Task<(byte[] bytes, string filename)> B2cAsync(ReportFilter f, bool excel, Guid? scope)
    {
        var data = await BuildB2cAsync(f, scope);
        return Render(data, excel, "gst-b2c");
    }

    public async Task<(byte[] bytes, string filename)?> InvoicePdfAsync(Guid orderId, Guid? scope)
    {
        var order = await _db.Orders.Include(o => o.Items).Include(o => o.Franchise)
            .FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return null;
        if (scope.HasValue && order.FranchiseId != scope) return null;          // franchisee may only fetch own
        var bytes = RenderInvoicePdf(order);
        return (bytes, $"invoice-{order.OrderNumber}.pdf");
    }

    // ─────────────── Data builders (read DB → ReportData) ───────────────

    private async Task<ReportData> BuildOrdersAsync(ReportFilter f, Guid? scope)
    {
        var (from, to) = ResolveRange(f);
        var q = _db.Orders.ExcludeWalletTopUps().Include(o => o.Items).Include(o => o.Franchise).AsQueryable();
        var fid = scope ?? f.FranchiseId;
        if (fid.HasValue) q = q.Where(o => o.FranchiseId == fid);
        if (f.Status.HasValue) q = q.Where(o => o.Status == f.Status);
        if (f.Source.HasValue) q = q.Where(o => o.Source == f.Source);
        q = q.Where(o => o.CreatedAt >= from && o.CreatedAt <= to);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();
        var rows = orders.Select(o => new[]
        {
            o.OrderNumber,
            o.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            o.Franchise?.Name ?? "—",
            o.StudentName,
            o.StudentPhone,
            o.Items.FirstOrDefault()?.ProductTitle ?? "—",
            o.Items.Sum(i => i.Quantity).ToString(),
            o.Status.ToString(),
            o.Source.ToString(),
            Fmt(o.TotalAmount)
        }).ToList();
        var total = orders.Sum(o => o.TotalAmount);

        return new ReportData
        {
            Title = "Orders Report",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Order #", "Date", "Franchise", "Customer", "Phone", "Product", "Qty", "Status", "Source", "Total (₹)" },
            Rows = rows,
            Summary = new() {
                new("Total orders", orders.Count.ToString()),
                new("Total revenue", $"₹{total:N2}")
            }
        };
    }

    private async Task<ReportData> BuildCommissionAsync(ReportFilter f, Guid? scope)
    {
        var (from, to) = ResolveRange(f);
        var q = _db.FranchiseCommissionEntries.Include(e => e.Franchise).AsQueryable();
        var fid = scope ?? f.FranchiseId;
        if (fid.HasValue) q = q.Where(e => e.FranchiseId == fid);
        q = q.Where(e => e.EarnedAt >= from && e.EarnedAt <= to);

        var entries = await q.OrderByDescending(e => e.EarnedAt).ToListAsync();
        var rows = entries.Select(e => new[]
        {
            e.EarnedAt.ToString("yyyy-MM-dd HH:mm"),
            e.Franchise?.Name ?? "—",
            e.OrderNumber,
            e.ProductTitle,
            Fmt(e.BaseAmount),
            e.Type == CommissionType.Percent ? $"{e.Value}%" : $"₹{e.Value}/unit",
            Fmt(e.CommissionAmount)
        }).ToList();
        var total = entries.Sum(e => e.CommissionAmount);

        return new ReportData
        {
            Title = "Commission Report",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Date", "Franchise", "Order #", "Product", "Base (₹)", "Rule", "Commission (₹)" },
            Rows = rows,
            Summary = new() {
                new("Entries", entries.Count.ToString()),
                new("Total commission", $"₹{total:N2}")
            }
        };
    }

    /// <summary>
    /// Earned faculty shares from the ledger, decomposed per the share specification: the bare share
    /// (taxable value of the faculty's supply), the GST charged on it, and the total payout.
    ///
    /// <para><b>Registration is read from the ledger snapshot</b>, not from the Faculty record. A
    /// faculty who registers for GST later must not have last quarter's rows re-stated as taxable —
    /// that is precisely the mistake that made the franchise commission split unbackfillable.</para>
    /// </summary>
    private async Task<ReportData> BuildFacultyShareAsync(ReportFilter f, Guid? scope)
    {
        var (from, to) = ResolveRange(f);

        // Faculty remuneration is not franchisee-visible. A scoped (franchise-portal) caller gets an
        // empty report rather than the institute's payroll.
        if (scope.HasValue)
            return new ReportData
            {
                Title = "Faculty Share Report",
                Subtitle = SubtitleFor(from, to, scope, "All faculty"),
                Headers = FacultyShareHeaders,
                Rows = new List<string[]>(),
                Summary = new() { new("Access", "Not available for franchise accounts") }
            };

        var q = _db.FacultyShareEntries.AsNoTracking().Include(e => e.Faculty).AsQueryable();
        if (f.FacultyId.HasValue) q = q.Where(e => e.FacultyId == f.FacultyId);
        q = q.Where(e => e.EarnedAt >= from && e.EarnedAt <= to);

        var entries = await q.OrderByDescending(e => e.EarnedAt).ToListAsync();
        var rows = entries.Select(e => new[]
        {
            e.EarnedAt.ToString("yyyy-MM-dd HH:mm"),
            e.Faculty?.DisplayName ?? "—",
            e.WasGstRegistered ? "Yes" : "No",
            e.OrderNumber,
            e.ProductTitle,
            Fmt(e.BaseAmount),
            e.ShareType == SharingType.Percentage ? $"{e.ShareValue:0.##}%" : $"₹{e.ShareValue:0.##}/unit",
            Fmt(e.ShareAmount),
            Fmt(e.GstOnShare),
            Fmt(e.TotalPayout),
            e.WasCapped ? "Yes" : ""
        }).ToList();

        var share = entries.Sum(e => e.ShareAmount);
        var gst = entries.Sum(e => e.GstOnShare);

        return new ReportData
        {
            Title = "Faculty Share Report",
            Subtitle = SubtitleFor(from, to, f.FacultyId, "All faculty"),
            Headers = FacultyShareHeaders,
            Rows = rows,
            Summary = new()
            {
                new("Entries", entries.Count.ToString()),
                new("Faculty", entries.Select(e => e.FacultyId).Distinct().Count().ToString()),
                // The share is the institute's cost. The GST on it is a pass-through recoverable as
                // input tax credit, so the two are never added into a single "expense" figure here.
                new("Total share (cost)", $"₹{share:N2}"),
                new("GST on share (ITC)", $"₹{gst:N2}"),
                new("Total payout", $"₹{share + gst:N2}")
            }
        };
    }

    private static readonly string[] FacultyShareHeaders =
    {
        "Date", "Faculty", "GST reg.", "Order #", "Product", "Base (₹)", "Rule",
        "Share (₹)", "GST on share (₹)", "Total payout (₹)", "Capped"
    };

    private async Task<ReportData> BuildWalletAsync(ReportFilter f, Guid? scope)
    {
        var (from, to) = ResolveRange(f);
        var fid = scope ?? f.FranchiseId;
        var q = _db.FranchiseLedger.Include(e => e.Franchise).AsQueryable();
        if (fid.HasValue) q = q.Where(e => e.FranchiseId == fid);
        q = q.Where(e => e.CreatedAt >= from && e.CreatedAt <= to);

        var entries = await q.OrderBy(e => e.CreatedAt).ToListAsync();
        var rows = entries.Select(e => new[]
        {
            e.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            e.Franchise?.Name ?? "—",
            e.IsCredit ? "Credit" : "Debit",
            Fmt(e.Amount),
            Fmt(e.BalanceAfter),
            e.Description ?? ""
        }).ToList();
        var credits = entries.Where(e => e.IsCredit).Sum(e => e.Amount);
        var debits = entries.Where(e => !e.IsCredit).Sum(e => e.Amount);

        return new ReportData
        {
            Title = "Wallet Transactions",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Date", "Franchise", "Type", "Amount (₹)", "Balance After (₹)", "Description" },
            Rows = rows,
            Summary = new() {
                new("Total credits", $"₹{credits:N2}"),
                new("Total debits", $"₹{debits:N2}"),
                new("Net change", $"₹{(credits - debits):N2}")
            }
        };
    }

    private async Task<ReportData> BuildCustomersAsync(ReportFilter f, Guid? scope)
    {
        var (from, to) = ResolveRange(f);
        var q = _db.Orders.ExcludeWalletTopUps().AsQueryable();
        var fid = scope ?? f.FranchiseId;
        if (fid.HasValue) q = q.Where(o => o.FranchiseId == fid);
        q = q.Where(o => o.CreatedAt >= from && o.CreatedAt <= to);

        var grouped = await q.GroupBy(o => o.StudentPhone)
            .Select(g => new
            {
                Phone = g.Key,
                Name = g.OrderByDescending(x => x.CreatedAt).First().StudentName,
                Email = g.OrderByDescending(x => x.CreatedAt).First().StudentEmail,
                City = g.OrderByDescending(x => x.CreatedAt).First().StudentCity,
                Type = g.OrderByDescending(x => x.CreatedAt).First().CustomerType,
                OrgName = g.OrderByDescending(x => x.CreatedAt).First().OrgName,
                Orders = g.Count(),
                Spend = g.Sum(x => x.TotalAmount),
                Last = g.Max(x => x.CreatedAt)
            }).OrderByDescending(x => x.Spend).ToListAsync();

        var rows = grouped.Select(c => new[]
        {
            c.Name,
            c.Phone,
            c.Email ?? "",
            c.City ?? "",
            c.Type == CustomerType.Organization ? "Org" : "Individual",
            c.OrgName ?? "",
            c.Orders.ToString(),
            Fmt(c.Spend),
            c.Last.ToString("yyyy-MM-dd")
        }).ToList();

        return new ReportData
        {
            Title = "Customers Report",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Name", "Phone", "Email", "City", "Type", "Organization", "# Orders", "Total Spend (₹)", "Last Order" },
            Rows = rows,
            Summary = new() {
                new("Unique customers", grouped.Count.ToString()),
                new("Total spend", $"₹{grouped.Sum(c => c.Spend):N2}")
            }
        };
    }

    // ─────────────── GST: B2B / B2C / combined ───────────────
    //
    // All three read the same rows through LoadGstRowsAsync so their totals reconcile against each
    // other and against the invoice PDFs. Taxable is Total − GST (prices are GST-inclusive), which is
    // the same basis InvoiceService uses — Subtotal − Discount would drop checkout add-ons that GST
    // was actually charged on.

    private async Task<ReportData> BuildB2bAsync(ReportFilter f, Guid? scope)
    {
        var (rows, from, to, fid) = await LoadGstRowsAsync(f, scope, B2B);

        return new ReportData
        {
            Title = "GST — B2B (Registered Buyers)",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Invoice #", "Order #", "Date", "GSTIN", "Customer", "Place of Supply",
                              "Gross (₹)", "Franchisee Share (₹)", "Invoice Value (₹)",
                              "Taxable (₹)", "CGST (₹)", "SGST (₹)", "IGST (₹)", "Total GST (₹)",
                              "Refunded (₹)", "RCM" },
            NumericColumns = new[] { 6, 7, 8, 9, 10, 11, 12, 13, 14 },
            ColumnWeights = new[] { 1.9f, 1.3f, 1f, 1.8f, 2.2f, 1.4f, 1.1f, 1.4f, 1.3f, 1.2f, 1f, 1f, 1f, 1.2f, 1.1f, 0.6f },
            Rows = rows.Select(r => new[]
            {
                r.InvoiceNumber ?? "",
                r.Order.OrderNumber,
                r.Order.CreatedAt.ToString("yyyy-MM-dd"),
                r.Order.GstNumber ?? "",
                CustomerName(r.Order),
                r.Order.BillingState ?? "",
                Fmt(r.Order.TotalAmount), r.FranchiseShare > 0 ? Fmt(r.FranchiseShare) : "", Fmt(r.InvoiceValue),
                Fmt(r.Taxable), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.Gst),
                r.Refunded > 0 ? Fmt(r.Refunded) : "",
                r.Order.ReverseCharge ? "Y" : "N"
            }).ToList(),
            Summary = GstSummary(rows, "B2B invoices")
        };
    }

    private async Task<ReportData> BuildB2cAsync(ReportFilter f, Guid? scope)
    {
        var (rows, from, to, fid) = await LoadGstRowsAsync(f, scope, B2C);

        return new ReportData
        {
            Title = "GST — B2C (Unregistered Buyers)",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Invoice #", "Order #", "Date", "Customer", "Place of Supply",
                              "Gross (₹)", "Franchisee Share (₹)", "Invoice Value (₹)",
                              "Taxable (₹)", "CGST (₹)", "SGST (₹)", "IGST (₹)", "Total GST (₹)",
                              "Refunded (₹)" },
            NumericColumns = new[] { 5, 6, 7, 8, 9, 10, 11, 12, 13 },
            ColumnWeights = new[] { 1.9f, 1.3f, 1f, 2.4f, 1.4f, 1.1f, 1.4f, 1.3f, 1.2f, 1f, 1f, 1f, 1.2f, 1.1f },
            Rows = rows.Select(r => new[]
            {
                r.InvoiceNumber ?? "",
                r.Order.OrderNumber,
                r.Order.CreatedAt.ToString("yyyy-MM-dd"),
                CustomerName(r.Order),
                r.Order.BillingState ?? "",
                Fmt(r.Order.TotalAmount), r.FranchiseShare > 0 ? Fmt(r.FranchiseShare) : "", Fmt(r.InvoiceValue),
                Fmt(r.Taxable), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.Gst),
                r.Refunded > 0 ? Fmt(r.Refunded) : ""
            }).ToList(),
            Summary = GstSummary(rows, "B2C invoices")
        };
    }

    private async Task<ReportData> BuildGstAsync(ReportFilter f, Guid? scope)
    {
        var (rows, from, to, fid) = await LoadGstRowsAsync(f, scope, classification: null);

        var b2b = rows.Where(r => r.Order.GstClassification == B2B).ToList();
        var b2c = rows.Where(r => r.Order.GstClassification != B2B).ToList();
        var summary = GstSummary(rows, "Invoices");
        summary.Insert(1, new("B2B", $"{b2b.Count} invoices  •  taxable ₹{b2b.Sum(r => r.Taxable):N2}  •  GST ₹{b2b.Sum(r => r.Gst):N2}"));
        summary.Insert(2, new("B2C", $"{b2c.Count} invoices  •  taxable ₹{b2c.Sum(r => r.Taxable):N2}  •  GST ₹{b2c.Sum(r => r.Gst):N2}"));

        return new ReportData
        {
            Title = "GST Report",
            Subtitle = SubtitleFor(from, to, fid, "All franchises"),
            Headers = new[] { "Invoice #", "Order #", "Date", "Class", "GSTIN", "Customer", "Place of Supply",
                              "Gross (₹)", "Franchisee Share (₹)", "Invoice Value (₹)",
                              "Taxable (₹)", "CGST (₹)", "SGST (₹)", "IGST (₹)", "Total GST (₹)",
                              "Refunded (₹)" },
            NumericColumns = new[] { 7, 8, 9, 10, 11, 12, 13, 14, 15 },
            ColumnWeights = new[] { 1.9f, 1.3f, 1f, 0.7f, 1.8f, 2.2f, 1.4f, 1.1f, 1.4f, 1.3f, 1.2f, 1f, 1f, 1f, 1.2f, 1.1f },
            Rows = rows.Select(r => new[]
            {
                r.InvoiceNumber ?? "",
                r.Order.OrderNumber,
                r.Order.CreatedAt.ToString("yyyy-MM-dd"),
                r.Order.GstClassification,
                r.Order.GstNumber ?? "",
                CustomerName(r.Order),
                r.Order.BillingState ?? "",
                Fmt(r.Order.TotalAmount), r.FranchiseShare > 0 ? Fmt(r.FranchiseShare) : "", Fmt(r.InvoiceValue),
                Fmt(r.Taxable), Fmt(r.Cgst), Fmt(r.Sgst), Fmt(r.Igst), Fmt(r.Gst),
                r.Refunded > 0 ? Fmt(r.Refunded) : ""
            }).ToList(),
            Summary = summary
        };
    }

    /// <summary>One GST line per order, with the tax split resolved, the live invoice number
    /// attached, and succeeded refunds attached.
    ///
    /// <para>Every money figure here is on the INVOICE basis — the amount actually billed. For a
    /// franchise order that is the net of the franchisee's share, so these rows tie back to the tax
    /// invoice the franchisee holds rather than to the student-facing order gross.</para></summary>
    private sealed record GstRow(
        Order Order, string? InvoiceNumber, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst,
        decimal Refunded, decimal RefundedGst, decimal InvoiceValue, decimal Gst, decimal FranchiseShare);

    private async Task<(List<GstRow> rows, DateTime from, DateTime to, Guid? fid)> LoadGstRowsAsync(
        ReportFilter f, Guid? scope, string? classification)
    {
        var (from, to) = ResolveRange(f);
        var fid = scope ?? f.FranchiseId;
        // GST mirrors INVOICES, not sales — so this is the one report that keeps wallet top-ups
        // (tax charged when the money came in) and drops wallet-funded course orders (never
        // invoiced). See OrderQueryExtensions.InvoicedForGst.
        var q = _db.Orders.InvoicedForGst();
        if (fid.HasValue) q = q.Where(o => o.FranchiseId == fid);
        // Refunded orders stay in: the tax was charged and filed for the period of supply. Each row
        // carries its refunded amount so the summary can show net-of-refund GST without dropping the sale.
        q = q.Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
            .Where(o => o.Status != OrderStatus.Draft && o.Status != OrderStatus.Cancelled);

        // B2B is "a GSTIN was captured"; B2C is the catch-all so blank/legacy classifications still land
        // in exactly one of the two reports and the pair always adds up to the combined one.
        if (classification == B2B) q = q.Where(o => o.GstClassification == B2B);
        else if (classification == B2C) q = q.Where(o => o.GstClassification != B2B);

        var orders = await q.OrderByDescending(o => o.CreatedAt).ToListAsync();
        if (orders.Count == 0) return (new List<GstRow>(), from, to, fid);

        // Succeeded refunds only — pending/failed ones haven't moved money, so they can't reduce output tax.
        var ids = orders.Select(o => o.Id).ToList();
        var refunds = await _db.Refunds
            .Where(r => ids.Contains(r.OrderId) && r.Status == RefundStatus.Succeeded)
            .GroupBy(r => r.OrderId)
            .Select(g => new { OrderId = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Amount);

        // Live invoice per order — number AND total. An order can hold several invoice rows once one
        // has been cancelled and reissued, so key on the Active one rather than the Order.Invoice nav.
        // Orders that aren't paid yet have none — those rows show a blank invoice number and fall back
        // to the order total, which is what they would be invoiced for if paid today.
        var invoices = await _db.Set<Invoice>()
            .Where(i => ids.Contains(i.OrderId) && i.Status == InvoiceStatus.Active)
            .Select(i => new { i.OrderId, i.InvoiceNumber, i.TotalAmount })
            .ToDictionaryAsync(x => x.OrderId, x => x);

        var rows = orders.Select(o =>
        {
            invoices.TryGetValue(o.Id, out var inv);
            // Tax is declared on what was invoiced. For a franchise order that is the net of the
            // franchisee's share; for everything else it equals the order total and nothing changes.
            // Not-yet-paid orders carry no invoice (this report is not payment-filtered), so fall back
            // to what they WOULD be billed — the same rule InvoiceService applies when it issues one.
            var billable = inv?.TotalAmount ?? WouldBeInvoicedFor(o);
            var g = GstRowCalculator.For(o, refunds.TryGetValue(o.Id, out var amt) ? amt : 0m, billable);
            return new GstRow(o, inv?.InvoiceNumber, g.Taxable, g.Cgst, g.Sgst, g.Igst,
                g.Refunded, g.RefundedGst, g.InvoiceValue, g.Gst, g.FranchiseShare);
        }).ToList();

        return (rows, from, to, fid);
    }

    /// <summary>What this order would be invoiced for if it were paid today. Mirrors
    /// <c>InvoiceService.EnsureForOrderAsync</c>: a franchise order that recorded a share (or a net)
    /// bills the net; everything else bills the full total. Orders written before either value
    /// existed fall back to the total rather than showing ₹0.</summary>
    private static decimal WouldBeInvoicedFor(Order o) =>
        o.Source == OrderSource.Franchisee && o.FranchiseId != null
        && (o.FranchiseShareAmount > 0 || o.FranchiseNetPayable > 0)
            ? o.FranchiseNetPayable
            : o.TotalAmount;

    // Identical footer on all three GST reports so B2B + B2C add up to the combined one.
    private static List<KeyValuePair<string, string>> GstSummary(IReadOnlyList<GstRow> rows, string countLabel)
    {
        var gst = rows.Sum(r => r.Gst);
        var refundedGst = rows.Sum(r => r.RefundedGst);
        var share = rows.Sum(r => r.FranchiseShare);
        var summary = new List<KeyValuePair<string, string>>
        {
            new(countLabel, rows.Count.ToString()),
            new("Gross order value", $"₹{rows.Sum(r => r.Order.TotalAmount):N2}"),
        };
        // Only worth a line when franchise orders are in scope — otherwise it is always ₹0.
        if (share > 0)
            summary.Add(new("Less: franchisee share", $"−₹{share:N2}"));
        summary.AddRange(new KeyValuePair<string, string>[]
        {
            new("Invoice value", $"₹{rows.Sum(r => r.InvoiceValue):N2}"),
            new("Taxable value", $"₹{rows.Sum(r => r.Taxable):N2}"),
            new("CGST", $"₹{rows.Sum(r => r.Cgst):N2}"),
            new("SGST", $"₹{rows.Sum(r => r.Sgst):N2}"),
            new("IGST", $"₹{rows.Sum(r => r.Igst):N2}"),
            new("Total GST collected", $"₹{gst:N2}"),
            new("Less: GST on refunded orders", $"−₹{refundedGst:N2}"),
            new("Net GST", $"₹{(gst - refundedGst):N2}"),
            new("Refunded", $"₹{rows.Sum(r => r.Refunded):N2}")
        });
        return summary;
    }

    private static string CustomerName(Order o) =>
        o.CustomerType == CustomerType.Organization && !string.IsNullOrWhiteSpace(o.OrgName)
            ? o.OrgName!
            : o.BillingName ?? o.StudentName;

    // ─────────────── Renderers ───────────────

    private static (byte[] bytes, string filename) Render(ReportData d, bool excel, string stub)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        return excel
            ? (RenderExcel(d), $"{stub}-{stamp}.xlsx")
            : (RenderPdf(d), $"{stub}-{stamp}.pdf");
    }

    private static byte[] RenderExcel(ReportData d)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName(d.Title));

        var col = d.Headers.Length;
        var numeric = d.NumericColumns.ToHashSet();
        // Title block
        var titleCell = ws.Range(1, 1, 1, col).Merge();
        titleCell.Value = d.Title;
        titleCell.Style.Font.FontSize = 16;
        titleCell.Style.Font.Bold = true;

        if (!string.IsNullOrWhiteSpace(d.Subtitle))
        {
            var sub = ws.Range(2, 1, 2, col).Merge();
            sub.Value = d.Subtitle;
            sub.Style.Font.FontSize = 10;
            sub.Style.Font.FontColor = XLColor.Gray;
        }

        // Header row
        const int headerRow = 4;
        for (var i = 0; i < d.Headers.Length; i++)
        {
            var c = ws.Cell(headerRow, i + 1);
            c.Value = d.Headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.LightGray;
            if (numeric.Contains(i)) c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        // Data rows. Amount columns go in as real numbers so the sheet totals them; blanks stay blank.
        for (var r = 0; r < d.Rows.Count; r++)
            for (var i = 0; i < d.Rows[r].Length; i++)
            {
                var cell = ws.Cell(headerRow + 1 + r, i + 1);
                var raw = d.Rows[r][i];
                if (numeric.Contains(i)
                    && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                {
                    cell.Value = n;
                    cell.Style.NumberFormat.Format = "#,##0.00";
                }
                else cell.Value = raw;
            }

        // Summary
        if (d.Summary.Count > 0)
        {
            var startRow = headerRow + d.Rows.Count + 3;
            for (var i = 0; i < d.Summary.Count; i++)
            {
                ws.Cell(startRow + i, 1).Value = d.Summary[i].Key;
                ws.Cell(startRow + i, 1).Style.Font.Bold = true;
                ws.Cell(startRow + i, 2).Value = d.Summary[i].Value;
            }
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] RenderPdf(ReportData d)
    {
        return Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.A4.Landscape());
                p.Margin(24);
                // Wide financial tables (GST runs 12–14 columns) need a smaller face to stay readable.
                p.DefaultTextStyle(t => t.FontSize(
                    d.Headers.Length > 13 ? 6.5f : d.Headers.Length > 10 ? 7f : 9f));

                p.Header().Column(col =>
                {
                    col.Item().Text(d.Title).FontSize(16).SemiBold();
                    if (!string.IsNullOrWhiteSpace(d.Subtitle))
                        col.Item().Text(d.Subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
                    col.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                });

                p.Content().PaddingVertical(8).Column(col =>
                {
                    col.Item().Table(t =>
                    {
                        var numeric = d.NumericColumns.ToHashSet();

                        t.ColumnsDefinition(cd =>
                        {
                            for (var i = 0; i < d.Headers.Length; i++)
                                cd.RelativeColumn(d.ColumnWeights is { Length: > 0 } w && i < w.Length ? w[i] : 1f);
                        });

                        t.Header(h =>
                        {
                            for (var i = 0; i < d.Headers.Length; i++)
                            {
                                var c = h.Cell().Background(Colors.Grey.Lighten3).Padding(4);
                                if (numeric.Contains(i)) c.AlignRight().Text(d.Headers[i]).SemiBold();
                                else c.Text(d.Headers[i]).SemiBold();
                            }
                        });

                        foreach (var row in d.Rows)
                            for (var i = 0; i < row.Length; i++)
                            {
                                var c = t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(4);
                                if (numeric.Contains(i)) c.AlignRight().Text(row[i]);
                                else c.Text(row[i]);
                            }
                    });

                    if (d.Summary.Count > 0)
                    {
                        col.Item().PaddingTop(12).Column(s =>
                        {
                            s.Item().Text("Summary").SemiBold();
                            foreach (var kv in d.Summary)
                                s.Item().Row(r =>
                                {
                                    r.RelativeItem(1).Text(kv.Key).FontColor(Colors.Grey.Darken1);
                                    r.RelativeItem(2).Text(kv.Value).SemiBold();
                                });
                        });
                    }
                });

                p.Footer().AlignRight().Text(t =>
                {
                    t.Span("Generated ").FontColor(Colors.Grey.Darken1);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")).FontColor(Colors.Grey.Darken1);
                    t.Span("  •  Page ").FontColor(Colors.Grey.Darken1);
                    t.CurrentPageNumber().FontColor(Colors.Grey.Darken1);
                    t.Span(" / ").FontColor(Colors.Grey.Darken1);
                    t.TotalPages().FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    private byte[] RenderInvoicePdf(Order o)
    {
        return Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Size(PageSizes.A4);
                p.Margin(36);
                p.DefaultTextStyle(t => t.FontSize(10));

                p.Header().Row(r =>
                {
                    r.RelativeItem().Column(col =>
                    {
                        var cfg = _site.GetAsync().GetAwaiter().GetResult();
                        col.Item().Text(cfg.CompanyName ?? "My Store").FontSize(20).SemiBold().FontColor(Colors.Orange.Darken2);
                        if (!string.IsNullOrWhiteSpace(cfg.Address))
                            col.Item().Text(cfg.Address).FontSize(9).FontColor(Colors.Grey.Darken1);
                        var contact = string.Join("  •  ",
                            new[] { cfg.ContactEmail, !string.IsNullOrWhiteSpace(cfg.ContactPhone1) ? $"+91 {cfg.ContactPhone1}" : null }
                            .Where(s => !string.IsNullOrWhiteSpace(s)));
                        if (!string.IsNullOrEmpty(contact))
                            col.Item().Text(contact).FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                    r.ConstantItem(160).AlignRight().Column(col =>
                    {
                        col.Item().Text("TAX INVOICE").FontSize(14).SemiBold();
                        col.Item().Text($"# {o.OrderNumber}").FontSize(10);
                        col.Item().Text($"Date: {o.CreatedAt:dd MMM yyyy}").FontSize(9).FontColor(Colors.Grey.Darken1);
                        col.Item().Text($"Status: {o.Status}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });

                p.Content().PaddingVertical(20).Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Bill To").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                            c.Item().Text(o.BillingName ?? o.StudentName).SemiBold();
                            if (o.CustomerType == CustomerType.Organization && !string.IsNullOrWhiteSpace(o.OrgName))
                                c.Item().Text(o.OrgName);
                            if (!string.IsNullOrWhiteSpace(o.BillingAddress)) c.Item().Text(o.BillingAddress);
                            c.Item().Text($"{o.BillingCity ?? o.StudentCity ?? ""}  {o.BillingState ?? ""}  {o.BillingPincode ?? ""}".Trim());
                            c.Item().Text($"Phone: {o.StudentPhone}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            if (!string.IsNullOrWhiteSpace(o.StudentEmail))
                                c.Item().Text($"Email: {o.StudentEmail}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            if (!string.IsNullOrWhiteSpace(o.GstNumber))
                                c.Item().Text($"GSTIN: {o.GstNumber}").FontSize(9);
                        });
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Ship To").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                            if (!string.IsNullOrWhiteSpace(o.ShippingAddress))
                            {
                                c.Item().Text(o.BillingName ?? o.StudentName).SemiBold();
                                c.Item().Text(o.ShippingAddress);
                                c.Item().Text($"{o.ShippingCity ?? ""}  {o.ShippingState ?? ""}  {o.ShippingPincode ?? ""}".Trim());
                            }
                            else
                            {
                                c.Item().Text("Same as billing").FontColor(Colors.Grey.Darken1);
                            }
                            if (o.Franchise != null)
                                c.Item().PaddingTop(8).Text($"Issued via: {o.Franchise.Name} ({o.Franchise.Code})").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    col.Item().PaddingTop(18).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(5);
                            cd.ConstantColumn(50);
                            cd.ConstantColumn(80);
                            cd.ConstantColumn(80);
                            cd.ConstantColumn(90);
                        });

                        t.Header(h =>
                        {
                            void H(string s) => h.Cell().Background(Colors.Grey.Lighten3).Padding(6).Text(s).SemiBold();
                            H("Product");
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Qty").SemiBold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Unit ₹").SemiBold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Disc ₹").SemiBold();
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("Line ₹").SemiBold();
                        });

                        foreach (var item in o.Items)
                        {
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(c =>
                            {
                                c.Item().Text(item.ProductTitle);
                                if (!string.IsNullOrWhiteSpace(item.ModeName))
                                    c.Item().Text(item.ModeName).FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text(item.Quantity.ToString());
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text($"{item.UnitPrice:N2}");
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text($"{item.Discount:N2}");
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text($"{item.LineTotal:N2}");
                        }
                    });

                    col.Item().PaddingTop(12).AlignRight().Column(c =>
                    {
                        void Row(string label, string val) => c.Item().Row(r =>
                        {
                            r.ConstantItem(140).Text(label).FontColor(Colors.Grey.Darken1);
                            r.ConstantItem(110).AlignRight().Text(val);
                        });
                        Row("Subtotal", $"₹{o.Subtotal:N2}");
                        if (o.DiscountAmount > 0) Row("Discount", $"−₹{o.DiscountAmount:N2}");
                        if (o.ShippingCharges > 0) Row("Shipping", $"+₹{o.ShippingCharges:N2}");
                        Row($"GST ({o.GstClassification})", $"₹{o.GstAmount:N2}");
                        c.Item().PaddingTop(6).BorderTop(0.5f).BorderColor(Colors.Grey.Darken1).Row(r =>
                        {
                            r.ConstantItem(140).PaddingTop(6).Text("Total Payable").SemiBold();
                            r.ConstantItem(110).PaddingTop(6).AlignRight().Text($"₹{o.TotalAmount:N2}").FontSize(12).SemiBold();
                        });
                    });

                    // ── Payment ──
                    // Gateway = the channel the customer was sent to (Razorpay / Easebuzz / Cash …);
                    // Mode = what they actually paid with there (UPI / Credit Card / Net Banking …),
                    // captured from the gateway response. Each line is omitted when unknown.
                    if (o.PaymentMode.HasValue || !string.IsNullOrWhiteSpace(o.GatewayPaymentMode))
                    {
                        col.Item().PaddingTop(14).Column(c =>
                        {
                            c.Item().Text("Payment").FontSize(9).SemiBold().FontColor(Colors.Grey.Darken2);
                            if (o.PaymentMode.HasValue)
                                c.Item().Text($"Payment Gateway: {o.PaymentMode}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(o.GatewayPaymentMode))
                                c.Item().Text($"Payment Mode: {o.GatewayPaymentMode}").FontSize(9);
                            c.Item().Text($"Payment Status: {o.PaymentStatus}").FontSize(9);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(o.CustomerNotes))
                    {
                        col.Item().PaddingTop(16).Background(Colors.Grey.Lighten4).Padding(8).Column(c =>
                        {
                            c.Item().Text("Notes").SemiBold().FontSize(9);
                            c.Item().Text(o.CustomerNotes!).FontSize(9);
                        });
                    }
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("This is a computer-generated invoice. No signature required. ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    t.Span($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();
    }

    // ─────────────── Helpers ───────────────

    private static (DateTime from, DateTime to) ResolveRange(ReportFilter f)
    {
        // Bind from query string yields Kind=Unspecified, but the orders.CreatedAt column is
        // 'timestamp with time zone' — Npgsql demands UTC. Force Kind=Utc on both bounds.
        var rawTo = f.To ?? DateTime.UtcNow;
        var rawFrom = f.From ?? DateTime.UtcNow.AddMonths(-1);
        var to = DateTime.SpecifyKind(rawTo.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        var from = DateTime.SpecifyKind(rawFrom.Date, DateTimeKind.Utc);
        return (from, to);
    }

    private static string SubtitleFor(DateTime from, DateTime to, Guid? fid, string allLabel) =>
        $"{from:dd MMM yyyy} – {to:dd MMM yyyy}" + (fid.HasValue ? "" : $"  •  {allLabel}");

    // Excel rejects : \ / ? * [ ] in sheet names and caps them at 31 characters.
    private static string SheetName(string title)
    {
        var clean = new string(title.Select(ch => @"\/:*?[]".Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (clean.Length == 0) clean = "Report";
        return clean.Length > 31 ? clean[..31] : clean;
    }

    private static string Fmt(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
}
