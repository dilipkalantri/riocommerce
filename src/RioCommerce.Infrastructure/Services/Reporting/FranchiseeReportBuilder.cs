using RioCommerce.Core.DTOs.Reporting;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §11 — Franchisee-Wise Report. One row per order line belonging to a franchise order, with the
/// franchisee's own discount broken out alongside the student discount.
///
/// <para>Selecting several franchisees produces ONE consolidated result rather than a report per
/// franchisee, which is what §11 and §17 ask for — the multi-select simply widens the set and the
/// totals cover all of it.</para>
///
/// <para>Non-franchise orders are excluded: this report is about franchisee performance, and rows
/// with no franchisee would make the Franchisee Discount column meaningless.</para>
/// </summary>
internal sealed class FranchiseeReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.Franchisee;

    private readonly OrderLineSource _source;
    public FranchiseeReportBuilder(OrderLineSource source) => _source = source;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Franchisee",         ReportColumnKind.Text,    "franchise", 1.8f),
        new("Order No.",          ReportColumnKind.Text,    "order",     1.3f),
        new("Order Date",         ReportColumnKind.Date,    "date",      1.1f),
        new("Student",            ReportColumnKind.Text,    "student",   1.6f),
        new("Product/Course",     ReportColumnKind.Text,    "product",   2.2f),
        new("Subject",            ReportColumnKind.Text,    "subject",   1.3f),
        new("Qty",                ReportColumnKind.Integer, "qty",       0.6f),
        new("Gross Sales (₹)",    ReportColumnKind.Money,   "gross",     1.2f),
        new("Discount (₹)",       ReportColumnKind.Money,   "discount",  1.1f),
        new("Franchisee Disc (₹)",ReportColumnKind.Money,   "fdisc",     1.3f),
        new("Taxable (₹)",        ReportColumnKind.Money,   "taxable",   1.1f),
        new("GST (₹)",            ReportColumnKind.Money,   "gst",       1f),
        new("Net Sales (₹)",      ReportColumnKind.Money,   "net",       1.2f),
        new("Order Status",       ReportColumnKind.Status,  "status",    1f),
        new("Payment Status",     ReportColumnKind.Status,  "paystatus", 1.1f)
    };

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var all = await _source.LoadAsync(q, ct);
        var lines = all.Where(l => l.FranchiseId != null).ToList();

        var totals = new ReportTotals
        {
            TotalOrders = lines.Select(l => l.OrderId).Distinct().Count(),
            TotalQuantity = lines.Sum(l => l.Quantity),
            GrossAmount = lines.Sum(l => l.Money.Gross),
            Discount = lines.Sum(l => l.Money.Discount),
            FranchiseDiscount = lines.Sum(l => l.Money.FranchiseDiscount),
            TaxableAmount = lines.Sum(l => l.Money.Taxable),
            TotalGst = lines.Sum(l => l.Money.Gst),
            NetAmount = lines.Sum(l => l.Money.Net),
            TotalSales = lines.Sum(l => l.Money.Net)
        };
        totals.Extra.Add(("Franchisees covered",
            lines.Select(l => l.FranchiseId).Distinct().Count().ToString()));

        var sorted = Sort(lines, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "franchisee",
            Title = "Franchisee-Wise Report",
            Subtitle = ReportSupport.SalesSubtitle(q, "order date"),
            Columns = Cols,
            TotalRows = lines.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(l => new[]
            {
                ReportSupport.Text(l.FranchiseName),
                l.OrderNumber,
                ReportSupport.IstDate(l.OrderDate),
                l.StudentName,
                l.ProductTitle,
                ReportSupport.Text(l.SubjectName),
                ReportSupport.Int(l.Quantity),
                ReportSupport.Money(l.Money.Gross),
                ReportSupport.Money(l.Money.Discount),
                ReportSupport.Money(l.Money.FranchiseDiscount),
                ReportSupport.Money(l.Money.Taxable),
                ReportSupport.Money(l.Money.Gst),
                ReportSupport.Money(l.Money.Net),
                l.Status.ToString(),
                l.PaymentStatus.ToString()
            }).ToList()
        };
    }

    private static IEnumerable<OrderLine> Sort(List<OrderLine> rows, ReportQuery q) => q.SortBy switch
    {
        "order" => ReportSupport.Order(rows, l => l.OrderNumber, q.SortDesc),
        "date" => ReportSupport.Order(rows, l => l.OrderDate, q.SortDesc),
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
        "status" => ReportSupport.Order(rows, l => l.Status, q.SortDesc),
        "paystatus" => ReportSupport.Order(rows, l => l.PaymentStatus, q.SortDesc),
        // Default groups a consolidated multi-franchisee run by franchisee, newest order first
        // inside each — otherwise the rows interleave and the report reads as noise.
        _ => rows.OrderBy(l => l.FranchiseName).ThenByDescending(l => l.OrderDate)
    };
}
