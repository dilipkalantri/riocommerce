using System.Globalization;

namespace RioCommerce.Core.DTOs.Reporting;

/// <summary>How a column is rendered and aligned. Money and Number are right-aligned and go into
/// Excel as real numeric cells so the sheet can total them; everything else stays text.</summary>
public enum ReportColumnKind
{
    Text,
    Integer,
    Money,
    Date,
    Status
}

/// <param name="Key">Sort key. Null makes the column unsortable — correct for derived columns the
/// database cannot order by.</param>
/// <param name="Weight">Relative width. Only consulted by the PDF/Excel sizing.</param>
public sealed record ReportColumn(
    string Header,
    ReportColumnKind Kind = ReportColumnKind.Text,
    string? Key = null,
    float Weight = 1f)
{
    public bool IsNumeric => Kind is ReportColumnKind.Money or ReportColumnKind.Integer;
    public bool IsSortable => !string.IsNullOrEmpty(Key);
}

/// <summary>
/// One label/value pair in a report's summary bar.
///
/// <para>A named record rather than the <c>(string, string)</c> tuple this used to be, for one
/// reason: <c>System.Text.Json</c> does not serialise a ValueTuple's fields, so every entry left the
/// API as an empty <c>{}</c>. The Faculty-Wise report lost Teacher Share, GST on Share, Total Payout
/// and Faculty Covered that way — visible in the admin UI, which reads the service directly, but
/// gone for anything consuming the JSON.</para>
///
/// <para>Positional, so the <c>foreach (var (label, value) in …)</c> the grid and both exporters
/// already use keeps working, and implicitly convertible from the tuple so the twenty
/// <c>Extra.Add(("…", "…"))</c> call sites across the report builders needed no edit. Labels,
/// values and ordering are all unchanged — this is a serialisation fix, not a data one.</para>
/// </summary>
public sealed record ReportTotalItem(string Label, string Value)
{
    public static implicit operator ReportTotalItem((string Label, string Value) t) => new(t.Label, t.Value);
}

/// <summary>
/// Filter-aware totals (§24). Every field is nullable and only the ones a given report actually
/// computes are set — <see cref="Items"/> then yields exactly those, in a fixed order, for the
/// summary bar and the export footer. Nullable rather than zero so "this report has no GST" and
/// "GST came to zero" stay distinguishable.
///
/// <para>These are always computed over the FULL filtered result before paging, never over the
/// visible page.</para>
/// </summary>
public class ReportTotals
{
    public int? TotalOrders { get; set; }
    public int? TotalQuantity { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal? Discount { get; set; }
    public decimal? FranchiseDiscount { get; set; }
    public decimal? TaxableAmount { get; set; }
    public decimal? Cgst { get; set; }
    public decimal? Sgst { get; set; }
    public decimal? Igst { get; set; }
    public decimal? TotalGst { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? TotalSales { get; set; }
    public decimal? TotalPayable { get; set; }
    public decimal? AmountPaid { get; set; }
    public decimal? BalancePayable { get; set; }
    public decimal? TotalInvoiceAmount { get; set; }
    public decimal? Refunded { get; set; }

    /// <summary>Extra report-specific totals that do not fit the standard set — appended after it.</summary>
    public List<ReportTotalItem> Extra { get; } = new();

    /// <summary>The set totals, in display order. Money is formatted with the rupee sign; counts
    /// are plain.</summary>
    public IEnumerable<ReportTotalItem> Items()
    {
        if (TotalOrders is { } o) yield return ("Total Orders", o.ToString("N0", CultureInfo.InvariantCulture));
        if (TotalQuantity is { } q) yield return ("Total Quantity", q.ToString("N0", CultureInfo.InvariantCulture));
        if (GrossAmount is { } g) yield return ("Gross Amount", Money(g));
        if (Discount is { } d) yield return ("Total Discount", Money(d));
        if (FranchiseDiscount is { } fd) yield return ("Total Franchisee Discount", Money(fd));
        if (TaxableAmount is { } t) yield return ("Taxable Amount", Money(t));
        if (Cgst is { } c) yield return ("CGST", Money(c));
        if (Sgst is { } s) yield return ("SGST", Money(s));
        if (Igst is { } i) yield return ("IGST", Money(i));
        if (TotalGst is { } tg) yield return ("Total GST", Money(tg));
        if (NetAmount is { } n) yield return ("Net Amount", Money(n));
        if (TotalInvoiceAmount is { } ti) yield return ("Total Invoice Amount", Money(ti));
        if (Refunded is { } r) yield return ("Refunded", Money(r));
        if (TotalSales is { } ts) yield return ("Total Sales Amount", Money(ts));
        if (TotalPayable is { } tp) yield return ("Total Payable", Money(tp));
        if (AmountPaid is { } ap) yield return ("Amount Paid", Money(ap));
        if (BalancePayable is { } bp) yield return ("Balance Payable", Money(bp));
        foreach (var e in Extra) yield return e;
    }

    private static string Money(decimal v) => "₹" + v.ToString("N2", CultureInfo.InvariantCulture);
}

/// <summary>
/// The output shape of every report. Columns plus pre-rendered string cells, rather than a typed
/// row per report, is deliberate: it lets one Blazor component, one Excel writer and one CSV
/// writer serve all eight reports, which is exactly what §29 asks for. Each report's identity
/// lives in its columns and its query, not in a bespoke rendering path.
/// </summary>
public class ReportTable
{
    public ReportType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;

    public List<ReportColumn> Columns { get; set; } = new();

    /// <summary>Rendered cells, one array per row, aligned to <see cref="Columns"/>.</summary>
    public List<string[]> Rows { get; set; } = new();

    public ReportTotals Totals { get; set; } = new();

    /// <summary>Row count across the whole filtered result, not this page — drives the pager.</summary>
    public int TotalRows { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; }

    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);

    /// <summary>Slug used in routes and export filenames.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// What to say when there are no rows. Defaults to advice about the filters, which is right for
    /// most reports — but wrong for one whose emptiness usually means something is unconfigured
    /// rather than over-filtered. Telling a user to widen the date range when the real cause is
    /// "no sharing rules exist" sends them looking in the wrong place.
    /// </summary>
    public string EmptyMessage { get; set; } = "Nothing to show. Widen the date range or clear a filter.";
}

/// <summary>Dropdown contents for the shared filter bar. Loaded once per page rather than per
/// report so switching reports does not re-query the lookups.</summary>
public class ReportFilterOptions
{
    public List<ReportOption> Faculty { get; set; } = new();
    public List<ReportOption> Products { get; set; } = new();
    public List<ReportOption> Subjects { get; set; } = new();
    public List<ReportOption> Franchises { get; set; } = new();
    public List<string> Couriers { get; set; } = new();
}

public sealed record ReportOption(Guid Id, string Name);
