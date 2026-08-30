using RioCommerce.Core.DTOs.Reporting;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §8 — Sales Report. One row per order line, which is what lets the same grid answer both
/// readings the requirement asks for: sorted by date it is date-wise sales, sorted by order number
/// it is order-wise, and either way it carries the product, subject, customer and payment detail
/// the section lists.
/// </summary>
internal sealed class SalesReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.Sales;

    private readonly OrderLineSource _source;
    public SalesReportBuilder(OrderLineSource source) => _source = source;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Order No.",      ReportColumnKind.Text,    "order",     1.3f),
        new("Order Date",     ReportColumnKind.Date,    "date",      1.1f),
        new("Source No.",     ReportColumnKind.Text,    "sourceno",  1.1f),
        new("Student",        ReportColumnKind.Text,    "student",   1.8f),
        new("Product/Course", ReportColumnKind.Text,    "product",   2.2f),
        new("Subject",        ReportColumnKind.Text,    "subject",   1.3f),
        new("Qty",            ReportColumnKind.Integer, "qty",       0.6f),
        new("Gross (₹)",      ReportColumnKind.Money,   "gross",     1.1f),
        new("Discount (₹)",   ReportColumnKind.Money,   "discount",  1.1f),
        // Broken out rather than folded into Discount — §36.3 forbids merging the two, and on a
        // franchise sale it is the figure that explains why the GST report's invoice value is
        // lower than the sale recorded here.
        new("Franchisee Disc (₹)", ReportColumnKind.Money, "fdisc",  1.3f),
        new("Taxable (₹)",    ReportColumnKind.Money,   "taxable",   1.1f),
        new("GST (₹)",        ReportColumnKind.Money,   "gst",       1f),
        new("Net (₹)",        ReportColumnKind.Money,   "net",       1.1f),
        new("Payment Status", ReportColumnKind.Status,  "paystatus", 1.1f),
        new("Payment Mode",   ReportColumnKind.Text,    "paymode",   1.1f),
        new("Faculty",        ReportColumnKind.Text,    "faculty",   1.6f),
        new("Franchisee",     ReportColumnKind.Text,    "franchise", 1.4f),
        new("Source",         ReportColumnKind.Text,    "source",    1f),
        new("Order Status",   ReportColumnKind.Status,  "status",    1f)
    };

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var lines = await _source.LoadAsync(q, ct);

        // Totals cover the whole filtered set and are taken BEFORE paging (§24).
        var franchiseDiscount = lines.Sum(l => l.Money.FranchiseDiscount);
        var totals = new ReportTotals
        {
            TotalOrders = lines.Select(l => l.OrderId).Distinct().Count(),
            TotalQuantity = lines.Sum(l => l.Quantity),
            GrossAmount = lines.Sum(l => l.Money.Gross),
            Discount = lines.Sum(l => l.Money.Discount),
            TaxableAmount = lines.Sum(l => l.Money.Taxable),
            TotalGst = lines.Sum(l => l.Money.Gst),
            NetAmount = lines.Sum(l => l.Money.Net)
        };
        if (franchiseDiscount > 0) totals.FranchiseDiscount = franchiseDiscount;

        var sorted = Sort(lines, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "sales",
            Title = "Sales Report",
            Subtitle = ReportSupport.SalesSubtitle(q, "order date"),
            Columns = Cols,
            TotalRows = lines.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(l => new[]
            {
                l.OrderNumber,
                ReportSupport.IstDate(l.OrderDate),
                ReportSupport.Text(l.SourceNo),
                l.StudentName,
                l.ProductTitle + (string.IsNullOrWhiteSpace(l.ModeName) ? "" : $" ({l.ModeName})"),
                ReportSupport.Text(l.SubjectName),
                ReportSupport.Int(l.Quantity),
                ReportSupport.Money(l.Money.Gross),
                ReportSupport.Money(l.Money.Discount),
                ReportSupport.MoneyOrBlank(l.Money.FranchiseDiscount),
                ReportSupport.Money(l.Money.Taxable),
                ReportSupport.Money(l.Money.Gst),
                ReportSupport.Money(l.Money.Net),
                l.PaymentStatus.ToString(),
                l.PaymentMode?.ToString() ?? "—",
                ReportSupport.Text(l.FacultyNames),
                ReportSupport.Text(l.FranchiseName),
                l.Source.ToString(),
                l.Status.ToString()
            }).ToList()
        };
    }

    private static IEnumerable<OrderLine> Sort(List<OrderLine> rows, ReportQuery q) => q.SortBy switch
    {
        "order" => ReportSupport.Order(rows, l => l.OrderNumber, q.SortDesc),
        "sourceno" => ReportSupport.Order(rows, l => l.SourceNo ?? "", q.SortDesc),
        "student" => ReportSupport.Order(rows, l => l.StudentName, q.SortDesc),
        "product" => ReportSupport.Order(rows, l => l.ProductTitle, q.SortDesc),
        "subject" => ReportSupport.Order(rows, l => l.SubjectName ?? "", q.SortDesc),
        "qty" => ReportSupport.Order(rows, l => l.Quantity, q.SortDesc),
        "gross" => ReportSupport.Order(rows, l => l.Money.Gross, q.SortDesc),
        "discount" => ReportSupport.Order(rows, l => l.Money.Discount, q.SortDesc),
        "fdisc" => ReportSupport.Order(rows, l => l.Money.FranchiseDiscount, q.SortDesc),
        "taxable" => ReportSupport.Order(rows, l => l.Money.Taxable, q.SortDesc),
        "gst" => ReportSupport.Order(rows, l => l.Money.Gst, q.SortDesc),
        "net" => ReportSupport.Order(rows, l => l.Money.Net, q.SortDesc),
        "paystatus" => ReportSupport.Order(rows, l => l.PaymentStatus, q.SortDesc),
        "paymode" => ReportSupport.Order(rows, l => l.PaymentMode?.ToString() ?? "", q.SortDesc),
        "faculty" => ReportSupport.Order(rows, l => l.FacultyNames ?? "", q.SortDesc),
        "franchise" => ReportSupport.Order(rows, l => l.FranchiseName ?? "", q.SortDesc),
        "source" => ReportSupport.Order(rows, l => l.Source, q.SortDesc),
        "status" => ReportSupport.Order(rows, l => l.Status, q.SortDesc),
        _ => ReportSupport.Order(rows, l => l.OrderDate, q.SortDesc)
    };
}
