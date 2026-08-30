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
/// Retrying a failed serial key after the order has been corrected.
///
/// <para>The dispatcher re-read the product CONFIG before each attempt but never the CUSTOMER, so the
/// enqueue-time snapshot was used for ever. A vendor rejecting the order for a missing pincode could
/// therefore never be satisfied: filling the pincode in on the order changed nothing, and Retry
/// re-sent the same incomplete payload. Superclass returns exactly that —
/// <c>403 "The Pincode field is required."</c></para>
/// </summary>
public class SerialKeyCustomerRefreshTests
{
    /// <summary>Records the request it was handed, and can be made to fail like the vendor does.</summary>
    private sealed class SpyProvider : ISerialKeyProvider
    {
        public string Key => "superclass";
        public string DisplayName => "Superclass (spy)";
        public bool IssuesSerialKey => false;
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;

        public List<GenerateKeyRequest> Seen { get; } = new();
        public bool RejectWhenPincodeMissing { get; set; } = true;

        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest request, CancellationToken ct)
        {
            Seen.Add(request);
            if (RejectWhenPincodeMissing && string.IsNullOrWhiteSpace(request.CustomerPincode))
                return Task.FromResult(GenerateKeyResult.Fail("superclass_403", "Validation Error."));

            return Task.FromResult(new GenerateKeyResult
            {
                Success = true, ExternalReference = "student-1", Activated = true,
                RawRequest = "{}", RawResponse = """{"status":200}""",
            });
        }

        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"skrefresh-{Guid.NewGuid()}")
            .Options);

    private static SerialKeyService Svc(RioCommerceDbContext db, SpyProvider provider) =>
        new(db,
            new SerialKeyProviderFactory(new ISerialKeyProvider[] { provider }),
            new Mock<INotificationService>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            new Mock<IValencePackService>().Object,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);

    /// <summary>RIO-1059 as it stood: queued while the order had no pincode.</summary>
    private static async Task<(Guid OrderId, Guid RecordId)> SeedAsync(
        RioCommerceDbContext db, string? pincodeAtEnqueue, string? pincodeOnOrderNow,
        string? emailNow = "anushkasable2@gmail.com")
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        db.Products.Add(new Product { Id = productId, Title = "Beginner Economics Regular", Slug = $"p-{productId:N}" });
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "RIO-1059", UserId = Guid.NewGuid(),
            StudentName = "Anushka Sable", StudentPhone = "9359427260", StudentEmail = emailNow,
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m,
            BillingPincode = pincodeOnOrderNow,
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = orderItemId, OrderId = orderId, ProductId = productId,
            ProductTitle = "Beginner Economics Regular", Quantity = 1,
            UnitPrice = 1000m, LineTotal = 1000m,
        });

        var cfgJson = JsonSerializer.Serialize(new
        {
            CourseId = 2786, ViewType = 1, Value = 2, ValidityType = 1, ValidityDays = 270, ClassIdOverride = 318,
        });
        db.Set<ProductSerialKeyConfig>().Add(new ProductSerialKeyConfig
        {
            Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "superclass",
            ProviderProductCode = "2786", ConfigJson = cfgJson, IsActive = true,
        });

        // The snapshot as it was written at enqueue time — pincode as it was THEN.
        var req = new GenerateKeyRequest
        {
            ProviderKey = "superclass", OrderId = orderId, OrderItemId = orderItemId, OrderNumber = "RIO-1059",
            ProductId = productId, ProductTitle = "Beginner Economics Regular", ProviderProductCode = "2786",
            CustomerName = "Anushka Sable", CustomerEmail = "anushkasable2@gmail.com",
            CustomerPhone = "9359427260", CustomerCountryCode = "91",
            CustomerPincode = pincodeAtEnqueue,
            ConfigJson = cfgJson,
        };
        var recordId = Guid.NewGuid();
        db.Set<SerialKeyRecord>().Add(new SerialKeyRecord
        {
            Id = recordId, OrderId = orderId, OrderItemId = orderItemId, ProductId = productId,
            ProviderKey = "superclass", Status = SerialKeyStatus.Failed,
            ErrorCode = "superclass_403", ErrorMessage = "Validation Error.",
            RequestPayload = JsonSerializer.Serialize(req), NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
        });

        await db.SaveChangesAsync();
        return (orderId, recordId);
    }

    // ── The reported failure ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APincodeAddedAfterEnqueue_ReachesTheVendorOnRetry()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var (_, recordId) = await SeedAsync(db, pincodeAtEnqueue: null, pincodeOnOrderNow: "411001");

        await Svc(db, provider).ProcessDueAsync(10);

        var sent = Assert.Single(provider.Seen);
        Assert.Equal("411001", sent.CustomerPincode);      // was "" before — the vendor rejected it

        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId);
        Assert.Null(rec.ErrorCode);
        Assert.Equal(SerialKeyStatus.Activated, rec.Status);
    }

    [Fact]
    public async Task WithoutTheCorrection_TheVendorStillRejectsIt()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var (_, recordId) = await SeedAsync(db, pincodeAtEnqueue: null, pincodeOnOrderNow: null);

        await Svc(db, provider).ProcessDueAsync(10);

        Assert.True(string.IsNullOrWhiteSpace(Assert.Single(provider.Seen).CustomerPincode));
        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId);
        Assert.Equal("superclass_403", rec.ErrorCode);
    }

    // ── The other customer fields behave the same way ───────────────────────────────────────────

    [Fact]
    public async Task ACorrectedEmail_AlsoReachesTheVendor()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        await SeedAsync(db, pincodeAtEnqueue: "411001", pincodeOnOrderNow: "411001",
                        emailNow: "corrected@example.com");

        await Svc(db, provider).ProcessDueAsync(10);

        Assert.Equal("corrected@example.com", Assert.Single(provider.Seen).CustomerEmail);
    }

    [Fact]
    public async Task ShippingPincodeIsUsedWhenBillingHasNone()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var (orderId, _) = await SeedAsync(db, pincodeAtEnqueue: null, pincodeOnOrderNow: null);

        var order = await db.Orders.FirstAsync(o => o.Id == orderId);
        order.ShippingPincode = "442001";
        await db.SaveChangesAsync();

        await Svc(db, provider).ProcessDueAsync(10);

        Assert.Equal("442001", Assert.Single(provider.Seen).CustomerPincode);
    }

    /// <summary>A field cleared on the order must not wipe a value captured correctly at checkout.</summary>
    [Fact]
    public async Task ClearingAFieldOnTheOrder_DoesNotBlankTheSnapshotValue()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var (orderId, _) = await SeedAsync(db, pincodeAtEnqueue: "411001", pincodeOnOrderNow: "411001");

        var order = await db.Orders.FirstAsync(o => o.Id == orderId);
        order.BillingPincode = null;
        order.StudentEmail = null;
        await db.SaveChangesAsync();

        await Svc(db, provider).ProcessDueAsync(10);

        var sent = Assert.Single(provider.Seen);
        Assert.Equal("411001", sent.CustomerPincode);
        Assert.Equal("anushkasable2@gmail.com", sent.CustomerEmail);
    }

    [Fact]
    public async Task TheProductConfigIsStillRefreshedToo()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var (orderId, _) = await SeedAsync(db, pincodeAtEnqueue: "411001", pincodeOnOrderNow: "411001");

        // Admin corrects the course id on the product after the key was queued.
        var productId = await db.Orders.Where(o => o.Id == orderId)
            .Join(db.OrderItems, o => o.Id, i => i.OrderId, (o, i) => i.ProductId).FirstAsync();
        var cfg = await db.Set<ProductSerialKeyConfig>().FirstAsync(c => c.ProductId == productId);
        cfg.ProviderProductCode = "9999";
        await db.SaveChangesAsync();

        await Svc(db, provider).ProcessDueAsync(10);

        Assert.Equal("9999", Assert.Single(provider.Seen).ProviderProductCode);
    }
}
