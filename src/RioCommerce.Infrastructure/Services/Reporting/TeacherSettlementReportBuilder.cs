using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §15 — Teacher Settlement Report. One row per (faculty, product), so the calculation chain the
/// section describes reads straight across:
///
/// <para>Product/Course Sales → Teacher Share / Rate → Total Payable → Amount Paid → Balance
/// Payable.</para>
///
/// <para><b>The report shows earnings whether or not a settlement has been generated for them.</b>
/// It first read only the <c>teacher_settlements</c> tables, which meant it was blank until someone
/// remembered to go and generate a period — a report that answers "what do I owe my teachers" is
/// useless if it says nothing until you have already worked that out. So it merges two sources:</para>
///
/// <list type="bullet">
///   <item><b>Settled</b> — lines from a generated settlement. Payable is the figure snapshotted at
///   generation, along with what has been paid against it and the balance outstanding.</item>
///   <item><b>Unsettled</b> — earnings in the window from the <c>FacultyShareEntries</c> ledger that
///   no settlement covers yet. Payable is the share plus its GST; nothing is paid, so the whole
///   amount is outstanding.</item>
/// </list>
///
/// <para>Both sides use the share and GST exactly as the ledger recorded them at the time of each
/// order — the same figures the Faculty-Wise report and the payout run use (§26, §36.14/15). An
/// unsettled row becomes a settled one the moment its period is generated, with the same numbers.</para>
/// </summary>
internal sealed class TeacherSettlementReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.TeacherSettlement;

    private const string UnsettledStatus = "Unsettled";

    private readonly RioCommerceDbContext _db;
    public TeacherSettlementReportBuilder(RioCommerceDbContext db) => _db = db;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Faculty",            ReportColumnKind.Text,    "faculty",  1.8f),
        new("Settlement",         ReportColumnKind.Text,    "number",   1.5f),
        new("Settlement Period",  ReportColumnKind.Text,    "period",   1.9f),
        new("Product/Course",     ReportColumnKind.Text,    "product",  2.2f),
        new("Subject",            ReportColumnKind.Text,    "subject",  1.3f),
        new("Qty",                ReportColumnKind.Integer, "qty",      0.6f),
        new("Total Sales (₹)",    ReportColumnKind.Money,   "sales",    1.3f),
        new("Share / Rate",       ReportColumnKind.Text,    "rate",     1.1f),
        new("Teacher Share (₹)",  ReportColumnKind.Money,   "share",    1.3f),
        new("GST on Share (₹)",   ReportColumnKind.Money,   "sharegst", 1.3f),
        new("Line Payout (₹)",    ReportColumnKind.Money,   "payout",   1.3f),
        new("Total Payable (₹)",  ReportColumnKind.Money,   "payable",  1.3f),
        new("Amount Paid (₹)",    ReportColumnKind.Money,   "paid",     1.3f),
        new("Balance (₹)",        ReportColumnKind.Money,   "balance",  1.2f),
        new("Status",             ReportColumnKind.Status,  "status",   1.1f)
    };

    /// <param name="SettlementId">Null on an unsettled row — there is no settlement record yet.</param>
    /// <param name="Payable">Settlement-level on a settled row (so it repeats across that
    /// settlement's product lines); per-row on an unsettled one.</param>
    private sealed record Row(
        Guid? SettlementId, string Faculty, string Number, DateTime PeriodStart, DateTime PeriodEnd,
        string Product, string Subject, int Qty, decimal Sales, string Rate,
        decimal Share, decimal ShareGst, decimal Payout,
        decimal Payable, decimal Paid, decimal Balance, string Status);

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var (fromUtc, toUtc) = ReportSupport.ResolveRange(q);

        var rows = new List<Row>();
        rows.AddRange(await SettledAsync(q, fromUtc, toUtc, ct));

        // A status filter names settlement states, none of which an unsettled earning is in, so
        // asking for one means "settled rows only".
        var includeUnsettled = q.SettlementStatuses.Count == 0;
        if (includeUnsettled)
            rows.AddRange(await UnsettledAsync(q, fromUtc, toUtc, ct));

        // Payable / Paid / Balance are settlement-level on settled rows and repeat across that
        // settlement's product lines, so summing the column would multiply each settlement by its
        // line count. De-duplicate those on settlement; unsettled rows are per-row and do sum.
        var settledOnce = rows.Where(r => r.SettlementId is not null)
            .GroupBy(r => r.SettlementId!.Value).Select(g => g.First()).ToList();
        var unsettled = rows.Where(r => r.SettlementId is null).ToList();

        var payable = settledOnce.Sum(r => r.Payable) + unsettled.Sum(r => r.Payable);
        var paid = settledOnce.Sum(r => r.Paid);
        var balance = settledOnce.Sum(r => r.Balance) + unsettled.Sum(r => r.Balance);

        var totals = new ReportTotals
        {
            TotalQuantity = rows.Sum(r => r.Qty),
            TotalSales = rows.Sum(r => r.Sales),
            TotalPayable = payable,
            AmountPaid = paid,
            BalancePayable = balance
        };
        totals.Extra.Add(("Settlements", settledOnce.Count.ToString()));
        totals.Extra.Add(("Faculty covered", rows.Select(r => r.Faculty).Distinct().Count().ToString()));
        totals.Extra.Add(("Teacher share (cost)", $"₹{rows.Sum(r => r.Share):N2}"));
        totals.Extra.Add(("GST on share (ITC)", $"₹{rows.Sum(r => r.ShareGst):N2}"));

        // Outstanding earnings with no settlement behind them are the actionable number on this
        // page — they are what still needs generating and paying.
        if (unsettled.Count > 0)
            totals.Extra.Add(("⚠ Not yet settled",
                $"₹{unsettled.Sum(r => r.Payable):N2} across {unsettled.Count} " +
                $"{(unsettled.Count == 1 ? "line" : "lines")} — generate a settlement period to record payment against it."));

        var sorted = Sort(rows, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "teacher-settlement",
            Title = "Teacher Settlement Report",
            Subtitle = ReportSupport.Subtitle(q, "settlement period")
                + (includeUnsettled ? "  •  includes earnings not yet settled" : "  •  settled records only"),
            EmptyMessage = "No faculty earnings or settlements in this period. "
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
                r.Number,
                Period(r.PeriodStart, r.PeriodEnd),
                r.Product,
                r.Subject,
                ReportSupport.Int(r.Qty),
                ReportSupport.Money(r.Sales),
                r.Rate,
                ReportSupport.Money(r.Share),
                ReportSupport.Money(r.ShareGst),
                ReportSupport.Money(r.Payout),
                ReportSupport.Money(r.Payable),
                ReportSupport.Money(r.Paid),
                ReportSupport.Money(r.Balance),
                r.Status
            }).ToList()
        };
    }

    // ─────────────── settled ───────────────

    /// <summary>Lines from generated settlements whose period overlaps the window. Overlap rather
    /// than containment: a quarterly settlement must still appear under a one-month filter.</summary>
    private async Task<List<Row>> SettledAsync(ReportQuery q, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var settlements = _db.TeacherSettlements.AsNoTracking()
            .Where(s => s.PeriodStartUtc < toUtc && s.PeriodEndUtc > fromUtc);

        if (q.FacultyIds.Count > 0) settlements = settlements.Where(s => q.FacultyIds.Contains(s.FacultyId));
        if (q.SettlementStatuses.Count > 0) settlements = settlements.Where(s => q.SettlementStatuses.Contains(s.Status));

        var lines = from s in settlements
                    join i in _db.TeacherSettlementItems.AsNoTracking() on s.Id equals i.SettlementId
                    select new { S = s, I = i };

        if (q.ProductIds.Count > 0) lines = lines.Where(x => q.ProductIds.Contains(x.I.ProductId));
        if (q.SubjectIds.Count > 0)
            lines = lines.Where(x => x.I.SubjectId != null && q.SubjectIds.Contains(x.I.SubjectId.Value));

        if (ReportSupport.Term(q.Search) is { } term)
        {
            var like = $"%{term}%";
            lines = lines.Where(x =>
                EF.Functions.ILike(x.S.FacultyName, like)
                || EF.Functions.ILike(x.S.SettlementNumber, like)
                || EF.Functions.ILike(x.I.ProductTitle, like));
        }

        return await lines.Select(x => new Row(
            x.S.Id, x.S.FacultyName, x.S.SettlementNumber, x.S.PeriodStartUtc, x.S.PeriodEndUtc,
            x.I.ProductTitle, x.I.SubjectName ?? "—", x.I.Quantity, x.I.GrossSales,
            x.I.ShareType == SharingType.Percentage
                ? x.I.ShareValue.ToString("0.##") + "%"
                : "₹" + x.I.ShareValue.ToString("0.##") + "/unit",
            x.I.ShareAmount, x.I.GstOnShare, x.I.TotalPayout,
            x.S.TotalPayable, x.S.AmountPaid, x.S.BalancePayable,
            x.S.Status == TeacherSettlementStatus.PartiallyPaid ? "Partially Paid" : x.S.Status.ToString()
        )).ToListAsync(ct);
    }

    // ─────────────── unsettled ───────────────

    /// <summary>
    /// Earnings in the window that no settlement covers, straight from the ledger — what is owed
    /// but not yet recorded as a settlement.
    ///
    /// <para>An entry is "covered" when a non-cancelled settlement exists for that faculty whose
    /// period contains the moment it was earned. Cancelled settlements do not cover anything: the
    /// money is owed again.</para>
    /// </summary>
    private async Task<List<Row>> UnsettledAsync(ReportQuery q, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Cancelled and refunded orders must not create a payable — same revenue basis as every
        // other figure in the admin.
        var orders = ReportSupport.ApplyRevenueStatus(
            _db.Orders.AsNoTracking().ExcludeWalletTopUps(), q);

        if (q.FranchiseScopeId is { } scope) orders = orders.Where(o => o.FranchiseId == scope);
        else if (q.FranchiseIds.Count > 0)
            orders = orders.Where(o => o.FranchiseId != null && q.FranchiseIds.Contains(o.FranchiseId.Value));

        var entries = from e in _db.FacultyShareEntries.AsNoTracking()
                      join oi in _db.OrderItems.AsNoTracking() on e.OrderItemId equals oi.Id
                      join o in orders on e.OrderId equals o.Id
                      where e.EarnedAt >= fromUtc && e.EarnedAt < toUtc
                      // Not already inside a live settlement for this faculty.
                      where !_db.TeacherSettlements.Any(s =>
                          s.FacultyId == e.FacultyId
                          && s.Status != TeacherSettlementStatus.Cancelled
                          && s.PeriodStartUtc <= e.EarnedAt
                          && s.PeriodEndUtc > e.EarnedAt)
                      select new { Entry = e, Item = oi };

        if (q.FacultyIds.Count > 0) entries = entries.Where(x => q.FacultyIds.Contains(x.Entry.FacultyId));
        if (q.ProductIds.Count > 0) entries = entries.Where(x => q.ProductIds.Contains(x.Entry.ProductId));
        if (q.SubjectIds.Count > 0)
            entries = entries.Where(x => x.Item.Product.SubjectId != null && q.SubjectIds.Contains(x.Item.Product.SubjectId.Value));

        if (ReportSupport.Term(q.Search) is { } term)
        {
            var like = $"%{term}%";
            entries = entries.Where(x =>
                EF.Functions.ILike(x.Entry.Faculty.DisplayName, like)
                || EF.Functions.ILike(x.Entry.ProductTitle, like)
                || EF.Functions.ILike(x.Entry.OrderNumber, like));
        }

        var raw = await entries.Select(x => new
        {
            x.Entry.FacultyId,
            FacultyName = x.Entry.Faculty.DisplayName,
            x.Entry.ProductId,
            x.Entry.ProductTitle,
            SubjectName = x.Item.Product.Subject != null ? x.Item.Product.Subject.Name : null,
            x.Item.Quantity,
            x.Item.LineTotal,
            x.Entry.ShareType,
            x.Entry.ShareValue,
            x.Entry.ShareAmount,
            x.Entry.GstOnShare,
            x.Entry.TotalPayout
        }).ToListAsync(ct);

        // Rolled up to the same grain as a settled row — one line per (faculty, product) — so the
        // two sources read as one table rather than two shapes stacked on each other.
        return raw
            .GroupBy(r => new { r.FacultyId, r.FacultyName, r.ProductId, r.ProductTitle, r.SubjectName })
            .Select(g =>
            {
                var last = g.Last();
                var payout = g.Sum(x => x.TotalPayout);
                return new Row(
                    null,
                    g.Key.FacultyName,
                    "— not generated",
                    fromUtc, toUtc,
                    g.Key.ProductTitle,
                    g.Key.SubjectName ?? "—",
                    g.Sum(x => x.Quantity),
                    Math.Round(g.Sum(x => x.LineTotal), 2),
                    last.ShareType == SharingType.Percentage
                        ? $"{last.ShareValue:0.##}%"
                        : $"₹{last.ShareValue:0.##}/unit",
                    g.Sum(x => x.ShareAmount),
                    g.Sum(x => x.GstOnShare),
                    payout,
                    // Nothing has been paid against an unsettled earning, so the whole payout is
                    // both the payable and the outstanding balance.
                    payout, 0m, payout,
                    UnsettledStatus);
            })
            .ToList();
    }

    /// <summary>The period end is exclusive, so the displayed end date is the last day actually
    /// covered — otherwise a calendar month reads as ending on the 1st of the next one.</summary>
    private static string Period(DateTime start, DateTime end) =>
        $"{ReportSupport.IstDate(start)} – {ReportSupport.IstDate(end.AddTicks(-1))}";

    private static IEnumerable<Row> Sort(List<Row> rows, ReportQuery q) => q.SortBy switch
    {
        "faculty" => ReportSupport.Order(rows, r => r.Faculty, q.SortDesc),
        "number" => ReportSupport.Order(rows, r => r.Number, q.SortDesc),
        "period" => ReportSupport.Order(rows, r => r.PeriodStart, q.SortDesc),
        "product" => ReportSupport.Order(rows, r => r.Product, q.SortDesc),
        "subject" => ReportSupport.Order(rows, r => r.Subject, q.SortDesc),
        "qty" => ReportSupport.Order(rows, r => r.Qty, q.SortDesc),
        "sales" => ReportSupport.Order(rows, r => r.Sales, q.SortDesc),
        "share" => ReportSupport.Order(rows, r => r.Share, q.SortDesc),
        "sharegst" => ReportSupport.Order(rows, r => r.ShareGst, q.SortDesc),
        "payout" => ReportSupport.Order(rows, r => r.Payout, q.SortDesc),
        "payable" => ReportSupport.Order(rows, r => r.Payable, q.SortDesc),
        "paid" => ReportSupport.Order(rows, r => r.Paid, q.SortDesc),
        "balance" => ReportSupport.Order(rows, r => r.Balance, q.SortDesc),
        "status" => ReportSupport.Order(rows, r => r.Status, q.SortDesc),
        // Unsettled first — it is the actionable half of the report — then by faculty and course.
        _ => rows.OrderByDescending(r => r.SettlementId is null)
                 .ThenBy(r => r.Faculty)
                 .ThenBy(r => r.Product)
    };
}
