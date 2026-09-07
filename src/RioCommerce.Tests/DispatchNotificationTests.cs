using RioCommerce.Core.DTOs.Admin;
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
/// Covers the customer-facing half of dispatch: who gets emailed, when, and what the customer is
/// allowed to see. These run against a real <see cref="RioCommerceDbContext"/> (in-memory) because the
/// things most likely to break — the duplicate-email guard and the ownership check — are properties
/// of the persisted state, not of any single pure function.
/// </summary>
public class DispatchNotificationTests
{
    // ── Test doubles ────────────────────────────────────────────────────────────────────────────

    /// <summary>Records what would have been emailed instead of sending it.</summary>
    private sealed class RecordingNotifier : INotificationService
    {
        public List<(string Key, NotificationRecipient To, IDictionary<string, string> Tokens)> Sent { get; } = new();
        public Exception? ThrowOnSend { get; set; }

        public Task<(bool ok, string? error, int channelsSent)> SendAsync(
            string templateKey, NotificationRecipient to, IDictionary<string, string> tokens, string? triggeredBy = null)
        {
            if (ThrowOnSend != null) throw ThrowOnSend;
            Sent.Add((templateKey, to, tokens));
            return Task.FromResult((true, (string?)null, 1));
        }

        // Not exercised by these tests — the dispatch path only ever calls SendAsync.
        public Task<NotificationStats> StatsAsync() => throw new NotSupportedException();
        public Task<List<MessageTemplateItem>> ListTemplatesAsync() => throw new NotSupportedException();
        public Task<MessageTemplateEditModel?> GetTemplateAsync(Guid id) => throw new NotSupportedException();
        public Task<(bool ok, string? error, Guid id)> SaveTemplateAsync(MessageTemplateEditModel m) => throw new NotSupportedException();
        public Task ToggleTemplateAsync(Guid id) => throw new NotSupportedException();
        public Task<(bool ok, string? error)> DeleteTemplateAsync(Guid id) => throw new NotSupportedException();
        public Task<List<NotificationLogItem>> LogsAsync(int take = 50) => throw new NotSupportedException();
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────────────────────────

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"dispatch-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static OperationsService Ops(RioCommerceDbContext db, RecordingNotifier notifier) =>
        new(db, notifier, NullLogger<OperationsService>.Instance);

    /// <summary>The customer-facing receipt service. Every collaborator except the database is
    /// irrelevant to <c>GetReceiptAsync</c>, so they are stubs.</summary>
    private static CheckoutService Checkout(RioCommerceDbContext db) =>
        new(db,
            new Mock<IPaymentGatewayFactory>().Object,
            new Mock<INotificationSender>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IRealtimeBus>().Object,
            new Mock<ISerialKeyService>().Object,
            new Mock<IInvoiceService>().Object,
            new Mock<IFacultySharingService>().Object,
            new Mock<INotificationService>().Object,
            new Mock<ISmsSender>().Object,
            NullLogger<CheckoutService>.Instance);

    private static async Task<Order> SeedOrderAsync(RioCommerceDbContext db, Guid? userId = null, string number = "RIO-9001")
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = number,
            UserId = userId ?? Guid.NewGuid(),
            StudentName = "Test Student",
            StudentPhone = "9876500000",
            StudentEmail = "student@example.com",
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m,
            TotalAmount = 1000m,
        };
        db.Orders.Add(order);
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = Guid.NewGuid(),
            ProductTitle = "CA Inter Costing — Printed Notes",
            Quantity = 1,
            UnitPrice = 1000m,
            LineTotal = 1000m,
        });
        await db.SaveChangesAsync();
        return order;
    }

    /// <summary>Puts an order through the real admin flow: assign courier, then dispatch.</summary>
    private static async Task<(bool ok, string? error)> DispatchAsync(
        OperationsService ops, Guid orderId, string courier, string? tracking)
    {
        var saved = await ops.SaveShipmentAsync(new ShipmentSaveRequest
        {
            OrderId = orderId,
            Courier = courier,
            TrackingNumber = tracking,
        });
        if (!saved.ok) return saved;
        return await ops.MarkDispatchedAsync(orderId);
    }

    // ── TEST 1 — Trackon ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test1_TrackonDispatch_StoresTracking_NotifiesCustomer_AndIsVisibleToThem()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var order = await SeedOrderAsync(db);

        var (ok, error) = await DispatchAsync(Ops(db, notifier), order.Id, Couriers.Trackon, "ABC123456");

        Assert.True(ok, error);

        // …tracking number stored, tracking URL resolved centrally
        var shipment = await db.Shipments.FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal(Couriers.Trackon, shipment.Courier);
        Assert.Equal("ABC123456", shipment.TrackingNumber);
        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);
        Assert.NotNull(shipment.DispatchedAt);
        Assert.Equal("https://www.trackon.in/courier-tracking", Couriers.TrackingUrl(Couriers.Trackon));

        // …dispatch email carries the tracking information
        var sent = Assert.Single(notifier.Sent);
        Assert.Equal("order_dispatched", sent.Key);
        Assert.Equal("student@example.com", sent.To.Email);
        Assert.Equal(order.OrderNumber, sent.Tokens["order_number"]);
        Assert.Equal(Couriers.Trackon, sent.Tokens["courier"]);
        Assert.Equal("ABC123456", sent.Tokens["tracking_number"]);
        Assert.Equal("https://www.trackon.in/courier-tracking", sent.Tokens["tracking_url"]);
        Assert.Contains("ABC123456", sent.Tokens["tracking_block"]);
        Assert.Contains("trackon.in", sent.Tokens["tracking_block"]);
        Assert.Contains("Printed Notes", sent.Tokens["product_title"]);

        // …and the customer sees it, with a Track Shipment button
        var receipt = await Checkout(db).GetReceiptAsync(order.UserId!.Value, order.OrderNumber);
        Assert.NotNull(receipt!.Shipment);
        Assert.Equal(Couriers.Trackon, receipt.Shipment!.Courier);
        Assert.Equal("ABC123456", receipt.Shipment.TrackingNumber);
        Assert.Equal("https://www.trackon.in/courier-tracking", receipt.Shipment.TrackingUrl);
        Assert.True(receipt.Shipment.HasTracking);
    }

    // ── TEST 2 — India Post ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test2_IndiaPostDispatch_StoresTracking_NotifiesCustomer_AndIsVisibleToThem()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var order = await SeedOrderAsync(db);

        var (ok, error) = await DispatchAsync(Ops(db, notifier), order.Id, Couriers.IndiaPost, "EN123456789IN");

        Assert.True(ok, error);

        var shipment = await db.Shipments.FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal(Couriers.IndiaPost, shipment.Courier);
        Assert.Equal("EN123456789IN", shipment.TrackingNumber);
        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal("EN123456789IN", sent.Tokens["tracking_number"]);
        Assert.Equal("https://www.indiapost.gov.in/tracking", sent.Tokens["tracking_url"]);
        Assert.Contains("EN123456789IN", sent.Tokens["tracking_block"]);

        var receipt = await Checkout(db).GetReceiptAsync(order.UserId!.Value, order.OrderNumber);
        Assert.Equal(Couriers.IndiaPost, receipt!.Shipment!.Courier);
        Assert.Equal("https://www.indiapost.gov.in/tracking", receipt.Shipment.TrackingUrl);
        Assert.True(receipt.Shipment.HasTracking);
    }

    // ── TEST 3 — PCMC (no tracking anywhere) ────────────────────────────────────────────────────

    [Fact]
    public async Task Test3_PcmcDispatch_StoresNoTracking_AndEmailHasNoTrackingSection()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var order = await SeedOrderAsync(db);

        // A tracking number is offered on purpose — it must be discarded, not stored.
        var (ok, error) = await DispatchAsync(Ops(db, notifier), order.Id, Couriers.Pcmc, "SHOULD-BE-DROPPED");

        Assert.True(ok, error);

        var shipment = await db.Shipments.FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal(Couriers.Pcmc, shipment.Courier);
        Assert.Null(shipment.TrackingNumber);
        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);

        var sent = Assert.Single(notifier.Sent);
        Assert.Equal(Couriers.Pcmc, sent.Tokens["courier"]);
        Assert.Equal("", sent.Tokens["tracking_number"]);
        Assert.Equal("", sent.Tokens["tracking_url"]);
        Assert.Equal("", sent.Tokens["tracking_block"]);   // whole section collapses away

        // The customer sees the delivery method only — no number, no button.
        var receipt = await Checkout(db).GetReceiptAsync(order.UserId!.Value, order.OrderNumber);
        Assert.Equal(Couriers.Pcmc, receipt!.Shipment!.Courier);
        Assert.Null(receipt.Shipment.TrackingNumber);
        Assert.Null(receipt.Shipment.TrackingUrl);
        Assert.False(receipt.Shipment.HasTracking);
    }

    // ── TEST 4 / 5 — a tracking courier with no number ──────────────────────────────────────────

    [Theory]
    [InlineData(Couriers.Trackon)]   // TEST 4
    [InlineData(Couriers.IndiaPost)] // TEST 5
    public async Task Test4And5_TrackingCourierWithoutNumber_IsRejected_AndSendsNoEmail(string courier)
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var order = await SeedOrderAsync(db);
        var ops = Ops(db, notifier);

        var saved = await ops.SaveShipmentAsync(new ShipmentSaveRequest { OrderId = order.Id, Courier = courier });
        Assert.False(saved.ok);
        Assert.Contains("tracking number", saved.error, StringComparison.OrdinalIgnoreCase);

        // Even if the row were somehow saved without a number, dispatch itself must refuse.
        var dispatched = await ops.MarkDispatchedAsync(order.Id);
        Assert.False(dispatched.ok);

        Assert.Empty(notifier.Sent);
        Assert.False(await db.Shipments.AnyAsync(s => s.OrderId == order.Id && s.Status == ShipmentStatus.Dispatched));
    }

    // ── TEST 6 — courier outside the supported list ─────────────────────────────────────────────

    [Fact]
    public async Task Test6_InvalidCourier_IsRejected()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var order = await SeedOrderAsync(db);

        var saved = await Ops(db, notifier).SaveShipmentAsync(new ShipmentSaveRequest
        {
            OrderId = order.Id,
            Courier = "Blue Dart",
            TrackingNumber = "BD999",
        });

        Assert.False(saved.ok);
        Assert.NotNull(saved.error);
        Assert.Empty(notifier.Sent);
        Assert.Null(await db.Shipments.FirstOrDefaultAsync(s => s.OrderId == order.Id));
    }

    // ── TEST 7 — one customer must not see another's shipment ───────────────────────────────────

    [Fact]
    public async Task Test7_AnotherCustomersOrder_ExposesNothing()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var owner = Guid.NewGuid();
        var intruder = Guid.NewGuid();
        var order = await SeedOrderAsync(db, owner);

        Assert.True((await DispatchAsync(Ops(db, notifier), order.Id, Couriers.Trackon, "SECRET-AWB")).ok);

        // Guessing the order number is not enough — ownership is part of the query, so the whole
        // receipt (shipment included) comes back null rather than merely hiding a field.
        var stolen = await Checkout(db).GetReceiptAsync(intruder, order.OrderNumber);
        Assert.Null(stolen);

        var mine = await Checkout(db).GetReceiptAsync(owner, order.OrderNumber);
        Assert.Equal("SECRET-AWB", mine!.Shipment!.TrackingNumber);
    }

    // ── TEST 8 — pressing Dispatch again ────────────────────────────────────────────────────────

    [Fact]
    public async Task Test8_RepeatedDispatch_SendsOnlyOneEmail()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier();
        var order = await SeedOrderAsync(db);
        var ops = Ops(db, notifier);

        Assert.True((await DispatchAsync(ops, order.Id, Couriers.Trackon, "ABC123456")).ok);
        var firstDispatchedAt = (await db.Shipments.FirstAsync(s => s.OrderId == order.Id)).DispatchedAt;

        Assert.True((await ops.MarkDispatchedAsync(order.Id)).ok);
        Assert.True((await ops.MarkDispatchedAsync(order.Id)).ok);

        Assert.Single(notifier.Sent);

        // The re-runs must also leave the original dispatch timestamp alone.
        var shipment = await db.Shipments.FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal(firstDispatchedAt, shipment.DispatchedAt);
    }

    // ── TEST 9 — a shipment written before the courier list existed ─────────────────────────────

    [Fact]
    public async Task Test9_LegacyDtdcShipment_StaysReadable_AndIsNotRewritten()
    {
        using var db = NewDb();
        var order = await SeedOrderAsync(db);
        db.Shipments.Add(new Shipment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Courier = "DTDC",                  // free text from before Couriers existed
            TrackingNumber = "D1234567",
            Status = ShipmentStatus.Dispatched,
            DispatchedAt = new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        var receipt = await Checkout(db).GetReceiptAsync(order.UserId!.Value, order.OrderNumber);

        // Readable as-is: the courier name and number survive untouched…
        Assert.Equal("DTDC", receipt!.Shipment!.Courier);
        Assert.Equal("D1234567", receipt.Shipment.TrackingNumber);
        Assert.Equal(new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc), receipt.Shipment.DispatchedAt);

        // …and because we have no tracking page for an unknown courier, no button is offered rather
        // than a guessed URL.
        Assert.Null(receipt.Shipment.TrackingUrl);
        Assert.False(receipt.Shipment.HasTracking);

        // Nothing was rewritten by reading it.
        var stored = await db.Shipments.AsNoTracking().FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal("DTDC", stored.Courier);
        Assert.Equal("D1234567", stored.TrackingNumber);
    }

    // ── Failure handling (spec §11) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailFailure_DoesNotRollBackTheDispatch()
    {
        using var db = NewDb();
        var notifier = new RecordingNotifier { ThrowOnSend = new InvalidOperationException("SMTP down") };
        var order = await SeedOrderAsync(db);

        var (ok, error) = await DispatchAsync(Ops(db, notifier), order.Id, Couriers.Trackon, "ABC123456");

        Assert.True(ok, error);
        var shipment = await db.Shipments.AsNoTracking().FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);
        Assert.NotNull(shipment.DispatchedAt);
    }
}
