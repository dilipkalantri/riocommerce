using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.SerialKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// Resending the "your serial key is ready" email from the admin order page.
///
/// <para>No provider is called and no key changes — this only re-sends what the customer already
/// holds. The tests therefore concentrate on which records are eligible: a revoked key must never go
/// out again, a pending record has no key to quote, and a registration-style provider mails the
/// customer itself.</para>
/// </summary>
public class SerialKeyResendEmailTests
{
    /// <summary>Key-issuing provider, like Valence and RioPlay.</summary>
    private sealed class KeyProvider : ISerialKeyProvider
    {
        public string Key => "valence";
        public string DisplayName => "Valence";
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;
        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>Registration-style provider — issues no key of ours and mails the student directly.</summary>
    private sealed class RegistrationProvider : ISerialKeyProvider
    {
        public string Key => "superclass";
        public string DisplayName => "Superclass";
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;
        public bool IssuesSerialKey => false;
        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"keyresend-{Guid.NewGuid()}")
            .Options);

    private static SerialKeyService Svc(RioCommerceDbContext db, Mock<INotificationService> notify) =>
        new(db,
            new SerialKeyProviderFactory(new ISerialKeyProvider[] { new KeyProvider(), new RegistrationProvider() }),
            notify.Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            new Mock<IValencePackService>().Object,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);

    /// <summary>A notify mock that reports a real send (ok, one channel).</summary>
    private static Mock<INotificationService> Notifier()
    {
        var m = new Mock<INotificationService>();
        m.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                 It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
         .ReturnsAsync((true, (string?)null, 1));
        return m;
    }

    /// <summary>RIO-1063 as it stands in production: paid, one activated Valence key.</summary>
    private static async Task<(Order Order, SerialKeyRecord Rec)> SeedAsync(
        RioCommerceDbContext db,
        SerialKeyStatus status = SerialKeyStatus.Activated,
        string? key = "OD4ALFTJGUIG",
        string provider = "valence",
        PaymentStatus payment = PaymentStatus.Success,
        string? email = "moinuddinmansuri04@gmail.com",
        bool deleted = false)
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "RIO-1063", UserId = Guid.NewGuid(),
            StudentName = "Moinuddin mansuri", StudentPhone = "08369926759", StudentEmail = email,
            Status = OrderStatus.Confirmed, PaymentStatus = payment,
            Subtotal = 299m, TotalAmount = 299m, IsDeleted = deleted,
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = itemId, OrderId = orderId, ProductId = productId,
            ProductTitle = "CA Inter LAW Fastrack", Quantity = 1, UnitPrice = 299m, LineTotal = 299m,
        });

        var req = new GenerateKeyRequest
        {
            ProviderKey = provider, CustomerName = "Moinuddin mansuri",
            CustomerEmail = email, CustomerPhone = "08369926759",
            OrderId = orderId, OrderItemId = itemId, OrderNumber = "RIO-1063",
            ProductId = productId, ProductTitle = "CA Inter LAW Fastrack",
        };
        var rec = new SerialKeyRecord
        {
            Id = Guid.NewGuid(), OrderId = orderId, OrderItemId = itemId, ProductId = productId,
            ProviderKey = provider, SerialKey = key, Status = status,
            RequestPayload = JsonSerializer.Serialize(req),
            NextRetryAt = DateTime.UtcNow.AddYears(10),
        };
        db.Set<SerialKeyRecord>().Add(rec);
        await db.SaveChangesAsync();
        return (db.Orders.IgnoreQueryFilters().First(o => o.Id == orderId), rec);
    }

    // ── The thing this was built for ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnIssuedKeyIsSentAgain()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db);
        var notify = Notifier();

        var (ok, error, sent, sentTo) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.True(ok, error);
        Assert.Equal(1, sent);
        Assert.Equal("moinuddinmansuri04@gmail.com", sentTo);
        notify.Verify(n => n.SendAsync("serial_key_generated", It.IsAny<NotificationRecipient>(),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task TheMailCarriesTheRealKeyAndProduct()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db);
        var notify = Notifier();
        IDictionary<string, string>? tokens = null;
        notify.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                      It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
              .Callback<string, NotificationRecipient, IDictionary<string, string>, string?>((_, _, t, _) => tokens = t)
              .ReturnsAsync((true, (string?)null, 1));

        await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.NotNull(tokens);
        Assert.Equal("OD4ALFTJGUIG", tokens!["serial_key"]);
        Assert.Equal("CA Inter LAW Fastrack", tokens["product_title"]);
        Assert.Equal("RIO-1063", tokens["order_number"]);
        Assert.Equal("Moinuddin mansuri", tokens["name"]);
    }

    [Fact]
    public async Task ItGoesToTheOrdersCurrentAddress_NotTheOneTheKeyWasIssuedTo()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db, email: "typo@example.com");
        var tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
        tracked.StudentEmail = "corrected@example.com";     // the usual reason for a resend
        await db.SaveChangesAsync();

        var notify = Notifier();
        NotificationRecipient? to = null;
        notify.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                      It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
              .Callback<string, NotificationRecipient, IDictionary<string, string>, string?>((_, r, _, _) => to = r)
              .ReturnsAsync((true, (string?)null, 1));

        var (ok, _, _, sentTo) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.True(ok);
        Assert.Equal("corrected@example.com", sentTo);
        Assert.Equal("corrected@example.com", to!.Email);
    }

    [Fact]
    public async Task ACombosTwoKeysAreBothSent()
    {
        using var db = NewDb();
        var (order, first) = await SeedAsync(db);
        db.Set<SerialKeyRecord>().Add(new SerialKeyRecord
        {
            Id = Guid.NewGuid(), OrderId = order.Id, OrderItemId = first.OrderItemId,
            ProductId = first.ProductId, ProviderKey = "valence",
            SerialKey = "E0VTUPCKWSTA", Status = SerialKeyStatus.Activated,
            RequestPayload = first.RequestPayload, NextRetryAt = DateTime.UtcNow.AddYears(10),
        });
        await db.SaveChangesAsync();
        var notify = Notifier();

        var (ok, _, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.True(ok);
        Assert.Equal(2, sent);   // one mail per key — a combo buyer gets both subjects
    }

    [Fact]
    public async Task OneKeyCanBeTargetedOnItsOwn()
    {
        using var db = NewDb();
        var (order, first) = await SeedAsync(db);
        db.Set<SerialKeyRecord>().Add(new SerialKeyRecord
        {
            Id = Guid.NewGuid(), OrderId = order.Id, OrderItemId = first.OrderItemId,
            ProductId = first.ProductId, ProviderKey = "valence",
            SerialKey = "E0VTUPCKWSTA", Status = SerialKeyStatus.Activated,
            RequestPayload = first.RequestPayload, NextRetryAt = DateTime.UtcNow.AddYears(10),
        });
        await db.SaveChangesAsync();
        var notify = Notifier();

        var (ok, _, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, first.Id, Guid.NewGuid());

        Assert.True(ok);
        Assert.Equal(1, sent);
    }

    // ── When it must refuse ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARevokedKeyIsNeverResent()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db, status: SerialKeyStatus.Revoked);
        var notify = Notifier();

        var (ok, error, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("revoked", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, sent);
        // A revoked key was deliberately invalidated — mailing it again would hand back access.
        notify.Verify(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(SerialKeyStatus.Pending)]
    [InlineData(SerialKeyStatus.Failed)]
    [InlineData(SerialKeyStatus.DeadLettered)]
    public async Task ARecordWithNoUsableKeyIsRefused(SerialKeyStatus status)
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db, status: status, key: null);
        var notify = Notifier();

        var (ok, error, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("no key", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, sent);
        // Otherwise the customer receives a mail with an empty {{serial_key}}.
        notify.Verify(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ARegistrationStyleProviderHasNothingToResend()
    {
        using var db = NewDb();
        // Superclass mails its own welcome message and issues no key of ours.
        var (order, _) = await SeedAsync(db, provider: "superclass", key: "ref-12345");
        var notify = Notifier();

        var (ok, error, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("directly", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, sent);
        notify.Verify(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    public async Task AnUnpaidOrderIsRefused(PaymentStatus payment)
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db, payment: payment);
        var notify = Notifier();

        var (ok, error, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("paid", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, sent);
        notify.Verify(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task AnOrderWithNoEmailAddressIsRefused()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db, email: null);
        var notify = Notifier();

        var (ok, error, _, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("email", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnOrderInTheRecycleBinIsRefused()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db, deleted: true);
        var notify = Notifier();

        var (ok, error, _, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("recycle bin", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnOrderWithNoKeyRecordsAtAllIsRefused()
    {
        using var db = NewDb();
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "RIO-9999", StudentName = "X",
            StudentPhone = "9", StudentEmail = "x@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
        });
        await db.SaveChangesAsync();
        var notify = Notifier();

        var (ok, error, _, _) = await Svc(db, notify).ResendKeyEmailAsync(orderId, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("no serial key records", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── A send failure must not read as success ─────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheMailProviderFails_TheAdminIsToldItFailed()
    {
        using var db = NewDb();
        var (order, rec) = await SeedAsync(db);
        var notify = new Mock<INotificationService>();
        notify.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                      It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
              .ThrowsAsync(new HttpRequestException("SMTP unreachable"));

        var (ok, error, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal(0, sent);
        Assert.DoesNotContain("SMTP", error);   // never the raw exception

        // NotifiedAt must not move — it means "the customer has been told", not "we tried".
        Assert.Null((await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == rec.Id)).NotifiedAt);
    }

    [Fact]
    public async Task NoChannelSentCountsAsAFailure()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db);
        var notify = new Mock<INotificationService>();
        // Template inactive / no destination: reports ok but nothing actually went out.
        notify.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                      It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
              .ReturnsAsync((true, (string?)null, 0));

        var (ok, _, sent, _) = await Svc(db, notify).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task ASuccessfulResendStampsNotifiedAt()
    {
        using var db = NewDb();
        var (order, rec) = await SeedAsync(db);

        await Svc(db, Notifier()).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        Assert.NotNull((await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == rec.Id)).NotifiedAt);
    }

    [Fact]
    public async Task ResendingDoesNotTouchTheKeyOrItsStatus()
    {
        using var db = NewDb();
        var (order, rec) = await SeedAsync(db);

        await Svc(db, Notifier()).ResendKeyEmailAsync(order.Id, null, Guid.NewGuid());

        var after = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == rec.Id);
        Assert.Equal("OD4ALFTJGUIG", after.SerialKey);
        Assert.Equal(SerialKeyStatus.Activated, after.Status);
        Assert.Equal(0, after.AttemptCount);   // no provider call was made
    }
}
