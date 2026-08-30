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
/// Editing one order's billing and shipping address.
///
/// <para>The billing STATE is not just text: it decides CGST+SGST versus IGST. So these tests care
/// about two things — that an address can actually be corrected, and that correcting it never leaves
/// the order's tax figures disagreeing with the address printed next to them.</para>
/// </summary>
public class OrderAddressEditTests
{
    private const string SellerState = "Maharashtra";

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"orderaddr-{Guid.NewGuid()}")
            .Options);

    private static OrderAdminService Svc(RioCommerceDbContext db, Mock<IAuditService>? audit = null) =>
        new(db,
            (audit ?? new Mock<IAuditService>()).Object,
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

    /// <summary>₹299 intra-state: GST ₹45.61 split as CGST ₹22.80 + SGST ₹22.81.</summary>
    private static async Task<Order> SeedOrderAsync(RioCommerceDbContext db, string billingState = SellerState)
    {
        var intra = string.Equals(billingState, SellerState, StringComparison.OrdinalIgnoreCase);
        var gst = 45.61m;
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = "RIO-1062", UserId = Guid.NewGuid(),
            StudentName = "Rajat Girde", StudentPhone = "9021129106", StudentEmail = "rajatgirde21@gmail.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 299m, TotalAmount = 299m, GstAmount = gst,
            CgstAmount = intra ? 22.80m : 0m,
            SgstAmount = intra ? 22.81m : 0m,
            IgstAmount = intra ? 0m : gst,
            BillingName = "Rajat Girde", BillingCity = "nagpur", BillingState = billingState,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static OrderAddressEdit EditOf(Order o) => new()
    {
        OrderId = o.Id,
        BillingName = o.BillingName, BillingAddress = o.BillingAddress,
        BillingCity = o.BillingCity, BillingState = o.BillingState, BillingPincode = o.BillingPincode,
        ShippingAddress = o.ShippingAddress, ShippingCity = o.ShippingCity,
        ShippingState = o.ShippingState, ShippingPincode = o.ShippingPincode,
    };

    // ── Ordinary corrections ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheBillingAddressCanBeCorrected()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var req = EditOf(o);
        req.BillingAddress = "Flat 3, Shivaji Nagar";
        req.BillingPincode = "440010";

        var (ok, error) = await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid());

        Assert.True(ok, error);
        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal("Flat 3, Shivaji Nagar", saved.BillingAddress);
        Assert.Equal("440010", saved.BillingPincode);
    }

    [Fact]
    public async Task ASeparateShippingAddressCanBeSet()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var req = EditOf(o);
        req.ShippingAddress = "C/o Sharma Book Depot, Main Road";
        req.ShippingCity = "Wardha";
        req.ShippingState = "Maharashtra";
        req.ShippingPincode = "442001";

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal("C/o Sharma Book Depot, Main Road", saved.ShippingAddress);
        Assert.Equal("Wardha", saved.ShippingCity);
        Assert.Equal("442001", saved.ShippingPincode);
    }

    [Fact]
    public async Task ClearingEveryShippingField_MeansShipToBilling()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        o.ShippingAddress = "Old place"; o.ShippingCity = "Pune"; o.ShippingPincode = "411001";
        await db.SaveChangesAsync();

        var req = EditOf(o);
        req.ShippingAddress = null; req.ShippingCity = null; req.ShippingState = null; req.ShippingPincode = null;

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Null(saved.ShippingAddress);
        Assert.Null(saved.ShippingCity);
        Assert.Null(saved.ShippingPincode);
    }

    [Fact]
    public async Task AShippingCityWithNoStreet_IsRejected()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var req = EditOf(o);
        req.ShippingCity = "Wardha";        // half an address is worse than none

        var (ok, error) = await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("shipping street address", error);
    }

    [Theory]
    [InlineData("44001")]
    [InlineData("4400100")]
    [InlineData("04401")]
    [InlineData("abcdef")]
    public async Task AMalformedPinCode_IsRejected(string bad)
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var req = EditOf(o);
        req.BillingPincode = bad;

        var (ok, error) = await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("PIN", error);
    }

    // ── The state drives the tax split ──────────────────────────────────────────────────────────

    [Fact]
    public async Task MovingTheBillingStateOutsideMaharashtra_ReSplitsTheTaxAsIgst()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);                 // starts intra-state
        var req = EditOf(o);
        req.BillingState = "Gujarat";

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal(0m, saved.CgstAmount);
        Assert.Equal(0m, saved.SgstAmount);
        Assert.Equal(45.61m, saved.IgstAmount);
        // The customer still pays the same, and the same tax — only the heads change.
        Assert.Equal(299m, saved.TotalAmount);
        Assert.Equal(45.61m, saved.GstAmount);
    }

    [Fact]
    public async Task MovingTheBillingStateIntoMaharashtra_ReSplitsTheTaxAsCgstSgst()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db, billingState: "Gujarat");   // starts inter-state
        var req = EditOf(o);
        req.BillingState = "Maharashtra";

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal(0m, saved.IgstAmount);
        Assert.Equal(22.80m, saved.CgstAmount);
        Assert.Equal(22.81m, saved.SgstAmount);
        // The halves must still add up to the whole — rounding is absorbed, never dropped.
        Assert.Equal(saved.GstAmount, saved.CgstAmount + saved.SgstAmount + saved.IgstAmount);
    }

    [Fact]
    public async Task EditingTheStreetOnly_LeavesTheTaxSplitAlone()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var req = EditOf(o);
        req.BillingAddress = "New street, same state";

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal(22.80m, saved.CgstAmount);
        Assert.Equal(22.81m, saved.SgstAmount);
        Assert.Equal(0m, saved.IgstAmount);
    }

    // ── An issued tax invoice freezes the state ─────────────────────────────────────────────────

    [Fact]
    public async Task OnceAnInvoiceExists_TheBillingStateCannotBeChanged()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        db.Set<Invoice>().Add(new Invoice
        {
            Id = Guid.NewGuid(), OrderId = o.Id, OrderNumber = o.OrderNumber,
            InvoiceNumber = "INV/2026/0001", BillingState = SellerState,
        });
        await db.SaveChangesAsync();

        var req = EditOf(o);
        req.BillingState = "Gujarat";

        var (ok, error) = await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("tax invoice", error);
        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal(SellerState, saved.BillingState);
        Assert.Equal(22.80m, saved.CgstAmount);      // tax untouched too
    }

    [Fact]
    public async Task OnceAnInvoiceExists_EveryOtherAddressFieldIsStillEditable()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        db.Set<Invoice>().Add(new Invoice
        {
            Id = Guid.NewGuid(), OrderId = o.Id, OrderNumber = o.OrderNumber,
            InvoiceNumber = "INV/2026/0001", BillingAddress = "Old address", BillingState = SellerState,
        });
        await db.SaveChangesAsync();

        var req = EditOf(o);
        req.BillingAddress = "Corrected street";
        req.BillingPincode = "440010";
        req.ShippingAddress = "Deliver to the shop instead";

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal("Corrected street", saved.BillingAddress);
        Assert.Equal("Deliver to the shop instead", saved.ShippingAddress);

        // The issued invoice keeps its own snapshot — a tax document is not restated by an address form.
        Assert.Equal("Old address", (await db.Set<Invoice>().AsNoTracking().FirstAsync()).BillingAddress);
    }

    // ── Scope, audit, misc ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NothingButTheAddressIsTouched()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var req = EditOf(o);
        req.BillingAddress = "Somewhere else";

        Assert.True((await Svc(db).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Orders.AsNoTracking().FirstAsync(x => x.Id == o.Id);
        Assert.Equal(299m, saved.TotalAmount);
        Assert.Equal(299m, saved.Subtotal);
        Assert.Equal(OrderStatus.Confirmed, saved.Status);
        Assert.Equal(PaymentStatus.Success, saved.PaymentStatus);
        Assert.Equal("Rajat Girde", saved.StudentName);
        Assert.Equal("9021129106", saved.StudentPhone);
    }

    [Fact]
    public async Task AnUnknownOrderIsRejected()
    {
        using var db = NewDb();

        var (ok, error) = await Svc(db).UpdateAddressesAsync(
            new OrderAddressEdit { OrderId = Guid.NewGuid() }, Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal("Order not found.", error);
    }

    [Fact]
    public async Task OnlyTheFieldsThatChangedAreAudited()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var audit = new Mock<IAuditService>();
        string? details = null;
        audit.Setup(a => a.LogAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
             .Callback<Guid?, string, string, string, string?, string?>((_, _, _, _, _, d) => details = d)
             .Returns(Task.CompletedTask);

        var req = EditOf(o);
        req.BillingPincode = "440010";
        Assert.True((await Svc(db, audit).UpdateAddressesAsync(req, Guid.NewGuid())).ok);

        Assert.NotNull(details);
        Assert.Contains("BillingPincode", details);
        Assert.Contains("440010", details);
        Assert.DoesNotContain("BillingCity", details);
    }

    [Fact]
    public async Task SavingWithNoChanges_SucceedsAndWritesNoAuditEntry()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        var audit = new Mock<IAuditService>();

        Assert.True((await Svc(db, audit).UpdateAddressesAsync(EditOf(o), Guid.NewGuid())).ok);

        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                                     It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
                     Times.Never);
    }

    [Fact]
    public async Task TheOrderPageSeesTheShippingAddressAndTheInvoiceFlag()
    {
        using var db = NewDb();
        var o = await SeedOrderAsync(db);
        o.ShippingAddress = "Shop address"; o.ShippingCity = "Wardha";
        await db.SaveChangesAsync();

        var detail = await Svc(db).GetDetailAsync(o.Id);

        Assert.NotNull(detail);
        Assert.Equal("Shop address", detail!.ShippingAddress);
        Assert.Equal("Wardha", detail.ShippingCity);
        Assert.False(detail.HasInvoice);
    }
}
