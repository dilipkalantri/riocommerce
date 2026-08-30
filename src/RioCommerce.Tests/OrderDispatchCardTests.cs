using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Shipping;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The Dispatch / Shipment card on an order's detail page.
///
/// <para>The card is read-only — the dispatch board keeps sole ownership of creating and editing a
/// consignment — so what matters here is that the detail page reports THIS order's shipment and
/// only this order's, and that a courier with no tracking is not made to look like one that lost
/// its number.</para>
/// </summary>
public class OrderDispatchCardTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"orderdispatch-{Guid.NewGuid()}")
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

    private static async Task<Order> SeedOrderAsync(RioCommerceDbContext db, string orderNumber)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            StudentName = "Sonica M",
            StudentPhone = "07996052114",
            StudentEmail = "sonica1795@gmail.com",
            Status = OrderStatus.Processing,
            PaymentStatus = PaymentStatus.Success,
            Subtotal = 8100m,
            TotalAmount = 8100m,
            GstAmount = 1235.59m,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task SeedShipmentAsync(
        RioCommerceDbContext db, Guid orderId, ShipmentStatus status,
        string? courier, string? tracking,
        DateTime? dispatchedAt = null, DateTime? deliveredAt = null)
    {
        db.Shipments.Add(new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Status = status,
            Courier = courier,
            TrackingNumber = tracking,
            DispatchedAt = dispatchedAt,
            DeliveredAt = deliveredAt,
        });
        await db.SaveChangesAsync();
    }

    // ── No shipment record yet ─────────────────────────────────────────────

    [Fact]
    public async Task An_order_with_no_shipment_reads_Pending_with_nothing_invented()
    {
        using var db = NewDb();
        var order = await SeedOrderAsync(db, "RIO-1070");

        var d = await Svc(db).GetDetailAsync(order.Id);

        Assert.NotNull(d);
        Assert.False(d!.HasShipment);
        // Same default the dispatch board shows for an order it has no row for.
        Assert.Equal(ShipmentStatus.Pending, d.ShipmentStatus);
        // No fake courier, no fake consignment number.
        Assert.Null(d.ShipmentCourier);
        Assert.Null(d.ShipmentTrackingNumber);
        Assert.Null(d.DispatchedAt);
        Assert.Null(d.DeliveredAt);
    }

    [Fact]
    public async Task A_shipment_row_parked_at_Pending_is_distinguishable_from_having_none()
    {
        // Both read "Pending", but only one has a courier already chosen — HasShipment is what
        // separates them, and it decides whether the card offers "View" or "Go to" Dispatch.
        using var db = NewDb();
        var order = await SeedOrderAsync(db, "RIO-1069");
        await SeedShipmentAsync(db, order.Id, ShipmentStatus.Pending, Couriers.Trackon, "100458816999");

        var d = await Svc(db).GetDetailAsync(order.Id);

        Assert.True(d!.HasShipment);
        Assert.Equal(ShipmentStatus.Pending, d.ShipmentStatus);
        Assert.Equal("Trackon", d.ShipmentCourier);
    }

    // ── Dispatched / delivered ─────────────────────────────────────────────

    [Fact]
    public async Task A_dispatched_Trackon_order_reports_its_courier_and_tracking_number()
    {
        // RIO-1072 as it stands on the dispatch board.
        using var db = NewDb();
        var order = await SeedOrderAsync(db, "RIO-1072");
        var at = new DateTime(2026, 8, 23, 10, 15, 0, DateTimeKind.Utc);
        await SeedShipmentAsync(db, order.Id, ShipmentStatus.Dispatched, Couriers.Trackon, "100458816774", dispatchedAt: at);

        var d = await Svc(db).GetDetailAsync(order.Id);

        Assert.True(d!.HasShipment);
        Assert.Equal(ShipmentStatus.Dispatched, d.ShipmentStatus);
        Assert.Equal("Trackon", d.ShipmentCourier);
        Assert.Equal("100458816774", d.ShipmentTrackingNumber);
        Assert.Equal(at, d.DispatchedAt);

        // The card's Track link comes from the shared courier list, not from any mapping of its own.
        Assert.True(Couriers.RequiresTracking(d.ShipmentCourier));
        Assert.NotNull(Couriers.TrackingUrl(d.ShipmentCourier));
    }

    [Fact]
    public async Task A_delivered_order_keeps_reporting_its_courier_and_tracking()
    {
        using var db = NewDb();
        var order = await SeedOrderAsync(db, "RIO-1071");
        var sent = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
        var got = new DateTime(2026, 8, 22, 17, 30, 0, DateTimeKind.Utc);
        await SeedShipmentAsync(db, order.Id, ShipmentStatus.Delivered, Couriers.Trackon, "100458816776", sent, got);

        var d = await Svc(db).GetDetailAsync(order.Id);

        Assert.Equal(ShipmentStatus.Delivered, d!.ShipmentStatus);
        Assert.Equal("Trackon", d.ShipmentCourier);
        Assert.Equal("100458816776", d.ShipmentTrackingNumber);
        Assert.Equal(got, d.DeliveredAt);
    }

    // ── Per-courier behaviour ──────────────────────────────────────────────

    [Fact]
    public async Task An_India_Post_shipment_reports_a_tracking_number()
    {
        using var db = NewDb();
        var order = await SeedOrderAsync(db, "RIO-1068");
        await SeedShipmentAsync(db, order.Id, ShipmentStatus.Dispatched, Couriers.IndiaPost, "EX123456789IN");

        var d = await Svc(db).GetDetailAsync(order.Id);

        Assert.Equal("India Post", d!.ShipmentCourier);
        Assert.Equal("EX123456789IN", d.ShipmentTrackingNumber);
        Assert.True(Couriers.RequiresTracking(d.ShipmentCourier));
        Assert.NotNull(Couriers.TrackingUrl(d.ShipmentCourier));
    }

    [Fact]
    public async Task A_PCMC_shipment_has_no_tracking_number_and_no_tracking_url()
    {
        // Local same-day delivery: there is no consignment number to show and no page to link to.
        // The card must read "—", not an empty box that looks like a lost number.
        using var db = NewDb();
        var order = await SeedOrderAsync(db, "RIO-1067");
        await SeedShipmentAsync(db, order.Id, ShipmentStatus.Dispatched, Couriers.Pcmc, tracking: null);

        var d = await Svc(db).GetDetailAsync(order.Id);

        Assert.Equal("PCMC - 1 Day Delivery", d!.ShipmentCourier);
        Assert.Null(d.ShipmentTrackingNumber);
        Assert.Equal(ShipmentStatus.Dispatched, d.ShipmentStatus);
        Assert.False(Couriers.RequiresTracking(d.ShipmentCourier));
        Assert.Null(Couriers.TrackingUrl(d.ShipmentCourier));
    }

    // ── Isolation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Each_order_reports_only_its_own_shipment()
    {
        using var db = NewDb();
        var a = await SeedOrderAsync(db, "RIO-2001");
        var b = await SeedOrderAsync(db, "RIO-2002");
        await SeedShipmentAsync(db, a.Id, ShipmentStatus.Dispatched, Couriers.Trackon, "AAA-111");
        await SeedShipmentAsync(db, b.Id, ShipmentStatus.Delivered, Couriers.IndiaPost, "BBB-222");

        var da = await Svc(db).GetDetailAsync(a.Id);
        var dbe = await Svc(db).GetDetailAsync(b.Id);

        Assert.Equal("AAA-111", da!.ShipmentTrackingNumber);
        Assert.Equal(ShipmentStatus.Dispatched, da.ShipmentStatus);

        Assert.Equal("BBB-222", dbe!.ShipmentTrackingNumber);
        Assert.Equal(ShipmentStatus.Delivered, dbe.ShipmentStatus);
    }

    [Fact]
    public async Task A_shipment_belonging_to_another_customers_order_is_never_borrowed()
    {
        // Same student, same phone, same email on both orders — the only thing separating the two
        // consignments is OrderId, which is exactly what the lookup must use.
        using var db = NewDb();
        var withShipment = await SeedOrderAsync(db, "RIO-3001");
        var withoutShipment = await SeedOrderAsync(db, "RIO-3002");
        await SeedShipmentAsync(db, withShipment.Id, ShipmentStatus.Dispatched, Couriers.Trackon, "ONLY-MINE");

        var d = await Svc(db).GetDetailAsync(withoutShipment.Id);

        Assert.False(d!.HasShipment);
        Assert.Null(d.ShipmentCourier);
        Assert.Null(d.ShipmentTrackingNumber);
        Assert.NotEqual("ONLY-MINE", d.ShipmentTrackingNumber);
        Assert.Equal(ShipmentStatus.Pending, d.ShipmentStatus);
    }
}
