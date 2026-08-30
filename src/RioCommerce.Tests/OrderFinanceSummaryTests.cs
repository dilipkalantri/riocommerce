using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The finance bar above the Orders grid. Its six tiles are read as a single sentence —
/// Gross − Student Discount − Franchise Discount = Net Revenue — so the numbers have to reconcile
/// with each other, not merely each be defensible on its own.
/// </summary>
public class OrderFinanceSummaryTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"ordsum-{Guid.NewGuid()}")
            .Options);

    private static OrderAdminService Svc(RioCommerceDbContext db) =>
        new(db,
            new Mock<IAuditService>().Object,
            new Mock<IRealtimeBus>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IOrderCalculationService>().Object,
            new Mock<INotificationService>().Object,
            new Mock<IFranchiseService>().Object,
            new Mock<IFacultySharingService>().Object,
            new Mock<ISerialKeyService>().Object,
            new Mock<IInvoiceService>().Object,
            new Mock<IInstallmentService>().Object,
            new Mock<INotificationSender>().Object,
            new Mock<IPermissionService>().Object,
            NullLogger<OrderAdminService>.Instance);

    /// <summary>One order with a single line, priced the way the grid stores it.</summary>
    private static async Task<Order> SeedOrderAsync(
        RioCommerceDbContext db, string number, OrderSource source, decimal unitPrice,
        decimal lineDiscount = 0, decimal orderDiscount = 0,
        decimal franchiseShare = 0, decimal gst = 0, int qty = 1)
    {
        var total = unitPrice * qty - orderDiscount;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = number,
            Source = source,
            StudentName = "Test Student",
            StudentPhone = "9876500000",
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Success,
            Subtotal = unitPrice * qty,
            DiscountAmount = orderDiscount,
            TotalAmount = total,
            GstAmount = gst,
            FranchiseShareAmount = franchiseShare,
            FranchiseNetPayable = franchiseShare > 0 ? total - franchiseShare : 0m,
        };
        db.Orders.Add(order);
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = order.Id, ProductId = Guid.NewGuid(),
            ProductTitle = "CA Inter Audit", Quantity = qty,
            UnitPrice = unitPrice, Discount = lineDiscount, LineTotal = unitPrice * qty,
        });
        await db.SaveChangesAsync();
        return order;
    }

    // ── The franchise share is a real deduction ─────────────────────────────────────────────────

    [Fact]
    public async Task FranchiseShareOnTheOrder_IsReportedAsFranchiseDiscount()
    {
        using var db = NewDb();
        // No FranchiseCommissionEntries row — the state every production franchise order is in.
        await SeedOrderAsync(db, "FRN-1", OrderSource.Franchisee,
            unitPrice: 34795m, franchiseShare: 9193.48m, gst: 5307.71m);

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        Assert.Equal(9193.48m, s.FranchiseDiscount);      // was ₹0 — read from an empty ledger table
        Assert.Equal(34795m, s.GrossRevenue);
        Assert.Equal(25601.52m, s.NetRevenue);           // what the institute actually keeps
    }

    [Fact]
    public async Task TheSixTiles_Reconcile_GrossMinusDiscountsEqualsNet()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "RIO-1", OrderSource.Website,   unitPrice: 55688m, gst: 8494.78m);
        await SeedOrderAsync(db, "FRN-1", OrderSource.Franchisee, unitPrice: 34795m, franchiseShare: 9193.48m, gst: 5307.71m);
        await SeedOrderAsync(db, "CNT-1", OrderSource.Counter,   unitPrice: 10000m, gst: 1525.42m);

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        Assert.Equal(3, s.TotalOrders);
        Assert.Equal(100483m, s.GrossRevenue);
        Assert.Equal(0m, s.StudentDiscount);
        Assert.Equal(9193.48m, s.FranchiseDiscount);
        Assert.Equal(91289.52m, s.NetRevenue);

        // The sentence the bar is read as, asserted directly.
        Assert.Equal(s.NetRevenue, s.GrossRevenue - s.StudentDiscount - s.FranchiseDiscount);
    }

    [Fact]
    public async Task NonFranchiseOrders_AreUnaffected()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "RIO-1", OrderSource.Website, unitPrice: 55688m, gst: 8494.78m);

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        Assert.Equal(0m, s.FranchiseDiscount);
        Assert.Equal(55688m, s.GrossRevenue);
        Assert.Equal(55688m, s.NetRevenue);   // no share to deduct, so Net still equals what was billed
    }

    // ── Student discounts still work the way they did ───────────────────────────────────────────

    [Fact]
    public async Task StudentDiscount_CountsTheCanonicalOrderRollUpOnce()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "RIO-1", OrderSource.Website,
            unitPrice: 10000m, lineDiscount: 500m, orderDiscount: 1000m, gst: 1372.88m);

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        // UnitPrice is already the list price, so gross is ₹10,000 — the line discount is NOT added
        // back on top of it.
        Assert.Equal(10000m, s.GrossRevenue);
        // Order.DiscountAmount is the canonical total for the order, so it is counted once. Adding
        // the ₹500 line discount to it would report the same money twice.
        Assert.Equal(1000m, s.StudentDiscount);
        Assert.Equal(9000m, s.NetRevenue);               // 10000 − 1000 order discount
        Assert.Equal(s.NetRevenue, s.GrossRevenue - s.StudentDiscount - s.FranchiseDiscount);
    }

    [Fact]
    public async Task AnOrderWithBothAStudentAndAFranchiseDiscount_DeductsEach()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "FRN-2", OrderSource.Franchisee,
            unitPrice: 10000m, orderDiscount: 1000m, franchiseShare: 900m, gst: 1372.88m);

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        Assert.Equal(10000m, s.GrossRevenue);
        Assert.Equal(1000m, s.StudentDiscount);
        Assert.Equal(900m, s.FranchiseDiscount);
        Assert.Equal(8100m, s.NetRevenue);
        Assert.Equal(s.NetRevenue, s.GrossRevenue - s.StudentDiscount - s.FranchiseDiscount);
    }

    // ── GST ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StoredGstIsUsedWhenPresent_AndDerivedOnlyWhenItIsNot()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "RIO-1", OrderSource.Website, unitPrice: 1180m, gst: 180m);
        await SeedOrderAsync(db, "RIO-2", OrderSource.Website, unitPrice: 1180m, gst: 0m);   // legacy row

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        // 180 stored + 1180 × 18/118 derived = 180 + 180
        Assert.Equal(360m, s.GstTotal);
    }

    [Fact]
    public async Task TaxableTotal_StaysConsistentWithNetAndGst()
    {
        using var db = NewDb();
        await SeedOrderAsync(db, "FRN-1", OrderSource.Franchisee,
            unitPrice: 34795m, franchiseShare: 9193.48m, gst: 5307.71m);

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        Assert.Equal(s.NetRevenue - s.GstTotal, s.TaxableTotal);
    }

    [Fact]
    public async Task NoOrders_ReturnsZeroesRatherThanThrowing()
    {
        using var db = NewDb();

        var s = await Svc(db).SummaryAsync(new OrderFilter());

        Assert.Equal(0, s.TotalOrders);
        Assert.Equal(0m, s.GrossRevenue);
        Assert.Equal(0m, s.NetRevenue);
        Assert.Equal(0m, s.FranchiseDiscount);
    }
}
