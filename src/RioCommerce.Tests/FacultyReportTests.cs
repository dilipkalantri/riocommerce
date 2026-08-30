using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// §10 Faculty-Wise Report — the arithmetic, not the plumbing.
///
/// <para>The report is driven by the <c>FacultyShareEntries</c> ledger: one row per
/// (order line × faculty) that actually earned. Two consequences drive almost every test here:</para>
///
/// <list type="bullet">
///   <item><b>A co-taught line appears once per faculty, showing the FULL course sale on each row.</b>
///         Adding the sales columns down the page would therefore multiply the revenue by the number
///         of teachers. The builder guards this by computing sales totals over DISTINCT
///         <c>OrderItemId</c> while summing the share columns across rows — the single most important
///         behaviour in the file, and previously untested.</item>
///   <item><b>Share, GST and payout are read from the ledger, never recomputed.</b> They were
///         snapshotted with the rule in force at the time of the order, so no filter and no
///         configuration change may alter them.</item>
/// </list>
///
/// <para>Subject is the newest way to reach a row — a combo now matches on any subject it covers —
/// so several tests below exist purely to prove that widening the FILTER cannot widen the MONEY.</para>
/// </summary>
public class FacultyReportTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"facultyreport-{Guid.NewGuid()}")
            .Options);

    private static FacultyReportBuilder Builder(RioCommerceDbContext db) => new(db);

    /// <summary>Mid-window so the default date range (and any explicit one below) contains it.</summary>
    private static readonly DateTime OrderedAt = DateTime.UtcNow.AddDays(-3);

    private static ReportQuery Query() => new() { Page = 1, PageSize = 100 };

    // ── Fixture builders ────────────────────────────────────────────────────────────────────────

    private static Faculty Fac(Guid id, string name) => new()
    { Id = id, DisplayName = name, IsActive = true };

    private static Subject Sub(Guid id, string name) => new()
    { Id = id, Name = name, Slug = $"s-{id:N}", IsActive = true };

    private static Product Prod(Guid id, string title, Guid? subjectId) => new()
    {
        Id = id, Title = title, Slug = $"p-{id:N}", SubjectId = subjectId,
        SellingPrice = 1180m, GstRate = 18m, Status = ProductStatus.Active
    };

    /// <summary>A revenue-bearing order. Status/PaymentStatus matter: the builder applies the same
    /// revenue-only rule as every other sales figure, so a cancelled order must not appear.</summary>
    private static Order Ord(Guid id, string number, string student, DateTime at) => new()
    {
        Id = id, OrderNumber = number, StudentName = student, StudentPhone = "9800000000",
        CreatedAt = at, Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
        Source = OrderSource.Website, TotalAmount = 1180m
    };

    private static OrderItem Item(Guid id, Guid orderId, Guid productId, string title, int qty,
                                  decimal unitPrice = 1180m, decimal discount = 0m) => new()
    {
        Id = id, OrderId = orderId, ProductId = productId, ProductTitle = title,
        Quantity = qty, UnitPrice = unitPrice, Discount = discount,
        LineTotal = (unitPrice - discount) * qty
    };

    /// <summary>
    /// A ledger row, written the way <c>FacultySharingService.RecordOrderFacultyShareAsync</c> writes
    /// one: the per-unit split already multiplied out to the line quantity. The tests never recompute
    /// it — that is the point. Defaults describe a 10% share for an unregistered faculty on a
    /// ₹1,180 @ 18% product (taxable base ₹1,000).
    /// </summary>
    private static FacultyShareEntry Entry(
        Guid facultyId, Order order, OrderItem item, Guid productId, string productTitle,
        decimal share, decimal gst = 0m, decimal shareValue = 10m,
        SharingType type = SharingType.Percentage) => new()
        {
            Id = Guid.NewGuid(),
            FacultyId = facultyId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OrderItemId = item.Id,
            ProductId = productId,
            ProductTitle = productTitle,
            BaseAmount = 1000m * item.Quantity,
            ShareType = type,
            ShareValue = shareValue,
            ShareAmount = share,
            GstOnShare = gst,
            TotalPayout = share + gst,
            WasGstRegistered = gst > 0m,
            EarnedAt = order.CreatedAt
        };

    private static ProductSubject Link(Guid productId, Guid subjectId, bool primary) => new()
    { Id = Guid.NewGuid(), ProductId = productId, SubjectId = subjectId, IsPrimary = primary };

    // ── Total readers ───────────────────────────────────────────────────────────────────────────
    // The four faculty-specific totals live in Totals.Extra as pre-formatted strings, so they are
    // read by label rather than by property.

    private static string Extra(ReportTable t, string label) =>
        t.Totals!.Extra.First(e => e.Label == label).Value;

    private static string TeacherShare(ReportTable t) => Extra(t, "Teacher share (cost)");
    private static string ShareGst(ReportTable t) => Extra(t, "GST on share (ITC)");
    private static string TotalPayout(ReportTable t) => Extra(t, "Total payout");
    private static string FacultyCovered(ReportTable t) => Extra(t, "Faculty covered");
    private static bool HasCoTaughtWarning(ReportTable t) => t.Totals!.Extra.Any(e => e.Label.Contains("Co-taught"));

    /// <summary>Column index by key, so a future column insertion cannot silently shift assertions.</summary>
    private static string Cell(ReportTable t, int rowIndex, string columnKey)
    {
        var col = t.Columns.ToList().FindIndex(c => c.Key == columnKey);
        Assert.True(col >= 0, $"No column with key '{columnKey}'.");
        return t.Rows[rowIndex][col];
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 1. One order + one faculty
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task One_order_one_faculty_reports_the_sale_and_the_share()
    {
        using var db = NewDb();
        Guid fac = Guid.NewGuid(), subj = Guid.NewGuid(), prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9001", "Asha", OrderedAt);
        var item = Item(Guid.NewGuid(), order.Id, prod, "Audit Regular", qty: 1);

        db.Faculty.Add(Fac(fac, "CA Harshad Jaju"));
        db.Subjects.Add(Sub(subj, "Audit"));
        db.Products.Add(Prod(prod, "Audit Regular", subj));
        db.ProductSubjects.Add(Link(prod, subj, true));
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        db.FacultyShareEntries.Add(Entry(fac, order, item, prod, "Audit Regular", share: 100m));
        await db.SaveChangesAsync();

        var t = await Builder(db).BuildAsync(Query(), default);

        Assert.Equal(1, t.TotalRows);
        Assert.Equal(1, t.Totals!.TotalOrders);
        Assert.Equal(1, t.Totals.TotalQuantity);
        Assert.Equal(1180m, t.Totals.GrossAmount);
        Assert.Equal(0m, t.Totals.Discount);
        Assert.Equal(1180m, t.Totals.TotalSales);
        Assert.Equal("₹100.00", TeacherShare(t));
        Assert.Equal("₹0.00", ShareGst(t));
        Assert.Equal("₹100.00", TotalPayout(t));
        Assert.Equal("1", FacultyCovered(t));
        Assert.False(HasCoTaughtWarning(t));

        Assert.Equal("CA Harshad Jaju", Cell(t, 0, "faculty"));
        Assert.Equal("Audit", Cell(t, 0, "subject"));
        Assert.Equal("10%", Cell(t, 0, "rate"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 2. One order item + two faculties — the double-count guard
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_co_taught_line_counts_its_sale_once_and_both_shares_separately()
    {
        using var db = NewDb();
        Guid f1 = Guid.NewGuid(), f2 = Guid.NewGuid(), subj = Guid.NewGuid(), prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9002", "Bhavesh", OrderedAt);
        var item = Item(Guid.NewGuid(), order.Id, prod, "Audit & Costing", qty: 1);

        db.Faculty.AddRange(Fac(f1, "Harshad"), Fac(f2, "Tejal"));
        db.Subjects.Add(Sub(subj, "Audit"));
        db.Products.Add(Prod(prod, "Audit & Costing", subj));
        db.ProductSubjects.Add(Link(prod, subj, true));
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        db.FacultyShareEntries.AddRange(
            Entry(f1, order, item, prod, "Audit & Costing", share: 100m),
            Entry(f2, order, item, prod, "Audit & Costing", share: 150m, shareValue: 15m));
        await db.SaveChangesAsync();

        var t = await Builder(db).BuildAsync(Query(), default);

        // Two rows — one per faculty — but ONE sale.
        Assert.Equal(2, t.TotalRows);
        Assert.Equal(1, t.Totals!.TotalOrders);
        Assert.Equal(1, t.Totals.TotalQuantity);          // NOT 2
        Assert.Equal(1180m, t.Totals.GrossAmount);        // NOT 2360
        Assert.Equal(1180m, t.Totals.TotalSales);         // NOT 2360

        // The shares are separate entitlements and DO add up.
        Assert.Equal("₹250.00", TeacherShare(t));
        Assert.Equal("₹250.00", TotalPayout(t));
        Assert.Equal("2", FacultyCovered(t));

        // And the reader is told the sales column cannot be summed down the page.
        Assert.True(HasCoTaughtWarning(t));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 3. Several items in ONE order
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Several_lines_in_one_order_still_count_as_a_single_order()
    {
        using var db = NewDb();
        Guid fac = Guid.NewGuid(), subj = Guid.NewGuid(), p1 = Guid.NewGuid(), p2 = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9003", "Chirag", OrderedAt);
        var i1 = Item(Guid.NewGuid(), order.Id, p1, "Audit", qty: 1);
        var i2 = Item(Guid.NewGuid(), order.Id, p2, "Costing", qty: 1);

        db.Faculty.Add(Fac(fac, "Harshad"));
        db.Subjects.Add(Sub(subj, "Audit"));
        db.Products.AddRange(Prod(p1, "Audit", subj), Prod(p2, "Costing", subj));
        db.ProductSubjects.AddRange(Link(p1, subj, true), Link(p2, subj, true));
        db.Orders.Add(order);
        db.OrderItems.AddRange(i1, i2);
        db.FacultyShareEntries.AddRange(
            Entry(fac, order, i1, p1, "Audit", share: 100m),
            Entry(fac, order, i2, p2, "Costing", share: 100m));
        await db.SaveChangesAsync();

        var t = await Builder(db).BuildAsync(Query(), default);

        Assert.Equal(2, t.TotalRows);
        Assert.Equal(1, t.Totals!.TotalOrders);           // ← one order, two lines
        Assert.Equal(2, t.Totals.TotalQuantity);
        Assert.Equal(2360m, t.Totals.GrossAmount);        // two DISTINCT lines, so these do add
        Assert.Equal(2360m, t.Totals.TotalSales);
        Assert.Equal("₹200.00", TeacherShare(t));
        Assert.Equal("1", FacultyCovered(t));
        Assert.False(HasCoTaughtWarning(t));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 4. Quantity > 1 — multiplied exactly once
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Quantity_is_applied_once_to_sales_and_not_at_all_to_the_stored_share()
    {
        using var db = NewDb();
        Guid fac = Guid.NewGuid(), subj = Guid.NewGuid(), prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9004", "Divya", OrderedAt);
        // ₹1,100 list, ₹80 off per unit, three of them. UnitPrice is stored as the LIST price.
        var item = Item(Guid.NewGuid(), order.Id, prod, "Audit", qty: 3, unitPrice: 1100m, discount: 80m);

        db.Faculty.Add(Fac(fac, "Harshad"));
        db.Subjects.Add(Sub(subj, "Audit"));
        db.Products.Add(Prod(prod, "Audit", subj));
        db.ProductSubjects.Add(Link(prod, subj, true));
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        // The ledger already holds the per-unit share multiplied out: 100 × 3.
        db.FacultyShareEntries.Add(Entry(fac, order, item, prod, "Audit", share: 300m));
        await db.SaveChangesAsync();

        var t = await Builder(db).BuildAsync(Query(), default);

        Assert.Equal(3, t.Totals!.TotalQuantity);
        Assert.Equal(3300m, t.Totals.GrossAmount);        // 1100 × 3 — the discount is not added back
        Assert.Equal(240m, t.Totals.Discount);            // 80 × 3
        Assert.Equal(3060m, t.Totals.TotalSales);         // (1100 − 80) × 3
        // Gross − Discount now ties to Sales. Adding the discount back into gross broke this.
        Assert.Equal(t.Totals.TotalSales, t.Totals.GrossAmount - t.Totals.Discount);
        // Read straight off the ledger — the builder must not multiply by quantity a second time.
        Assert.Equal("₹300.00", TeacherShare(t));
        Assert.Equal("₹300.00", TotalPayout(t));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 5-8. Subject filtering
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A combo whose primary subject is Audit and which also covers Costing.</summary>
    private sealed record ComboFx(Guid Faculty, Guid Audit, Guid Costing, Guid Law, Guid Product, Guid ItemId);

    private static async Task<ComboFx> SeedComboAsync(RioCommerceDbContext db)
    {
        Guid fac = Guid.NewGuid(), audit = Guid.NewGuid(), costing = Guid.NewGuid(), law = Guid.NewGuid(),
             prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9005", "Esha", OrderedAt);
        var item = Item(Guid.NewGuid(), order.Id, prod, "Audit & Costing COMBO", qty: 1);

        db.Faculty.Add(Fac(fac, "Harshad"));
        db.Subjects.AddRange(Sub(audit, "Audit"), Sub(costing, "Costing"), Sub(law, "Law"));
        db.Products.Add(Prod(prod, "Audit & Costing COMBO", audit));      // primary = Audit
        db.ProductSubjects.AddRange(Link(prod, audit, true), Link(prod, costing, false));
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        db.FacultyShareEntries.Add(Entry(fac, order, item, prod, "Audit & Costing COMBO", share: 100m));
        await db.SaveChangesAsync();
        return new ComboFx(fac, audit, costing, law, prod, item.Id);
    }

    [Fact]
    public async Task Filtering_by_the_primary_subject_finds_the_combo()
    {
        using var db = NewDb();
        var fx = await SeedComboAsync(db);

        var q = Query(); q.SubjectIds.Add(fx.Audit);
        var t = await Builder(db).BuildAsync(q, default);

        Assert.Equal(1, t.TotalRows);
        Assert.Equal(1180m, t.Totals!.TotalSales);
    }

    [Fact]
    public async Task Filtering_by_the_secondary_subject_finds_the_same_combo()
    {
        using var db = NewDb();
        var fx = await SeedComboAsync(db);

        var q = Query(); q.SubjectIds.Add(fx.Costing);
        var t = await Builder(db).BuildAsync(q, default);

        Assert.Equal(1, t.TotalRows);
        Assert.Equal(1180m, t.Totals!.TotalSales);
        // The Subject COLUMN still reports the primary — one row can only name one subject.
        Assert.Equal("Audit", Cell(t, 0, "subject"));
    }

    [Fact]
    public async Task Selecting_both_of_a_combos_subjects_does_not_duplicate_its_line()
    {
        using var db = NewDb();
        var fx = await SeedComboAsync(db);

        var q = Query();
        q.SubjectIds.Add(fx.Audit);
        q.SubjectIds.Add(fx.Costing);
        var t = await Builder(db).BuildAsync(q, default);

        // The subject predicate is an EXISTS, not a join, so a product matching on two subjects is
        // still one row and one sale.
        Assert.Equal(1, t.TotalRows);
        Assert.Equal(1, t.Totals!.TotalOrders);
        Assert.Equal(1, t.Totals.TotalQuantity);
        Assert.Equal(1180m, t.Totals.GrossAmount);
        Assert.Equal(1180m, t.Totals.TotalSales);
        Assert.Equal("₹100.00", TeacherShare(t));
        Assert.Equal("₹100.00", TotalPayout(t));
    }

    [Fact]
    public async Task A_subject_the_product_does_not_cover_excludes_it()
    {
        using var db = NewDb();
        var fx = await SeedComboAsync(db);

        var q = Query(); q.SubjectIds.Add(fx.Law);
        var t = await Builder(db).BuildAsync(q, default);

        Assert.Equal(0, t.TotalRows);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 9. Multiple faculties AND multiple subjects on one line
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Two_faculties_on_a_two_subject_combo_still_count_one_sale()
    {
        using var db = NewDb();
        Guid f1 = Guid.NewGuid(), f2 = Guid.NewGuid(),
             audit = Guid.NewGuid(), costing = Guid.NewGuid(), prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9006", "Farhan", OrderedAt);
        var item = Item(Guid.NewGuid(), order.Id, prod, "Audit & Costing COMBO", qty: 2);

        db.Faculty.AddRange(Fac(f1, "Harshad"), Fac(f2, "Tejal"));
        db.Subjects.AddRange(Sub(audit, "Audit"), Sub(costing, "Costing"));
        db.Products.Add(Prod(prod, "Audit & Costing COMBO", audit));
        db.ProductSubjects.AddRange(Link(prod, audit, true), Link(prod, costing, false));
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        db.FacultyShareEntries.AddRange(
            Entry(f1, order, item, prod, "Audit & Costing COMBO", share: 200m),
            Entry(f2, order, item, prod, "Audit & Costing COMBO", share: 300m, gst: 54m, shareValue: 15m));
        await db.SaveChangesAsync();

        var q = Query();
        q.SubjectIds.Add(audit);
        q.SubjectIds.Add(costing);
        var t = await Builder(db).BuildAsync(q, default);

        // Worst case for double-counting: 2 faculty × 2 subjects × qty 2. Still ONE sale of two units.
        Assert.Equal(2, t.TotalRows);
        Assert.Equal(1, t.Totals!.TotalOrders);
        Assert.Equal(2, t.Totals.TotalQuantity);
        Assert.Equal(2360m, t.Totals.GrossAmount);
        Assert.Equal(2360m, t.Totals.TotalSales);

        Assert.Equal("₹500.00", TeacherShare(t));
        Assert.Equal("₹54.00", ShareGst(t));
        Assert.Equal("₹554.00", TotalPayout(t));
        Assert.Equal("2", FacultyCovered(t));
        Assert.True(HasCoTaughtWarning(t));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 10. A filter changes WHICH rows appear, never what they are worth
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task The_stored_share_gst_and_payout_are_identical_under_every_subject_filter()
    {
        using var db = NewDb();
        var fx = await SeedComboAsync(db);

        var unfiltered = await Builder(db).BuildAsync(Query(), default);

        var byPrimary = Query(); byPrimary.SubjectIds.Add(fx.Audit);
        var primary = await Builder(db).BuildAsync(byPrimary, default);

        var bySecondary = Query(); bySecondary.SubjectIds.Add(fx.Costing);
        var secondary = await Builder(db).BuildAsync(bySecondary, default);

        foreach (var t in new[] { unfiltered, primary, secondary })
        {
            Assert.Equal("₹100.00", TeacherShare(t));
            Assert.Equal("₹0.00", ShareGst(t));
            Assert.Equal("₹100.00", TotalPayout(t));
            Assert.Equal(1180m, t.Totals!.TotalSales);
            Assert.Equal("10%", Cell(t, 0, "rate"));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 11. No sharing rule → no ledger row → no report row
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_course_that_earned_no_share_produces_no_row()
    {
        using var db = NewDb();
        Guid fac = Guid.NewGuid(), subj = Guid.NewGuid(), prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9007", "Gauri", OrderedAt);
        var item = Item(Guid.NewGuid(), order.Id, prod, "Unshared Course", qty: 1);

        // Faculty attached to the product, but NO FacultyShareEntry — the shape a course with no
        // sharing rule leaves behind.
        db.Faculty.Add(Fac(fac, "Harshad"));
        db.Subjects.Add(Sub(subj, "Audit"));
        db.Products.Add(Prod(prod, "Unshared Course", subj));
        db.ProductSubjects.Add(Link(prod, subj, true));
        db.ProductFaculty.Add(new ProductFaculty
        { Id = Guid.NewGuid(), ProductId = prod, FacultyId = fac, IsPrimary = true });
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        await db.SaveChangesAsync();

        var t = await Builder(db).BuildAsync(Query(), default);

        Assert.Equal(0, t.TotalRows);
        Assert.Empty(t.Rows);
        Assert.Equal(0, t.Totals!.TotalOrders);
        Assert.Equal(0m, t.Totals.TotalSales);
        Assert.Equal("₹0.00", TotalPayout(t));
        Assert.Equal("0", FacultyCovered(t));
        Assert.Contains("Faculty Sharing", t.EmptyMessage);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 12. Date + Faculty + Product + Subject compose with AND
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Two faculty, two products, two subjects, two dates — every filter has something to
    /// exclude, so an OR anywhere in the chain shows up immediately.</summary>
    private sealed record MatrixFx(
        Guid FacWanted, Guid FacOther, Guid ProdWanted, Guid ProdOther,
        Guid SubWanted, Guid SubOther, DateTime InWindow, DateTime OutOfWindow);

    private static async Task<MatrixFx> SeedMatrixAsync(RioCommerceDbContext db)
    {
        Guid fw = Guid.NewGuid(), fo = Guid.NewGuid(),
             pw = Guid.NewGuid(), po = Guid.NewGuid(),
             sw = Guid.NewGuid(), so = Guid.NewGuid();
        var inWindow = DateTime.UtcNow.AddDays(-3);
        var outOfWindow = DateTime.UtcNow.AddDays(-40);

        db.Faculty.AddRange(Fac(fw, "Wanted Faculty"), Fac(fo, "Other Faculty"));
        db.Subjects.AddRange(Sub(sw, "Wanted Subject"), Sub(so, "Other Subject"));
        db.Products.AddRange(Prod(pw, "Wanted Product", sw), Prod(po, "Other Product", so));
        db.ProductSubjects.AddRange(Link(pw, sw, true), Link(po, so, true));

        // The one row that satisfies everything.
        var okOrder = Ord(Guid.NewGuid(), "RIO-9100", "Match", inWindow);
        var okItem = Item(Guid.NewGuid(), okOrder.Id, pw, "Wanted Product", 1);
        db.Orders.Add(okOrder); db.OrderItems.Add(okItem);
        db.FacultyShareEntries.Add(Entry(fw, okOrder, okItem, pw, "Wanted Product", share: 100m));

        // Each of these fails exactly one filter.
        var wrongFaculty = Ord(Guid.NewGuid(), "RIO-9101", "WrongFac", inWindow);
        var wfItem = Item(Guid.NewGuid(), wrongFaculty.Id, pw, "Wanted Product", 1);
        db.Orders.Add(wrongFaculty); db.OrderItems.Add(wfItem);
        db.FacultyShareEntries.Add(Entry(fo, wrongFaculty, wfItem, pw, "Wanted Product", share: 100m));

        var wrongProduct = Ord(Guid.NewGuid(), "RIO-9102", "WrongProd", inWindow);
        var wpItem = Item(Guid.NewGuid(), wrongProduct.Id, po, "Other Product", 1);
        db.Orders.Add(wrongProduct); db.OrderItems.Add(wpItem);
        db.FacultyShareEntries.Add(Entry(fw, wrongProduct, wpItem, po, "Other Product", share: 100m));

        var wrongDate = Ord(Guid.NewGuid(), "RIO-9103", "WrongDate", outOfWindow);
        var wdItem = Item(Guid.NewGuid(), wrongDate.Id, pw, "Wanted Product", 1);
        db.Orders.Add(wrongDate); db.OrderItems.Add(wdItem);
        db.FacultyShareEntries.Add(Entry(fw, wrongDate, wdItem, pw, "Wanted Product", share: 100m));

        await db.SaveChangesAsync();

        // RioCommerceDbContext.SaveChangesAsync stamps CreatedAt = UtcNow on every INSERT, so an order
        // cannot be seeded into the past in one step — it would land in the window along with the
        // rest and the date filter would look broken when it is not. Backdating afterwards works
        // because the Modified branch only touches UpdatedAt.
        wrongDate.CreatedAt = outOfWindow;
        await db.SaveChangesAsync();

        return new MatrixFx(fw, fo, pw, po, sw, so, inWindow, outOfWindow);
    }

    [Fact]
    public async Task All_four_filters_intersect_rather_than_union()
    {
        using var db = NewDb();
        var fx = await SeedMatrixAsync(db);

        var q = Query();
        q.From = DateTime.UtcNow.AddDays(-7);
        q.To = DateTime.UtcNow;
        q.FacultyIds.Add(fx.FacWanted);
        q.ProductIds.Add(fx.ProdWanted);
        q.SubjectIds.Add(fx.SubWanted);

        var t = await Builder(db).BuildAsync(q, default);

        Assert.Equal(1, t.TotalRows);
        Assert.Equal("RIO-9100", Cell(t, 0, "order"));
        Assert.Equal(1, t.Totals!.TotalOrders);
        Assert.Equal(1180m, t.Totals.TotalSales);
        Assert.Equal("₹100.00", TeacherShare(t));
        Assert.Equal("1", FacultyCovered(t));
    }

    [Fact]
    public async Task Each_filter_on_its_own_excludes_only_what_it_should()
    {
        using var db = NewDb();
        var fx = await SeedMatrixAsync(db);

        // Date window alone: drops the 40-day-old order, keeps the other three.
        var dated = Query();
        dated.From = DateTime.UtcNow.AddDays(-7);
        dated.To = DateTime.UtcNow;
        Assert.Equal(3, (await Builder(db).BuildAsync(dated, default)).TotalRows);

        // Faculty alone, inside that window: drops the other faculty's row.
        var byFaculty = Query();
        byFaculty.From = dated.From; byFaculty.To = dated.To;
        byFaculty.FacultyIds.Add(fx.FacWanted);
        Assert.Equal(2, (await Builder(db).BuildAsync(byFaculty, default)).TotalRows);

        // Product alone, inside that window: drops the other product's row.
        var byProduct = Query();
        byProduct.From = dated.From; byProduct.To = dated.To;
        byProduct.ProductIds.Add(fx.ProdWanted);
        Assert.Equal(2, (await Builder(db).BuildAsync(byProduct, default)).TotalRows);

        // Subject alone, inside that window: same two, reached through ProductSubjects.
        var bySubject = Query();
        bySubject.From = dated.From; bySubject.To = dated.To;
        bySubject.SubjectIds.Add(fx.SubWanted);
        Assert.Equal(2, (await Builder(db).BuildAsync(bySubject, default)).TotalRows);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Revenue basis — a cancelled order is not a faculty's earning
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_cancelled_order_is_left_out_of_the_share_report()
    {
        using var db = NewDb();
        Guid fac = Guid.NewGuid(), subj = Guid.NewGuid(), prod = Guid.NewGuid();
        var order = Ord(Guid.NewGuid(), "RIO-9008", "Hina", OrderedAt);
        order.Status = OrderStatus.Cancelled;
        var item = Item(Guid.NewGuid(), order.Id, prod, "Audit", qty: 1);

        db.Faculty.Add(Fac(fac, "Harshad"));
        db.Subjects.Add(Sub(subj, "Audit"));
        db.Products.Add(Prod(prod, "Audit", subj));
        db.ProductSubjects.Add(Link(prod, subj, true));
        db.Orders.Add(order);
        db.OrderItems.Add(item);
        db.FacultyShareEntries.Add(Entry(fac, order, item, prod, "Audit", share: 100m));
        await db.SaveChangesAsync();

        var t = await Builder(db).BuildAsync(Query(), default);

        Assert.Equal(0, t.TotalRows);
        Assert.Equal("₹0.00", TotalPayout(t));
    }
}
