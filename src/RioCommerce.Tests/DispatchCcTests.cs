using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Notifications;
using RioCommerce.Core.Shipping;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// CC on the order-dispatched email. Runs the real <see cref="NotificationService"/> against an
/// in-memory database and a fake email transport, so what is asserted is the actual
/// <see cref="EmailMessage"/> handed to the provider — the CC header itself, not an intermediate
/// value that a provider might then ignore.
/// </summary>
public class DispatchCcTests
{
    // ── Test doubles ────────────────────────────────────────────────────────────────────────────

    /// <summary>Captures every outbound message instead of sending it.</summary>
    private sealed class CapturingRouter : IEmailRouter
    {
        public List<EmailMessage> Sent { get; } = new();
        public bool FailSends { get; set; }

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(FailSends
                ? EmailSendResult.Failure("SMTP refused the connection", null, 1)
                : EmailSendResult.Success("ok", 1));
        }

        public Task<EmailSendResult> SendTestAsync(string providerKey, EmailMessage message, CancellationToken ct = default)
            => SendAsync(message, ct);

        public Task<string> GetActiveProviderKeyAsync(CancellationToken ct = default) => Task.FromResult("smtp");
    }

    // ── Fixture helpers ─────────────────────────────────────────────────────────────────────────

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"cc-{Guid.NewGuid()}")
            .Options);

    private static NotificationService Notifications(RioCommerceDbContext db, CapturingRouter router) =>
        new(db, new Mock<IMessageDispatcher>().Object, router);

    private static OperationsService Ops(RioCommerceDbContext db, NotificationService notify) =>
        new(db, notify, NullLogger<OperationsService>.Instance);

    /// <summary>Installs the dispatch template exactly as migration 0035 does, plus a CC value.</summary>
    private static async Task SeedTemplateAsync(RioCommerceDbContext db, string? cc)
    {
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(),
            Key = "order_dispatched",
            Name = "Order — Dispatched (Email)",
            Channel = "Email",
            Subject = "Your order {{order_number}} has been dispatched — RioCommerce",
            Body = "Hi {{name}},\n\nGood news — your order {{order_number}} has been dispatched.\n\n"
                 + "Items: {{product_title}}\nCourier: {{courier}}\n{{tracking_block}}",
            IsActive = true,
            CcEmails = cc,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Order> SeedOrderAsync(RioCommerceDbContext db, string email = "student@example.com")
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = "RIO-9100",
            UserId = Guid.NewGuid(),
            StudentName = "Test Student",
            StudentPhone = "9876500000",
            StudentEmail = email,
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

    /// <summary>Full path: template with this CC → dispatch → the message the transport received.</summary>
    private static async Task<(CapturingRouter Router, bool Ok)> DispatchWithCcAsync(
        string? cc, string courier = Couriers.Trackon, string? tracking = "ABC123456",
        string customerEmail = "student@example.com")
    {
        using var db = NewDb();
        var router = new CapturingRouter();
        await SeedTemplateAsync(db, cc);
        var order = await SeedOrderAsync(db, customerEmail);
        var ok = (await DispatchAsync(Ops(db, Notifications(db, router)), order.Id, courier, tracking)).ok;
        return (router, ok);
    }

    // ── TEST 1 — no CC configured ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test1_NoCcConfigured_SendsToCustomerOnly()
    {
        var (router, ok) = await DispatchWithCcAsync(cc: null);

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal("student@example.com", msg.ToEmail);
        Assert.Empty(msg.Cc ?? Array.Empty<string>());
    }

    // ── TEST 2 — one CC ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test2_OneCcConfigured_AppearsOnTheCcHeader()
    {
        var (router, ok) = await DispatchWithCcAsync("staff1@riocommerce.com");

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal("student@example.com", msg.ToEmail);
        Assert.Equal(new[] { "staff1@riocommerce.com" }, msg.Cc);
    }

    // ── TEST 3 — two CC ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test3_TwoCcConfigured_BothAppearOnTheCcHeader()
    {
        var (router, ok) = await DispatchWithCcAsync("staff1@riocommerce.com, staff2@riocommerce.com");

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal(new[] { "staff1@riocommerce.com", "staff2@riocommerce.com" }, msg.Cc);
    }

    // ── TEST 4 — surrounding whitespace ─────────────────────────────────────────────────────────

    [Fact]
    public void Test4_AddressesAreTrimmed()
    {
        var parsed = CcRecipients.Parse(" staff1@example.com , staff2@example.com ");

        Assert.True(parsed.Ok);
        Assert.Equal(new[] { "staff1@example.com", "staff2@example.com" }, parsed.Addresses);
        Assert.Equal("staff1@example.com, staff2@example.com", parsed.Normalised);
    }

    // ── TEST 5 — duplicates, different casing ───────────────────────────────────────────────────

    [Fact]
    public async Task Test5_DuplicateAddresses_AreCollapsedCaseInsensitively()
    {
        var parsed = CcRecipients.Parse("staff@example.com, STAFF@example.com");
        Assert.True(parsed.Ok);
        Assert.Equal(new[] { "staff@example.com" }, parsed.Addresses);

        // …and the same holds end to end, so the mailbox is not copied twice.
        var (router, _) = await DispatchWithCcAsync("staff@example.com, STAFF@example.com");
        Assert.Single(Assert.Single(router.Sent).Cc!);
    }

    // ── TEST 6 — the customer's own address listed as CC ────────────────────────────────────────

    [Fact]
    public async Task Test6_CustomerAddressInCc_IsNotCopiedTwice()
    {
        var (router, ok) = await DispatchWithCcAsync("student@example.com, staff@example.com");

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal("student@example.com", msg.ToEmail);
        Assert.Equal(new[] { "staff@example.com" }, msg.Cc);
        Assert.DoesNotContain(msg.Cc!, a => a.Equals(msg.ToEmail, StringComparison.OrdinalIgnoreCase));
    }

    // ── TEST 7 — more than two ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test7_MoreThanTwoAddresses_IsRejectedOnSave()
    {
        var parsed = CcRecipients.Parse("a@example.com,b@example.com,c@example.com");
        Assert.False(parsed.Ok);
        Assert.Equal("Maximum 2 CC email addresses are allowed.", parsed.Error);

        // The Admin save path must surface that same error rather than silently truncating.
        using var db = NewDb();
        var (ok, error, _) = await Notifications(db, new CapturingRouter()).SaveTemplateAsync(new MessageTemplateEditModel
        {
            Key = "order_dispatched", Name = "Order — Dispatched (Email)", Channel = "Email",
            Subject = "s", Body = "b", CcEmails = "a@example.com,b@example.com,c@example.com",
        });
        Assert.False(ok);
        Assert.Equal("Maximum 2 CC email addresses are allowed.", error);
        Assert.Empty(db.MessageTemplates);
    }

    // ── TEST 8 — malformed addresses ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("staff@invalid")]              // no dotted domain — would never resolve
    [InlineData("not-an-email")]
    [InlineData("staff@@example.com")]
    [InlineData("staff@example..com")]
    [InlineData("Staff <staff@example.com>")]  // display-name form, not a bare address
    public void Test8_InvalidEmail_IsRejected(string bad)
    {
        var parsed = CcRecipients.Parse(bad);

        Assert.False(parsed.Ok);
        Assert.NotNull(parsed.Error);
        Assert.Empty(parsed.Addresses);
    }

    /// <summary>Header injection: a CR/LF would let a stored value append its own headers.</summary>
    [Theory]
    [InlineData("staff@example.com\r\nBcc: attacker@evil.com")]
    [InlineData("staff@example.com\nBcc: attacker@evil.com")]
    public void HeaderInjectionAttempt_IsRejected(string bad)
    {
        var parsed = CcRecipients.Parse(bad);

        Assert.False(parsed.Ok);
        Assert.Empty(parsed.Addresses);
    }

    // ── TEST 9 — empty CC is a normal send ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",  ,")]
    public void Test9_EmptyCc_IsValidAndYieldsNothing(string? raw)
    {
        var parsed = CcRecipients.Parse(raw);

        Assert.True(parsed.Ok);
        Assert.Null(parsed.Error);
        Assert.Empty(parsed.Addresses);
        Assert.Null(parsed.Normalised);
    }

    // ── TEST 10 — rejected dispatch emails nobody ───────────────────────────────────────────────

    [Fact]
    public async Task Test10_DispatchValidationFailure_SendsNeitherCustomerNorCcEmail()
    {
        using var db = NewDb();
        var router = new CapturingRouter();
        await SeedTemplateAsync(db, "staff1@riocommerce.com, staff2@riocommerce.com");
        var order = await SeedOrderAsync(db);

        // Trackon with no tracking number — rejected by the existing validation.
        var result = await DispatchAsync(Ops(db, Notifications(db, router)), order.Id, Couriers.Trackon, null);

        Assert.False(result.ok);
        Assert.Empty(router.Sent);
    }

    // ── TEST 11 — exactly one message, To + CC ──────────────────────────────────────────────────

    [Fact]
    public async Task Test11_SuccessfulDispatch_SendsExactlyOneMessageWithToAndCc()
    {
        var (router, ok) = await DispatchWithCcAsync("staff1@riocommerce.com, staff2@riocommerce.com");

        Assert.True(ok);

        // One message, not three — CC recipients must not become separate sends.
        var msg = Assert.Single(router.Sent);
        Assert.Equal("student@example.com", msg.ToEmail);
        Assert.Equal(2, msg.Cc!.Count);

        // …and the CC copy is the same content, by construction: one Subject, one Body.
        Assert.Contains("RIO-9100", msg.Subject);
        Assert.Contains("Test Student", msg.Body);
    }

    // ── TEST 12 — transport failure ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test12_SmtpFailure_LeavesTheDispatchIntact()
    {
        using var db = NewDb();
        var router = new CapturingRouter { FailSends = true };
        await SeedTemplateAsync(db, "staff1@riocommerce.com");
        var order = await SeedOrderAsync(db);

        var result = await DispatchAsync(Ops(db, Notifications(db, router)), order.Id, Couriers.Trackon, "ABC123456");

        Assert.True(result.ok, result.error);
        var shipment = await db.Shipments.AsNoTracking().FirstAsync(s => s.OrderId == order.Id);
        Assert.Equal(ShipmentStatus.Dispatched, shipment.Status);
        Assert.NotNull(shipment.DispatchedAt);
    }

    // ── TEST 13 — PCMC: CC still gets the mail, tracking block still empty ──────────────────────

    [Fact]
    public async Task Test13_PcmcDispatch_CopiesCc_AndKeepsTheTrackingBlockEmpty()
    {
        var (router, ok) = await DispatchWithCcAsync(
            "staff1@riocommerce.com, staff2@riocommerce.com", Couriers.Pcmc, tracking: null);

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal(2, msg.Cc!.Count);
        Assert.Contains($"Courier: {Couriers.Pcmc}", msg.Body);
        Assert.DoesNotContain("Tracking Number:", msg.Body);
        Assert.DoesNotContain("Track your shipment", msg.Body);
    }

    // ── TEST 14 / 15 — tracking couriers ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Couriers.Trackon, "ABC123456", "https://www.trackon.in/courier-tracking")]      // TEST 14
    [InlineData(Couriers.IndiaPost, "EN123456789IN", "https://www.indiapost.gov.in/tracking")]  // TEST 15
    public async Task Test14And15_TrackingCourier_CopiesCcAndIncludesTrackingDetails(
        string courier, string tracking, string url)
    {
        var (router, ok) = await DispatchWithCcAsync(
            "staff1@riocommerce.com, staff2@riocommerce.com", courier, tracking);

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal(2, msg.Cc!.Count);
        Assert.Contains($"Courier: {courier}", msg.Body);
        Assert.Contains(tracking, msg.Body);
        Assert.Contains(url, msg.Body);
    }

    // ── TEST 16 — templates that predate the CC column ──────────────────────────────────────────

    [Fact]
    public async Task Test16_TemplateWithoutCc_BehavesExactlyAsBefore()
    {
        using var db = NewDb();
        var router = new CapturingRouter();

        // An unrelated template as it exists in production today: CcEmails never set.
        db.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(),
            Key = "order_confirmation",
            Name = "Order — Confirmation (Email)",
            Channel = "Email",
            Subject = "Order {{order_number}} confirmed",
            Body = "Hi {{name}}, your order {{order_number}} is confirmed.",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var (ok, error, channels) = await Notifications(db, router).SendAsync(
            "order_confirmation",
            new NotificationRecipient(Email: "student@example.com"),
            new Dictionary<string, string> { ["name"] = "Test Student", ["order_number"] = "RIO-9100" });

        Assert.True(ok, error);
        Assert.Equal(1, channels);
        var msg = Assert.Single(router.Sent);
        Assert.Equal("student@example.com", msg.ToEmail);
        Assert.Empty(msg.Cc ?? Array.Empty<string>());
        Assert.Equal("Order RIO-9100 confirmed", msg.Subject);
    }

    // ── Storage rules ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingATemplate_StoresCcNormalised_AndOnlyForTheEmailChannel()
    {
        using var db = NewDb();
        var svc = Notifications(db, new CapturingRouter());

        var (ok, error, emailId) = await svc.SaveTemplateAsync(new MessageTemplateEditModel
        {
            Key = "order_dispatched", Name = "Order — Dispatched (Email)", Channel = "Email",
            Subject = "s", Body = "b", CcEmails = "  STAFF1@riocommerce.com ,  staff2@riocommerce.com  ",
        });
        Assert.True(ok, error);
        Assert.Equal("STAFF1@riocommerce.com, staff2@riocommerce.com",
            (await db.MessageTemplates.FirstAsync(t => t.Id == emailId)).CcEmails);

        // An SMS template has no CC header to put anything on, so nothing is stored.
        var (smsOk, smsError, smsId) = await svc.SaveTemplateAsync(new MessageTemplateEditModel
        {
            Key = "order_dispatched", Name = "Order — Dispatched (SMS)", Channel = "SMS",
            Subject = "s", Body = "b", CcEmails = "staff1@riocommerce.com",
        });
        Assert.True(smsOk, smsError);
        Assert.Null((await db.MessageTemplates.FirstAsync(t => t.Id == smsId)).CcEmails);
    }

    [Fact]
    public async Task EditingATemplate_RoundTripsTheCcValue()
    {
        using var db = NewDb();
        var svc = Notifications(db, new CapturingRouter());
        var (_, _, id) = await svc.SaveTemplateAsync(new MessageTemplateEditModel
        {
            Key = "order_dispatched", Name = "Order — Dispatched (Email)", Channel = "Email",
            Subject = "s", Body = "b", CcEmails = "staff1@riocommerce.com",
        });

        var loaded = await svc.GetTemplateAsync(id);

        Assert.Equal("staff1@riocommerce.com", loaded!.CcEmails);
    }

    /// <summary>A template saved before validation existed must not block the customer's email.</summary>
    [Fact]
    public async Task StoredCcThatIsInvalid_IsDroppedRatherThanFailingTheSend()
    {
        var (router, ok) = await DispatchWithCcAsync("a@x.com,b@x.com,c@x.com,d@x.com");

        Assert.True(ok);
        var msg = Assert.Single(router.Sent);
        Assert.Equal("student@example.com", msg.ToEmail);
        Assert.Empty(msg.Cc ?? Array.Empty<string>());
    }
}
