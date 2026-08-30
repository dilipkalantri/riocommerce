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
/// Resending the order-confirmation email from the admin order page.
///
/// <para>This button mails a real customer, so most of these tests assert when it must NOT fire.
/// The confirmation states that the order is confirmed and quotes an amount paid — sending it on an
/// unpaid order tells the customer something untrue, and no amount of care in the UI can be relied
/// on to prevent that, so the refusal is asserted against the service.</para>
/// </summary>
public class OrderEmailResendTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"resend-{Guid.NewGuid()}")
            .Options);

    private static OrderAdminService Svc(
        RioCommerceDbContext db,
        Mock<INotificationSender>? sender = null,
        Mock<IAuditService>? audit = null) =>
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
            (sender ?? new Mock<INotificationSender>()).Object,
            new Mock<IPermissionService>().Object,
            NullLogger<OrderAdminService>.Instance);

    /// <summary>RIO-1063 as it stands in production: confirmed, paid, one item.</summary>
    private static async Task<Order> SeedAsync(
        RioCommerceDbContext db,
        PaymentStatus payment = PaymentStatus.Success,
        string? email = "moinuddinmansuri04@gmail.com",
        bool deleted = false)
    {
        var o = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "RIO-1063",
            UserId = Guid.NewGuid(),
            StudentName = "Moinuddin mansuri",
            StudentPhone = "08369926759",
            StudentEmail = email,
            Status = OrderStatus.Confirmed,
            PaymentStatus = payment,
            Subtotal = 299m,
            TotalAmount = 299m,
            IsDeleted = deleted,
        };
        db.Orders.Add(o);
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = o.Id, ProductId = Guid.NewGuid(),
            ProductTitle = "CA Inter LAW Fastrack", Quantity = 1, UnitPrice = 299m, LineTotal = 299m,
        });
        await db.SaveChangesAsync();
        return o;
    }

    // ── The thing this was built for ────────────────────────────────────────────────────────────

    [Fact]
    public async Task APaidOrderResendsTheConfirmation()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var sender = new Mock<INotificationSender>();

        var (ok, error, sentTo) = await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        Assert.True(ok, error);
        Assert.Null(error);
        Assert.Equal("moinuddinmansuri04@gmail.com", sentTo);
        sender.Verify(s => s.SendOrderConfirmationAsync(It.Is<Order>(x => x.Id == o.Id)), Times.Once);
    }

    [Fact]
    public async Task ItSendsTheSameMailCheckoutSends()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var sender = new Mock<INotificationSender>();

        await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        // Going through INotificationSender rather than rebuilding tokens locally is what keeps the
        // resent mail identical to the original. Items must be loaded — the template counts them.
        sender.Verify(s => s.SendOrderConfirmationAsync(
            It.Is<Order>(x => x.OrderNumber == "RIO-1063" && x.Items.Count == 1)), Times.Once);
    }

    [Fact]
    public async Task ItGoesToTheCurrentAddress_NotTheOneTheOrderWasPlacedWith()
    {
        using var db = NewDb();
        var o = await SeedAsync(db, email: "typo@example.com");
        o.StudentEmail = "corrected@example.com";          // admin fixed it before resending
        await db.SaveChangesAsync();
        var sender = new Mock<INotificationSender>();

        var (ok, _, sentTo) = await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        Assert.True(ok);
        Assert.Equal("corrected@example.com", sentTo);
        sender.Verify(s => s.SendOrderConfirmationAsync(
            It.Is<Order>(x => x.StudentEmail == "corrected@example.com")), Times.Once);
    }

    // ── When it must refuse ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task AnUnpaidOrderIsRefusedAndNothingIsSent(PaymentStatus payment)
    {
        using var db = NewDb();
        var o = await SeedAsync(db, payment: payment);
        var sender = new Mock<INotificationSender>();

        var (ok, error, sentTo) = await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        Assert.False(ok);
        Assert.Contains("paid", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sentTo);
        // Counting calls, not just reading the error — the refusal is worthless if the mail went anyway.
        sender.Verify(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnOrderWithNoEmailAddressIsRefused(string? email)
    {
        using var db = NewDb();
        var o = await SeedAsync(db, email: email);
        var sender = new Mock<INotificationSender>();

        var (ok, error, _) = await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        Assert.False(ok);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
        sender.Verify(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task AnOrderInTheRecycleBinIsRefused()
    {
        using var db = NewDb();
        var o = await SeedAsync(db, deleted: true);
        var sender = new Mock<INotificationSender>();

        var (ok, error, _) = await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        Assert.False(ok);
        Assert.Contains("recycle bin", error, StringComparison.OrdinalIgnoreCase);
        sender.Verify(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task AnUnknownOrderIsRefused()
    {
        using var db = NewDb();
        var sender = new Mock<INotificationSender>();

        var (ok, error, _) = await Svc(db, sender).ResendOrderEmailAsync(Guid.NewGuid(), Guid.NewGuid(), "Admin");

        Assert.False(ok);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
        sender.Verify(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()), Times.Never);
    }

    // ── A mail failure must not read as success ─────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheMailProviderThrows_TheAdminIsToldItFailed()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var sender = new Mock<INotificationSender>();
        sender.Setup(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()))
              .ThrowsAsync(new HttpRequestException("SMTP unreachable"));

        var (ok, error, sentTo) = await Svc(db, sender).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(sentTo);
        // The admin gets a plain message, never the raw exception text.
        Assert.DoesNotContain("SMTP", error);
    }

    [Fact]
    public async Task AFailedSendIsNotRecordedAsAResend()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var sender = new Mock<INotificationSender>();
        sender.Setup(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()))
              .ThrowsAsync(new HttpRequestException("down"));
        var audit = new Mock<IAuditService>();

        await Svc(db, sender, audit).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), It.IsAny<string>(), "OrderEmailResent",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    // ── Audit trail ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AResendIsAudited_WithTheRecipient()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var audit = new Mock<IAuditService>();

        await Svc(db, audit: audit).ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin");

        // Who was mailed, and when, is the only record that a customer was contacted twice.
        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), "Admin", "OrderEmailResent",
            It.IsAny<string>(), o.Id.ToString(),
            It.Is<string>(d => d.Contains("RIO-1063") && d.Contains("moinuddinmansuri04@gmail.com"))),
            Times.Once);
    }

    [Fact]
    public async Task ResendingTwiceSendsTwice()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var sender = new Mock<INotificationSender>();
        var svc = Svc(db, sender);

        Assert.True((await svc.ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin")).ok);
        Assert.True((await svc.ResendOrderEmailAsync(o.Id, Guid.NewGuid(), "Admin")).ok);

        // Deliberate: an admin who presses resend again means it. There is no silent cooldown that
        // would report success while sending nothing.
        sender.Verify(s => s.SendOrderConfirmationAsync(It.IsAny<Order>()), Times.Exactly(2));
    }
}
