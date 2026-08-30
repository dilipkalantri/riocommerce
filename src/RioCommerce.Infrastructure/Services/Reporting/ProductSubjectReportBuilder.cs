using RioCommerce.Core.DTOs.Reporting;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §12 — Product-Wise / Subject-Wise Report. Aggregated: one row per (product, subject) pair
/// rather than per order line, because the question this report answers is "how did each course
/// sell", not "what happened on each order".
///
/// <para>Selecting several products or subjects produces one consolidated result — the multi-select
/// widens the set, it does not split the report (§12, §17).</para>
/// </summary>
internal sealed class ProductSubjectReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.ProductSubject;

    private readonly OrderLineSource _source;
    public ProductSubjectReportBuilder(OrderLineSource source) => _source = source;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Product/Course",  ReportColumnKind.Text,    "product",  2.6f),
        new("Subject",         ReportColumnKind.Text,    "subject",  1.6f),
        new("Orders",          ReportColumnKind.Integer, "orders",   0.8f),
        new("Quantity",        ReportColumnKind.Integer, "qty",      0.9f),
        new("Gross Sales (₹)", ReportColumnKind.Money,   "gross",    1.3f),
        new("Discount (₹)",    ReportColumnKind.Money,   "discount", 1.2f),
        new("Taxable (₹)",     ReportColumnKind.Money,   "taxable",  1.2f),
        new("GST (₹)",         ReportColumnKind.Money,   "gst",      1.1f),
        new("Net Sales (₹)",   ReportColumnKind.Money,   "net",      1.3f)
    };

    private sealed record Row(
        string Product, string Subject, int Orders, int Qty,
        decimal Gross, decimal Discount, decimal Taxable, decimal Gst, decimal Net);

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var lines = await _source.LoadAsync(q, ct);

        var grouped = lines
            .GroupBy(l => new { l.ProductId, l.ProductTitle, Subject = l.SubjectName })
            .Select(g => new Row(
                g.Key.ProductTitle,
                g.Key.Subject ?? "—",
                // Distinct orders, not line count — a product bought twice on one order is one order.
                g.Select(x => x.OrderId).Distinct().Count(),
                g.Sum(x => x.Quantity),
                g.Sum(x => x.Money.Gross),
                g.Sum(x => x.Money.Discount),
                g.Sum(x => x.Money.Taxable),
                g.Sum(x => x.Money.Gst),
                g.Sum(x => x.Money.Net)))
            .ToList();

        var totals = new ReportTotals
        {
            // Across the whole report, distinct orders — summing the per-row Orders column would
            // count an order once per product it contained.
            TotalOrders = lines.Select(l => l.OrderId).Distinct().Count(),
            TotalQuantity = grouped.Sum(r => r.Qty),
            GrossAmount = grouped.Sum(r => r.Gross),
            Discount = grouped.Sum(r => r.Discount),
            TaxableAmount = grouped.Sum(r => r.Taxable),
            TotalGst = grouped.Sum(r => r.Gst),
            NetAmount = grouped.Sum(r => r.Net),
            TotalSales = grouped.Sum(r => r.Net)
        };
        totals.Extra.Add(("Products covered", grouped.Select(r => r.Product).Distinct().Count().ToString()));
        totals.Extra.Add(("Subjects covered", grouped.Select(r => r.Subject).Distinct().Count().ToString()));

        var sorted = Sort(grouped, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "product-subject",
            Title = "Product-Wise / Subject-Wise Report",
            Subtitle = ReportSupport.SalesSubtitle(q, "order date"),
            Columns = Cols,
            TotalRows = grouped.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(r => new[]
            {
                r.Product,
                r.Subject,
                ReportSupport.Int(r.Orders),
                ReportSupport.Int(r.Qty),
                ReportSupport.Money(r.Gross),
                ReportSupport.Money(r.Discount),
                ReportSupport.Money(r.Taxable),
                ReportSupport.Money(r.Gst),
                ReportSupport.Money(r.Net)
            }).ToList()
        };
    }

    private static IEnumerable<Row> Sort(List<Row> rows, ReportQuery q) => q.SortBy switch
    {
        "product" => ReportSupport.Order(rows, r => r.Product, q.SortDesc),
        "subject" => ReportSupport.Order(rows, r => r.Subject, q.SortDesc),
        "orders" => ReportSupport.Order(rows, r => r.Orders, q.SortDesc),
        "qty" => ReportSupport.Order(rows, r => r.Qty, q.SortDesc),
        "gross" => ReportSupport.Order(rows, r => r.Gross, q.SortDesc),
        "discount" => ReportSupport.Order(rows, r => r.Discount, q.SortDesc),
        "taxable" => ReportSupport.Order(rows, r => r.Taxable, q.SortDesc),
        "gst" => ReportSupport.Order(rows, r => r.Gst, q.SortDesc),
        "net" => ReportSupport.Order(rows, r => r.Net, q.SortDesc),
        // Best-selling first is the useful default for an aggregated sales view.
        _ => rows.OrderByDescending(r => r.Net)
    };
}
