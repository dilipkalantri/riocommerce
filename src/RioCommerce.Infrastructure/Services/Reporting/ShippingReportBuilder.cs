using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// §14 — Shipping Report. One row per dispatched product line, for tracking the physical books
/// that go out to students.
///
/// <para>Dated on DISPATCH date, not order date (§25) — with the fallback that a shipment not yet
/// dispatched is dated by when its record was created, so pending consignments still appear in a
/// current window instead of vanishing until someone dispatches them.</para>
/// </summary>
internal sealed class ShippingReportBuilder : IReportBuilder
{
    public ReportType Type => ReportType.Shipping;

    private readonly RioCommerceDbContext _db;
    public ShippingReportBuilder(RioCommerceDbContext db) => _db = db;

    private static readonly List<ReportColumn> Cols = new()
    {
        new("Order ID",         ReportColumnKind.Text,    "order",    1.4f),
        new("Student",          ReportColumnKind.Text,    "student",  1.8f),
        new("Contact",          ReportColumnKind.Text,    "contact",  1.3f),
        new("Shipping Address", ReportColumnKind.Text,    "city",     3f),
        new("Product/Books",    ReportColumnKind.Text,    "product",  2.2f),
        new("Qty",              ReportColumnKind.Integer, "qty",      0.6f),
        new("Dispatch Date",    ReportColumnKind.Date,    "dispatch", 1.2f),
        new("Courier Partner",  ReportColumnKind.Text,    "courier",  1.3f),
        new("AWB / Tracking",   ReportColumnKind.Text,    "awb",      1.5f),
        new("Shipping Status",  ReportColumnKind.Status,  "status",   1.1f),
        new("Franchisee",       ReportColumnKind.Text,    "franchise",1.3f)
    };

    private sealed record Row(
        string OrderNumber, string Student, string Contact, string Address, string Product,
        int Qty, DateTime? Dispatch, string Courier, string Awb, ShipmentStatus Status,
        string Franchise, DateTime SortDate);

    public async Task<ReportTable> BuildAsync(ReportQuery q, CancellationToken ct)
    {
        var (fromUtc, toUtc) = ReportSupport.ResolveRange(q);

        var ship = _db.Shipments.AsNoTracking()
            // Undispatched consignments fall back to their creation date so the pending queue is
            // visible in a normal date window rather than only after dispatch.
            .Where(s => (s.DispatchedAt ?? s.CreatedAt) >= fromUtc
                        && (s.DispatchedAt ?? s.CreatedAt) < toUtc);

        if (q.ShippingStatuses.Count > 0) ship = ship.Where(s => q.ShippingStatuses.Contains(s.Status));
        if (!string.IsNullOrWhiteSpace(q.Courier)) ship = ship.Where(s => s.Courier == q.Courier);

        if (q.FranchiseScopeId is { } scope) ship = ship.Where(s => s.Order.FranchiseId == scope);
        else if (q.FranchiseIds.Count > 0)
            ship = ship.Where(s => s.Order.FranchiseId != null && q.FranchiseIds.Contains(s.Order.FranchiseId.Value));

        if (q.OrderStatuses.Count > 0) ship = ship.Where(s => q.OrderStatuses.Contains(s.Order.Status));

        // Line-level: a shipment with items yields one row per item. Shipments predating line-item
        // tracking are backfilled by migration 0028, so this join is the whole story.
        var lines = from s in ship
                    join si in _db.ShipmentItems.AsNoTracking() on s.Id equals si.ShipmentId
                    select new { S = s, I = si };

        if (q.ProductIds.Count > 0) lines = lines.Where(x => q.ProductIds.Contains(x.I.ProductId));
        if (q.SubjectIds.Count > 0)
            lines = lines.Where(x => _db.Products.Any(p => p.Id == x.I.ProductId
                && (_db.ProductSubjects.Any(ps => ps.ProductId == p.Id && q.SubjectIds.Contains(ps.SubjectId))
                    || (p.SubjectId != null && q.SubjectIds.Contains(p.SubjectId.Value)))));
        if (q.FacultyIds.Count > 0)
            lines = lines.Where(x => _db.ProductFaculty.Any(pf => pf.ProductId == x.I.ProductId
                && q.FacultyIds.Contains(pf.FacultyId)));

        if (ReportSupport.Term(q.Search) is { } term)
        {
            var like = $"%{term}%";
            lines = lines.Where(x =>
                EF.Functions.ILike(x.S.Order.OrderNumber, like)
                || EF.Functions.ILike(x.S.Order.StudentName, like)
                || EF.Functions.ILike(x.S.Order.StudentPhone, like)
                || EF.Functions.ILike(x.I.ProductTitle, like)
                || (x.S.TrackingNumber != null && EF.Functions.ILike(x.S.TrackingNumber, like))
                || (x.S.Courier != null && EF.Functions.ILike(x.S.Courier, like)));
        }

        var raw = await lines.Select(x => new
        {
            x.S.Order.OrderNumber,
            x.S.Order.StudentName,
            x.S.Order.StudentPhone,
            x.S.Order.ShippingAddress,
            x.S.Order.ShippingCity,
            x.S.Order.ShippingState,
            x.S.Order.ShippingPincode,
            x.S.Order.BillingAddress,
            x.S.Order.BillingCity,
            x.S.Order.BillingState,
            x.S.Order.BillingPincode,
            x.S.Order.StudentCity,
            Franchise = x.S.Order.Franchise != null ? x.S.Order.Franchise.Name : null,
            x.I.ProductTitle,
            x.I.Quantity,
            x.S.DispatchedAt,
            x.S.CreatedAt,
            x.S.Courier,
            x.S.TrackingNumber,
            x.S.Status
        }).ToListAsync(ct);

        var rows = raw.Select(r => new Row(
            r.OrderNumber,
            r.StudentName,
            r.StudentPhone,
            // Ship-to falls back to billing, which is what the invoice renderer does when no
            // separate shipping address was captured.
            Address(r.ShippingAddress, r.ShippingCity, r.ShippingState, r.ShippingPincode)
                ?? Address(r.BillingAddress, r.BillingCity, r.BillingState, r.BillingPincode)
                ?? ReportSupport.Text(r.StudentCity),
            r.ProductTitle,
            r.Quantity,
            r.DispatchedAt,
            r.Courier ?? "—",
            r.TrackingNumber ?? "—",
            r.Status,
            r.Franchise ?? "—",
            r.DispatchedAt ?? r.CreatedAt)).ToList();

        var totals = new ReportTotals
        {
            TotalOrders = rows.Select(r => r.OrderNumber).Distinct().Count(),
            TotalQuantity = rows.Sum(r => r.Qty)
        };
        foreach (var g in rows.GroupBy(r => r.Status).OrderBy(g => g.Key))
            totals.Extra.Add(($"{g.Key}", g.Count().ToString()));

        var sorted = Sort(rows, q);
        var page = ReportSupport.Page(sorted, q).ToList();

        return new ReportTable
        {
            Type = Type,
            Slug = "shipping",
            Title = "Shipping Report",
            Subtitle = ReportSupport.Subtitle(q, "dispatch date"),
            // An empty shipping report almost always means nothing has been entered on the dispatch
            // board yet, not that the filters are too tight — a shipment record only exists once an
            // operator saves a courier/AWB or marks the order dispatched.
            EmptyMessage = "No consignments in this period. A shipment appears here once it is "
                + "entered on the dispatch board — assign a courier and AWB, or mark the order "
                + "dispatched, under Sales › Dispatch.",
            Columns = Cols,
            TotalRows = rows.Count,
            Page = q.Page,
            PageSize = q.PageSize,
            Totals = totals,
            Rows = page.Select(r => new[]
            {
                r.OrderNumber,
                r.Student,
                r.Contact,
                r.Address,
                r.Product,
                ReportSupport.Int(r.Qty),
                ReportSupport.IstDate(r.Dispatch),
                r.Courier,
                r.Awb,
                r.Status.ToString(),
                r.Franchise
            }).ToList()
        };
    }

    /// <summary>Joins the address parts, returning null when there is nothing worth printing so the
    /// caller can fall through to the next-best address.</summary>
    private static string? Address(string? line, string? city, string? state, string? pin)
    {
        var parts = new[] { line, city, state, pin }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        var joined = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static IEnumerable<Row> Sort(List<Row> rows, ReportQuery q) => q.SortBy switch
    {
        "order" => ReportSupport.Order(rows, r => r.OrderNumber, q.SortDesc),
        "student" => ReportSupport.Order(rows, r => r.Student, q.SortDesc),
        "contact" => ReportSupport.Order(rows, r => r.Contact, q.SortDesc),
        "city" => ReportSupport.Order(rows, r => r.Address, q.SortDesc),
        "product" => ReportSupport.Order(rows, r => r.Product, q.SortDesc),
        "qty" => ReportSupport.Order(rows, r => r.Qty, q.SortDesc),
        "courier" => ReportSupport.Order(rows, r => r.Courier, q.SortDesc),
        "awb" => ReportSupport.Order(rows, r => r.Awb, q.SortDesc),
        "status" => ReportSupport.Order(rows, r => r.Status, q.SortDesc),
        "franchise" => ReportSupport.Order(rows, r => r.Franchise, q.SortDesc),
        _ => ReportSupport.Order(rows, r => r.SortDate, q.SortDesc)
    };
}
