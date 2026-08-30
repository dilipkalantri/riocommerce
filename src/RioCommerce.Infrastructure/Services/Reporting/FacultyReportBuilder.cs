using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §10 — Faculty-Wise Report. ONE ROW PER EARNED SHARE — that is, per (order line × faculty) — so
/// every individual order a faculty earned on is visible on its own line, the same presentation the
/// Franchisee-Wise report uses.
///
/// <para>This used to aggregate per (faculty, product), which collapsed four separate orders for one
/// course into a single row reading "Orders: 4". The underlying ledger has always been per order
/// line; only the presentation grouped it. Nothing about the money changed when that grouping was
/// removed — each row now shows the same per-line figures that were previously being summed, and the
/// report totals are still computed over the full result set, not the visible page.</para>
///
/// <para><b>Only courses the faculty actually earns a share on appear.</b> This is a share report,
/// so a faculty attached to a course with no sharing rule has nothing to report and is left out.
/// Reading the <c>FacultyShareEntries</c> ledger directly makes that structural — the alternative,
/// listing every ProductFaculty pairing and left-joining the ledger, filled the page with rows
/// reading "—" and ₹0.00 that buried the handful of real shares.</para>
///
/// <para>The ledger also snapshots the rule and the faculty's GST registration at the time of each
/// order, so the report shows what was actually earned. Recomputing from today's configuration
/// would restate settled history (§26).</para>
///
/// <para><b>Sales are not divided between co-faculty; the share is.</b> Quantity and sales value
/// describe the course, so where two faculty both earn on one line each row shows the full course
/// figure — summing the sales column down the page would double-count. The report's Total Sales is
/// therefore computed over distinct order lines rather than by adding the rows up, and it says so
/// when the result contains a co-taught line. The Teacher Share column is a per-faculty
/// entitlement and does sum correctly.</para>
/// </summary>
internal sealed class FacultyReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.Faculty;

    private readonly RioCommerceDbContext _db;
    public FacultyReportBuilder(RioCommerceDbContext db) => _db = db;

    private static readonly List<ReportColumn> Cols = new()
    {
        // Faculty leads, then the order-level identity columns, mirroring Franchisee-Wise. The old
        // "Orders" count column is gone: on a per-order row it would read 1 on every line.
        new("Faculty",          ReportColumnKind.Text,    "faculty",  1.9f),
        new("Order No.",        ReportColumnKind.Text,    "order",    1.3f),
        new("Order Date",       ReportColumnKind.Date,    "date",     1.1f),
        new("Student",          ReportColumnKind.Text,    "student",  1.6f),
        new("Product/Course",   ReportColumnKind.Text,    "product",  2.2f),
        new("Subject",          ReportColumnKind.Text,    "subject",  1.3f),
        new("Qty",              ReportColumnKind.Integer, "qty",      0.6f),
        new("Gross Sales (₹)",  ReportColumnKind.Money,   "gross",    1.2f),
        new("Discount (₹)",     ReportColumnKind.Money,   "discount", 1.1f),
        new("Net Sales (₹)",    ReportColumnKind.Money,   "net",      1.2f),
        new("Share / Rate",     ReportColumnKind.Text,    "rate",     1.1f),
        new("Teacher Share (₹)",ReportColumnKind.Money,   "share",    1.3f),
        new("GST on Share (₹)", ReportColumnKind.Money,   "sharegst", 1.3f),
        new("Total Payout (₹)", ReportColumnKind.Money,   "payout",   1.3f)
    };

    private sealed record Row(
        string Faculty, string OrderNumber, DateTime OrderDate, string Student,
        string Product, string Subject, int Qty,
        decimal Gross, decimal Discount, decimal Net,
        string Rate, decimal Share, decimal ShareGst, decimal Payout);

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var (fromUtc, toUtc) = ReportSupport.ResolveRange(q);

        var orders = _db.Orders.AsNoTracking().ExcludeWalletTopUps()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc);

        if (q.FranchiseScopeId is { } scope) orders = orders.Where(o => o.FranchiseId == scope);
        else if (q.FranchiseIds.Count > 0)
            orders = orders.Where(o => o.FranchiseId != null && q.FranchiseIds.Contains(o.FranchiseId.Value));

        // Same revenue basis as every other sales figure in the admin — a cancelled order must not
        // credit a faculty member with a sale, and must not appear as one on their share report.
        orders = ReportSupport.ApplyRevenueStatus(orders, q);

        if (q.PaymentStatuses.Count > 0) orders = orders.Where(o => q.PaymentStatuses.Contains(o.PaymentStatus));
        if (q.Sources.Count > 0) orders = orders.Where(o => q.Sources.Contains(o.Source));

        // ── Driven by the earnings ledger, so every row IS a share ──
        // The earlier version started from ProductFaculty and left-joined the ledger, which meant
        // every faculty attached to a course appeared whether or not they earn anything from it —
        // pages of rows reading "—" and ₹0.00. On a share report those are noise: a faculty with no
        // sharing rule for a course has no share to report. Starting from FacultyShareEntries makes
        // that structural rather than a filter, and the ledger is the authoritative record of what
        // was actually earned.
        var q1 = from e in _db.FacultyShareEntries.AsNoTracking()
                 join oi in _db.OrderItems.AsNoTracking() on e.OrderItemId equals oi.Id
                 join o in orders on e.OrderId equals o.Id
                 select new { Entry = e, Item = oi, Order = o };

        if (q.FacultyIds.Count > 0) q1 = q1.Where(x => q.FacultyIds.Contains(x.Entry.FacultyId));
        if (q.ProductIds.Count > 0) q1 = q1.Where(x => q.ProductIds.Contains(x.Entry.ProductId));
        if (q.SubjectIds.Count > 0)
            // Any subject the course covers — a filter on which rows appear, not on their value.
            q1 = q1.Where(x => _db.ProductSubjects
                    .Any(ps => ps.ProductId == x.Item.ProductId && q.SubjectIds.Contains(ps.SubjectId))
                || (x.Item.Product.SubjectId != null && q.SubjectIds.Contains(x.Item.Product.SubjectId.Value)));

        if (ReportSupport.Term(q.Search) is { } term)
        {
            var like = $"%{term}%";
            q1 = q1.Where(x =>
                EF.Functions.ILike(x.Entry.Faculty.DisplayName, like)
                || EF.Functions.ILike(x.Entry.ProductTitle, like)
                || EF.Functions.ILike(x.Entry.OrderNumber, like));
        }

        var raw = await q1.Select(x => new
        {
            x.Entry.FacultyId,
            FacultyName = x.Entry.Faculty.DisplayName,
            x.Entry.ProductId,
            x.Entry.ProductTitle,
            SubjectName = x.Item.Product.Subject != null ? x.Item.Product.Subject.Name : null,
            x.Entry.OrderId,
            // Order identity for the per-order rows. OrderNumber comes off the ledger entry because
            // that is the copy the report's search box already matches on; the date is the order's
            // own CreatedAt, which is the field the date-range filter above uses.
            x.Entry.OrderNumber,
            OrderDate = x.Order.CreatedAt,
            x.Order.StudentName,
            x.Entry.OrderItemId,
            x.Item.Quantity,
            x.Item.UnitPrice,
            x.Item.Discount,
            x.Item.LineTotal,
            // Snapshotted at the time of the order — rule, share and the faculty's GST registration
            // as they stood then. Never re-derived from today's configuration: that would restate
            // payouts already settled.
            x.Entry.ShareType,
            x.Entry.ShareValue,
            x.Entry.ShareAmount,
            x.Entry.GstOnShare,
            x.Entry.TotalPayout
        }).ToListAsync(ct);

        // ── One row per ledger entry: the individual order line, not a (faculty, product) roll-up ──
        // Every money expression below is the same one the grouped version used per line; the only
        // difference is that the sum across the group is gone, so each order stands on its own row.
        // The rate is likewise the rule snapshotted on THIS entry rather than the group's last one,
        // which also means a mid-period rate change now shows the rate each order actually earned at
        // instead of applying the newest rate to the whole group.
        var rows = raw
            .Select(r => new Row(
                r.FacultyName,
                r.OrderNumber,
                r.OrderDate,
                r.StudentName,
                r.ProductTitle,
                r.SubjectName ?? "—",
                r.Quantity,
                Math.Round(OrderMoney.LineGross(r.UnitPrice, r.Quantity), 2),
                Math.Round(r.Discount * r.Quantity, 2),
                Math.Round(r.LineTotal, 2),
                r.ShareType == SharingType.Percentage
                    ? $"{r.ShareValue:0.##}%"
                    : $"₹{r.ShareValue:0.##}/unit",
                r.ShareAmount,
                r.GstOnShare,
                r.TotalPayout))
            .ToList();

        // Sales totals go over DISTINCT order lines: a course taught by three faculty appears on
        // three rows, and adding the rows up would treble the revenue. The share columns are
        // per-faculty entitlements, so those do sum across rows.
        var distinctLines = raw
            .GroupBy(r => r.OrderItemId)
            .Select(g => g.First())
            .ToList();

        var totals = new ReportTotals
        {
            TotalOrders = distinctLines.Select(l => l.OrderId).Distinct().Count(),
            TotalQuantity = distinctLines.Sum(l => l.Quantity),
            GrossAmount = Math.Round(distinctLines.Sum(l => OrderMoney.LineGross(l.UnitPrice, l.Quantity)), 2),
            Discount = Math.Round(distinctLines.Sum(l => l.Discount * l.Quantity), 2),
            TotalSales = Math.Round(distinctLines.Sum(l => l.LineTotal), 2)
        };
        // Unchanged by the regrouping: these sum one value per ledger entry either way. Summing the
        // per-order rows gives the same figure the per-group rows did, because each group's value
        // was itself the sum of exactly these entries.
        totals.Extra.Add(("Faculty covered", rows.Select(r => r.Faculty).Distinct().Count().ToString()));
        totals.Extra.Add(("Teacher share (cost)", $"₹{rows.Sum(r => r.Share):N2}"));
        totals.Extra.Add(("GST on share (ITC)", $"₹{rows.Sum(r => r.ShareGst):N2}"));
        totals.Extra.Add(("Total payout", $"₹{rows.Sum(r => r.Payout):N2}"));

        // A course with more than one faculty produces one row per faculty, each showing the FULL
        // course sales — that is the point of a faculty-wise view, but it means the sales columns
        // cannot be added up down the page. The totals above already de-duplicate; someone
        // selecting the column in the exported sheet would not. Warn only when the result actually
        // contains a co-taught course, so it reads as a fact about this data rather than boilerplate.
        var coTaught = raw
            .GroupBy(r => r.OrderItemId)
            .Count(g => g.Select(x => x.FacultyId).Distinct().Count() > 1);
        if (coTaught > 0)
            totals.Extra.Add(("⚠ Co-taught lines",
                $"{coTaught} — sales repeat per faculty, so the sales columns do not sum down the page. " +
                "The totals here already count each sale once."));

        var sorted = Sort(rows, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "faculty",
            Title = "Faculty-Wise Report",
            Subtitle = ReportSupport.SalesSubtitle(q, "order date") + "  •  earned shares only",
            // An empty faculty report usually means no sharing rules are configured for the courses
            // that sold, not that the filters are too tight. Point at the right place.
            EmptyMessage = "No faculty shares were earned in this period. "
                + "A course only earns a share when a sharing rule exists for that faculty — "
                + "check Catalog › Faculty Sharing.",
            Columns = Cols,
            TotalRows = rows.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(r => new[]
            {
                r.Faculty,
                r.OrderNumber,
                ReportSupport.IstDate(r.OrderDate),
                ReportSupport.Text(r.Student),
                r.Product,
                r.Subject,
                ReportSupport.Int(r.Qty),
                ReportSupport.Money(r.Gross),
                ReportSupport.Money(r.Discount),
                ReportSupport.Money(r.Net),
                r.Rate,
                ReportSupport.Money(r.Share),
                ReportSupport.Money(r.ShareGst),
                ReportSupport.Money(r.Payout)
            }).ToList()
        };
    }

    private static IEnumerable<Row> Sort(List<Row> rows, ReportQuery q) => q.SortBy switch
    {
        "order" => ReportSupport.Order(rows, r => r.OrderNumber, q.SortDesc),
        "date" => ReportSupport.Order(rows, r => r.OrderDate, q.SortDesc),
        "student" => ReportSupport.Order(rows, r => r.Student, q.SortDesc),
        "product" => ReportSupport.Order(rows, r => r.Product, q.SortDesc),
        "subject" => ReportSupport.Order(rows, r => r.Subject, q.SortDesc),
        "qty" => ReportSupport.Order(rows, r => r.Qty, q.SortDesc),
        "gross" => ReportSupport.Order(rows, r => r.Gross, q.SortDesc),
        "discount" => ReportSupport.Order(rows, r => r.Discount, q.SortDesc),
        "net" => ReportSupport.Order(rows, r => r.Net, q.SortDesc),
        "share" => ReportSupport.Order(rows, r => r.Share, q.SortDesc),
        "sharegst" => ReportSupport.Order(rows, r => r.ShareGst, q.SortDesc),
        "payout" => ReportSupport.Order(rows, r => r.Payout, q.SortDesc),
        "faculty" => ReportSupport.Order(rows, r => r.Faculty, q.SortDesc),
        // Keeps each faculty's orders together and newest-first inside that block — the same default
        // Franchisee-Wise uses. Without it the per-order rows interleave across faculty and the
        // report stops reading as faculty-wise at all.
        _ => rows.OrderBy(r => r.Faculty).ThenByDescending(r => r.OrderDate)
    };
}
