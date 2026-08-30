using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// One flattened order line with everything the line-level reports need attached — product,
/// subject, faculty, franchisee, and the order's own money and status.
///
/// <para>Money here is the APPORTIONED share of the order's figures, not the raw
/// <c>OrderItem</c> columns. See <see cref="OrderMoney.Apportion"/>: order-level money (coupon,
/// shipping, checkout add-ons, franchisee commission) has no natural line, so it is divided
/// pro-rata. That is what makes a line-level report sum back to the order it came from, which §28
/// requires.</para>
/// </summary>
internal sealed class OrderLine
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public string? SourceNo { get; init; }
    public DateTime OrderDate { get; init; }
    public string StudentName { get; init; } = "";
    public string StudentPhone { get; init; } = "";
    public string? StudentEmail { get; init; }

    public Guid ProductId { get; init; }
    public string ProductTitle { get; init; } = "";
    public Guid? SubjectId { get; init; }
    public string? SubjectName { get; init; }
    public string? ModeName { get; init; }

    public string? FacultyNames { get; init; }
    public Guid? FranchiseId { get; init; }
    public string? FranchiseName { get; init; }

    public int Quantity { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public PaymentMode? PaymentMode { get; init; }
    public OrderSource Source { get; init; }

    public OrderMoney.Figures Money { get; init; }
}

/// <summary>
/// Builds the flattened, filtered order-line set that the Sales, Franchisee-Wise and
/// Product/Subject reports all read.
///
/// <para>Written once rather than three times so the three reports cannot disagree about what
/// counts as a sale, how a multi-select is applied, or how order money divides across lines
/// (§29, §36.15).</para>
/// </summary>
internal sealed class OrderLineSource
{
    private readonly RioCommerceDbContext _db;
    public OrderLineSource(RioCommerceDbContext db) => _db = db;

    /// <summary>
    /// Applies every order-scoped filter in the query and returns the surviving lines.
    ///
    /// <para><b>The line filters narrow which lines are REPORTED, not which lines the apportionment
    /// is computed over.</b> That distinction is the whole reason this runs in two phases. Filter to
    /// one product on an order holding two, and if the order's money were divided across only the
    /// surviving line, that single line would inherit the entire order total — a ₹1,000 product
    /// would report ₹4,000 of revenue. So the query first identifies the matching ORDERS, loads all
    /// of their lines to divide the money correctly, and only then drops the lines the user did not
    /// ask for.</para>
    ///
    /// <para>Both phases filter in SQL. The apportionment between them is per-order arithmetic that
    /// SQL cannot express, so only the already-narrowed rows are materialised.</para>
    /// </summary>
    public async Task<List<OrderLine>> LoadAsync(ReportQuery q, CancellationToken ct)
    {
        var (fromUtc, toUtc) = ReportSupport.ResolveRange(q);

        // Wallet top-ups are money moving into a franchisee's account, not a sale — including them
        // would count the same rupees twice, once here and again when the wallet buys a course.
        var orders = _db.Orders.AsNoTracking().ExcludeWalletTopUps()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc);

        // A franchise-portal caller is pinned to their own franchise; the admin multi-select only
        // applies when no scope was forced.
        if (q.FranchiseScopeId is { } scope) orders = orders.Where(o => o.FranchiseId == scope);
        else if (q.FranchiseIds.Count > 0) orders = orders.Where(o => o.FranchiseId != null && q.FranchiseIds.Contains(o.FranchiseId.Value));

        // Draft, cancelled and refunded orders are excluded unless the user asks for them by name.
        // Without this a cancelled ₹50,000 order counts as revenue here while every other figure in
        // the admin leaves it out.
        orders = ReportSupport.ApplyRevenueStatus(orders, q);

        if (q.PaymentStatuses.Count > 0) orders = orders.Where(o => q.PaymentStatuses.Contains(o.PaymentStatus));
        if (q.PaymentModes.Count > 0) orders = orders.Where(o => o.PaymentMode != null && q.PaymentModes.Contains(o.PaymentMode.Value));
        if (q.Sources.Count > 0) orders = orders.Where(o => q.Sources.Contains(o.Source));

        var term = ReportSupport.Term(q.Search);

        // ── Phase 1: keep only orders that contain at least one matching line ──
        // These are EXISTS predicates on the order, so they narrow the read without deciding which
        // lines come back.
        if (q.ProductIds.Count > 0)
            orders = orders.Where(o => o.Items.Any(i => q.ProductIds.Contains(i.ProductId)));
        // Subject goes through ProductSubject for the same reason faculty does below: a combo
        // course covers several subjects and must be found by all of them. This decides which LINES
        // survive, never what they are worth — every line still carries its own apportioned share
        // exactly once, and the SubjectId reported on it stays the product's primary.
        if (q.SubjectIds.Count > 0)
            orders = orders.Where(o => o.Items.Any(i => _db.ProductSubjects
                    .Any(ps => ps.ProductId == i.ProductId && q.SubjectIds.Contains(ps.SubjectId))
                || (i.Product.SubjectId != null && q.SubjectIds.Contains(i.Product.SubjectId.Value))));
        // Faculty goes through ProductFaculty — the real many-to-many — not Product.PrimaryFaculty,
        // so a course taught by three faculty is found by all three.
        if (q.FacultyIds.Count > 0)
            orders = orders.Where(o => o.Items.Any(i => _db.ProductFaculty
                .Any(pf => pf.ProductId == i.ProductId && q.FacultyIds.Contains(pf.FacultyId))));

        if (term is not null)
        {
            var like = $"%{term}%";
            orders = orders.Where(o =>
                EF.Functions.ILike(o.OrderNumber, like)
                || EF.Functions.ILike(o.StudentName, like)
                || EF.Functions.ILike(o.StudentPhone, like)
                || (o.StudentEmail != null && EF.Functions.ILike(o.StudentEmail, like))
                || (o.SourceNo != null && EF.Functions.ILike(o.SourceNo, like))
                || o.Items.Any(i => EF.Functions.ILike(i.ProductTitle, like)));
        }

        // Every line of every surviving order — the apportionment needs the order's full gross.
        var lines = from oi in _db.OrderItems.AsNoTracking()
                    join o in orders on oi.OrderId equals o.Id
                    select new { Item = oi, Order = o };

        var raw = await lines
            .Select(x => new
            {
                x.Order.Id,
                x.Order.OrderNumber,
                x.Order.SourceNo,
                x.Order.CreatedAt,
                x.Order.StudentName,
                x.Order.StudentPhone,
                x.Order.StudentEmail,
                x.Order.Status,
                x.Order.PaymentStatus,
                x.Order.PaymentMode,
                x.Order.Source,
                x.Order.FranchiseId,
                FranchiseName = x.Order.Franchise != null ? x.Order.Franchise.Name : null,
                x.Order.TotalAmount,
                x.Order.DiscountAmount,
                x.Order.GstAmount,

                ItemId = x.Item.Id,
                x.Item.ProductId,
                x.Item.ProductTitle,
                x.Item.ModeName,
                x.Item.Quantity,
                x.Item.UnitPrice,
                x.Item.Discount,
                SubjectId = x.Item.Product.SubjectId,
                SubjectName = x.Item.Product.Subject != null ? x.Item.Product.Subject.Name : null
            })
            .ToListAsync(ct);

        if (raw.Count == 0) return new List<OrderLine>();

        // The franchisee's commission is order-level and lives in its own ledger, so it is fetched
        // once for the orders in scope rather than joined per line.
        var orderIds = raw.Select(r => r.Id).Distinct().ToList();
        var commissions = await _db.FranchiseCommissionEntries.AsNoTracking()
            .Where(e => orderIds.Contains(e.OrderId))
            .GroupBy(e => e.OrderId)
            .Select(g => new { OrderId = g.Key, Amount = g.Sum(x => x.CommissionAmount) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Amount, ct);

        // Which faculty teach the products in play — resolved once for the whole result rather than
        // per line, so a 500-line report is one extra query instead of 500.
        var productIds = raw.Select(r => r.ProductId).Distinct().ToList();
        var facultyByProduct = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .OrderByDescending(pf => pf.IsPrimary)
            .Select(pf => new { pf.ProductId, pf.FacultyId, Name = pf.Faculty.DisplayName })
            .ToListAsync(ct);

        var facultyNames = facultyByProduct
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name)));
        var facultyIdsByProduct = facultyByProduct
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToHashSet());

        // Same prefetch for subjects, so phase 2 can match a combo on any subject it covers without
        // a query per line.
        var subjectIdsByProduct = (await _db.ProductSubjects.AsNoTracking()
                .Where(ps => productIds.Contains(ps.ProductId))
                .Select(ps => new { ps.ProductId, ps.SubjectId })
                .ToListAsync(ct))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SubjectId).ToHashSet());

        var result = new List<OrderLine>(raw.Count);

        // Apportion per order across its FULL line set, so each line's share is measured against
        // the order's real gross. Lines the user filtered out are dropped afterwards, in phase 2.
        foreach (var group in raw.GroupBy(r => r.Id))
        {
            var items = group.ToList();
            var grosses = items.Select(i => OrderMoney.LineGross(i.UnitPrice, i.Quantity)).ToList();

            var first = items[0];
            commissions.TryGetValue(first.Id, out var commission);

            var orderFigures = OrderMoney.ForOrder(
                grossSum: grosses.Sum(),
                orderDiscount: first.DiscountAmount,
                itemDiscountSum: items.Sum(i => i.Discount * i.Quantity),
                commission: commission,
                source: first.Source,
                totalAmount: first.TotalAmount,
                storedGst: first.GstAmount);

            var split = OrderMoney.Apportion(grosses, orderFigures);

            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];

                // ── Phase 2: drop the lines the user filtered out ──
                // Done here, AFTER the split, so the surviving lines carry the share they were
                // actually worth rather than absorbing the whole order.
                if (q.ProductIds.Count > 0 && !q.ProductIds.Contains(it.ProductId)) continue;
                if (q.SubjectIds.Count > 0)
                {
                    var covers = subjectIdsByProduct.TryGetValue(it.ProductId, out var mapped)
                        ? new HashSet<Guid>(mapped) : new HashSet<Guid>();
                    if (it.SubjectId is { } primarySubject) covers.Add(primarySubject);
                    if (!q.SubjectIds.Any(covers.Contains)) continue;
                }
                if (q.FacultyIds.Count > 0
                    && (!facultyIdsByProduct.TryGetValue(it.ProductId, out var taughtBy)
                        || !q.FacultyIds.Any(taughtBy.Contains))) continue;

                result.Add(new OrderLine
                {
                    OrderId = it.Id,
                    OrderNumber = it.OrderNumber,
                    SourceNo = it.SourceNo,
                    OrderDate = it.CreatedAt,
                    StudentName = it.StudentName,
                    StudentPhone = it.StudentPhone,
                    StudentEmail = it.StudentEmail,
                    ProductId = it.ProductId,
                    ProductTitle = it.ProductTitle,
                    SubjectId = it.SubjectId,
                    SubjectName = it.SubjectName,
                    ModeName = it.ModeName,
                    FacultyNames = facultyNames.TryGetValue(it.ProductId, out var names) ? names : null,
                    FranchiseId = it.FranchiseId,
                    FranchiseName = it.FranchiseName,
                    Quantity = it.Quantity,
                    Status = it.Status,
                    PaymentStatus = it.PaymentStatus,
                    PaymentMode = it.PaymentMode,
                    Source = it.Source,
                    Money = split[i]
                });
            }
        }

        return result;
    }
}
