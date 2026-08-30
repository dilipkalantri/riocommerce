using System.Globalization;
using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Enums;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// Shared plumbing for every report builder: the date window, cell formatting, search matching and
/// paging. Kept in one place so all eight reports behave identically (§29) — a change to how a date
/// is bounded or a rupee is rendered lands everywhere at once.
/// </summary>
internal static class ReportSupport
{
    /// <summary>
    /// Statuses that are not revenue. A draft was never placed, a cancelled order never happened,
    /// and a refunded one was reversed — counting any of them as a sale overstates income.
    ///
    /// <para>The same three names are defined in eight other services (<c>DashboardService</c>,
    /// <c>PayoutCalculationService</c>, <c>FranchiseService</c>, …). This is the reporting module's
    /// copy; a sales report that disagreed with the dashboard about what a sale is would be worse
    /// than useless (§36.15).</para>
    /// </summary>
    public static readonly OrderStatus[] NonRevenue =
        { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };

    /// <summary>
    /// Applies the report's order-status filter.
    ///
    /// <para>When the user has picked statuses explicitly, exactly those are returned — asking for
    /// Cancelled has to actually show cancelled orders. When they have picked none, non-revenue
    /// statuses are excluded, so the default view of a sales figure means the same thing here as
    /// everywhere else in the admin.</para>
    /// </summary>
    public static IQueryable<Core.Entities.Order> ApplyRevenueStatus(
        IQueryable<Core.Entities.Order> q, ReportQuery query) =>
        query.OrderStatuses.Count > 0
            ? q.Where(o => query.OrderStatuses.Contains(o.Status))
            : q.Where(o => !NonRevenue.Contains(o.Status));

    /// <summary>True when the result is the default revenue-only view, so the report can say so
    /// rather than leaving the reader to wonder where the cancelled orders went.</summary>
    public static bool IsRevenueOnly(ReportQuery query) => query.OrderStatuses.Count == 0;

    /// <summary>
    /// India Standard Time. Report day boundaries are IST, not UTC.
    ///
    /// <para>This matters and is a deliberate departure from the reporting code being replaced. The
    /// business, its working day and its filing periods are all in IST, but order timestamps are
    /// stored UTC. Bounding "1 Aug to 10 Aug" on UTC dates would put every sale made between
    /// midnight and 05:30 IST into the previous day — so a month-end report would silently exclude
    /// the last evening's takings and a GST period would be short by the same. The user picks IST
    /// calendar dates; these are converted to the matching UTC instants before querying.</para>
    /// </summary>
    private static readonly TimeSpan IstOffset = TimeSpan.FromMinutes(330);

    /// <summary>Default window when the user has not picked one: the last full month up to today.</summary>
    private const int DefaultWindowDays = 30;

    /// <summary>
    /// Resolves the query's From/To — read as IST calendar dates — into the half-open UTC instant
    /// range <c>[from, to)</c> to filter on.
    ///
    /// <para>Half-open rather than inclusive-of-23:59:59.9999999: an order stamped exactly at
    /// midnight belongs to the next day, and comparing against a "last tick" value is the classic
    /// way to lose a row to sub-tick precision.</para>
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) ResolveRange(ReportQuery q)
    {
        var todayIst = DateTime.UtcNow.Add(IstOffset).Date;

        var fromIst = (q.From ?? todayIst.AddDays(-DefaultWindowDays)).Date;
        var toIst = (q.To ?? todayIst).Date;

        // A range entered backwards is a slip, not a request for zero rows.
        if (toIst < fromIst) (fromIst, toIst) = (toIst, fromIst);

        var fromUtc = DateTime.SpecifyKind(fromIst - IstOffset, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(toIst.AddDays(1) - IstOffset, DateTimeKind.Utc);
        return (fromUtc, toUtc);
    }

    /// <summary>The same window expressed as IST calendar dates, for the report subtitle and for
    /// the reports that filter on a <c>date</c> column rather than a timestamp.</summary>
    public static (DateOnly From, DateOnly To) ResolveDateOnlyRange(ReportQuery q)
    {
        var (fromUtc, toUtc) = ResolveRange(q);
        return (DateOnly.FromDateTime(fromUtc.Add(IstOffset)),
                DateOnly.FromDateTime(toUtc.Add(IstOffset).AddDays(-1)));
    }

    /// <summary>Renders a stored UTC instant as an IST date for display, so the grid agrees with
    /// the date filter the user typed.</summary>
    public static string IstDate(DateTime utc) => utc.Add(IstOffset).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    public static string IstDateTime(DateTime utc) => utc.Add(IstOffset).ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string IstDate(DateTime? utc) => utc.HasValue ? IstDate(utc.Value) : "—";

    public static string Date(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>Money cell. Plain digits, no rupee sign and no thousands separator — the column
    /// header carries the unit, and Excel needs a parseable number to write a numeric cell.</summary>
    public static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Blank rather than "0.00" for money that is structurally absent — an unrefunded
    /// order, a non-franchise line. A column of zeroes reads as data; blanks read as not-applicable.</summary>
    public static string MoneyOrBlank(decimal v) => v == 0m ? "" : Money(v);

    public static string Text(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    /// <summary>Normalises a search term. Returns null when there is nothing to search for, which
    /// callers treat as "no search filter".</summary>
    public static string? Term(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    /// <summary>Subtitle line shown under every report title — the window plus a note of which
    /// dimensions were narrowed, so a printed or exported copy is self-describing.</summary>
    public static string Subtitle(ReportQuery q, string dateFieldLabel)
    {
        var (from, to) = ResolveDateOnlyRange(q);
        var parts = new List<string> { $"{Date(from)} – {Date(to)} ({dateFieldLabel})" };

        void Note(int count, string singular, string plural)
        {
            if (count == 1) parts.Add($"1 {singular}");
            else if (count > 1) parts.Add($"{count} {plural}");
        }

        Note(q.FacultyIds.Count, "faculty", "faculty");
        Note(q.ProductIds.Count, "product", "products");
        Note(q.SubjectIds.Count, "subject", "subjects");
        Note(q.FranchiseIds.Count, "franchisee", "franchisees");

        if (q.FranchiseScopeId.HasValue) parts.Add("own franchise only");
        if (!string.IsNullOrWhiteSpace(q.Search)) parts.Add($"search “{q.Search.Trim()}”");

        return string.Join("  •  ", parts);
    }

    /// <summary>
    /// Subtitle for the order-based reports, which states the revenue basis outright.
    ///
    /// <para>A reader comparing this against a raw order count needs to know that drafts, cancelled
    /// and refunded orders are out — otherwise the difference looks like a defect in the report.</para>
    /// </summary>
    public static string SalesSubtitle(ReportQuery q, string dateFieldLabel)
    {
        var basis = IsRevenueOnly(q)
            ? "excludes draft, cancelled and refunded"
            : "order status filtered";
        return Subtitle(q, dateFieldLabel) + "  •  " + basis;
    }

    /// <summary>
    /// Applies paging to an already-sorted, already-counted sequence. A <see cref="ReportQuery"/>
    /// with a non-positive page size is unpaged, which is how export runs.
    /// </summary>
    public static IQueryable<T> Page<T>(IQueryable<T> q, ReportQuery query) =>
        query.IsPaged ? q.Skip(query.Skip).Take(query.PageSize) : q;

    /// <summary>In-memory counterpart, for the reports whose final shape is assembled client-side
    /// after the database has done the filtering it can.</summary>
    public static IEnumerable<T> Page<T>(IEnumerable<T> q, ReportQuery query) =>
        query.IsPaged ? q.Skip(query.Skip).Take(query.PageSize) : q;

    /// <summary>
    /// Orders by one of a set of named keys, falling back to a default when the caller supplies a
    /// key the report does not know. Sorting comes from a hand-written switch in each builder
    /// rather than reflection over property names — a URL cannot then name a column that was never
    /// meant to be sortable, and every sort stays translatable to SQL.
    /// </summary>
    public static IOrderedQueryable<T> Order<T, TKey>(
        IQueryable<T> q, System.Linq.Expressions.Expression<Func<T, TKey>> key, bool desc) =>
        desc ? q.OrderByDescending(key) : q.OrderBy(key);

    public static IOrderedEnumerable<T> Order<T, TKey>(
        IEnumerable<T> q, Func<T, TKey> key, bool desc) =>
        desc ? q.OrderByDescending(key) : q.OrderBy(key);
}

/// <summary>
/// One report. Registered in DI as a set; <c>ReportingService</c> picks the builder whose
/// <see cref="Type"/> matches the request. Adding a ninth report means adding one class and one DI
/// line — no change to the page, the filter bar, the pager or either exporter.
/// </summary>
internal interface IReportBuilder
{
    ReportType Type { get; }
    Task<ReportTable> BuildAsync(ReportQuery query, CancellationToken ct);
}
