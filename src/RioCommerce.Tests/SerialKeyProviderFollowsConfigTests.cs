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
/// Re-issuing a key after the product has been moved to a different vendor.
///
/// <para>The vendor was fixed on the record when the key was first queued, so Regenerate went back to
/// the OLD vendor no matter what the product page said — a product switched from Valence to Superclass
/// kept producing Valence keys, with nothing anywhere reporting a problem.</para>
/// </summary>
public class SerialKeyProviderFollowsConfigTests
{
    private sealed class SpyProvider : ISerialKeyProvider
    {
        public SpyProvider(string key) => Key = key;
        public string Key { get; }
        public string DisplayName => Key;
        public bool IssuesSerialKey => true;
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;

        public int Calls { get; private set; }
        public List<GenerateKeyRequest> Seen { get; } = new();

        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest request, CancellationToken ct)
        {
            Calls++;
            Seen.Add(request);
            return Task.FromResult(new GenerateKeyResult
            {
                Success = true, SerialKey = $"{Key.ToUpperInvariant()}-KEY-{Calls}", Activated = true,
                RawRequest = "{}", RawResponse = """{"ok":true}""",
            });
        }

        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"provfollow-{Guid.NewGuid()}")
            .Options);

    private static SerialKeyService Svc(RioCommerceDbContext db, params ISerialKeyProvider[] providers)
    {
        // A Valence record asserts its product↔pack mapping before registering, and refuses to
        // register if it does not land. Left at Moq's default the tuple is (false, null), so the
        // Valence provider would never be reached and these tests would "pass" for the wrong reason.
        var packs = new Mock<IValencePackService>();
        packs.Setup(p => p.MapProductToPackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                                                 It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((true, (string?)null));

        return new SerialKeyService(db,
            new SerialKeyProviderFactory(providers),
            new Mock<INotificationService>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            packs.Object,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);
    }

    /// <summary>
    /// RIO-1055 as it stands: a Valence key already issued for the LAW product, which has since been
    /// mapped to Superclass — including a per-mode override on the very mode that was purchased.
    /// </summary>
    private static async Task<Guid> SeedAsync(
        RioCommerceDbContext db, string recordProvider, string configProvider,
        bool configOnPurchasedMode = true, string? existingKey = "IHK3QAXIMOPE")
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        var modeId = Guid.NewGuid();

        db.Products.Add(new Product { Id = productId, Title = "CA Foundation LAW Regular", Slug = $"p-{productId:N}" });
        db.Set<ProductMode>().Add(new ProductMode
        {
            Id = modeId, ProductId = productId, ModeName = "Recorded Lectures + Hardcopy Notes",
        });
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "RIO-1055", UserId = Guid.NewGuid(),
            StudentName = "ISHA BHALEGHARE", StudentPhone = "9876500000", StudentEmail = "isha@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m, BillingPincode = "411001",
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = orderItemId, OrderId = orderId, ProductId = productId, ProductModeId = modeId,
            ProductTitle = "CA Foundation LAW Regular", Quantity = 1, UnitPrice = 1000m, LineTotal = 1000m,
        });

        var cfgJson = configProvider == "superclass"
            ? JsonSerializer.Serialize(new { CourseId = 2787, ViewType = 1, Value = 2, ValidityType = 1, ValidityDays = 270 })
            : JsonSerializer.Serialize(new { PackId = 570, BaseUrl = "https://x/index.php", PathSegment = "seg", ClassId = "5702" });

        db.Set<ProductSerialKeyConfig>().Add(new ProductSerialKeyConfig
        {
            Id = Guid.NewGuid(), ProductId = productId,
            ProductModeId = configOnPurchasedMode ? modeId : null,
            ProviderKey = configProvider,
            ProviderProductCode = configProvider == "superclass" ? "2787" : productId.ToString(),
            ConfigJson = cfgJson, IsActive = true,
        });

        var req = new GenerateKeyRequest
        {
            ProviderKey = recordProvider, OrderId = orderId, OrderItemId = orderItemId, OrderNumber = "RIO-1055",
            ProductId = productId, ProductTitle = "CA Foundation LAW Regular",
            ProviderProductCode = productId.ToString(),
            CustomerName = "ISHA BHALEGHARE", CustomerEmail = "isha@example.com",
            CustomerPhone = "9876500000", CustomerCountryCode = "91", CustomerPincode = "411001",
            ConfigJson = "{}",
        };
        var recordId = Guid.NewGuid();
        db.Set<SerialKeyRecord>().Add(new SerialKeyRecord
        {
            Id = recordId, OrderId = orderId, OrderItemId = orderItemId, ProductId = productId,
            ProviderKey = recordProvider,
            SerialKey = existingKey,
            Status = SerialKeyStatus.Pending,          // what Regenerate leaves behind
            RequestPayload = JsonSerializer.Serialize(req), NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
        });

        await db.SaveChangesAsync();
        return recordId;
    }

    // ── The reported need ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegeneratingAfterAProviderSwitch_GoesToTheNewVendor()
    {
        using var db = NewDb();
        var valence = new SpyProvider("valence");
        var superclass = new SpyProvider("superclass");
        var recordId = await SeedAsync(db, recordProvider: "valence", configProvider: "superclass");

        await Svc(db, valence, superclass).ProcessDueAsync(10);

        Assert.Equal(0, valence.Calls);            // the old vendor is not called at all
        Assert.Equal(1, superclass.Calls);

        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId);
        Assert.Equal("superclass", rec.ProviderKey);
        Assert.StartsWith("SUPERCLASS-KEY", rec.SerialKey);   // the stale Valence key is gone
    }

    [Fact]
    public async Task TheNewVendorGetsTheNewConfig()
    {
        using var db = NewDb();
        var superclass = new SpyProvider("superclass");
        await SeedAsync(db, recordProvider: "valence", configProvider: "superclass");

        await Svc(db, new SpyProvider("valence"), superclass).ProcessDueAsync(10);

        var sent = Assert.Single(superclass.Seen);
        Assert.Equal("superclass", sent.ProviderKey);
        Assert.Equal("2787", sent.ProviderProductCode);
        Assert.Contains("2787", sent.ConfigJson);
    }

    [Fact]
    public async Task AProductLevelSwitchIsFollowedToo_NotJustAModeOverride()
    {
        using var db = NewDb();
        var superclass = new SpyProvider("superclass");
        await SeedAsync(db, recordProvider: "valence", configProvider: "superclass", configOnPurchasedMode: false);

        await Svc(db, new SpyProvider("valence"), superclass).ProcessDueAsync(10);

        Assert.Equal(1, superclass.Calls);
    }

    // ── Nothing changes when nothing changed ────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheProviderIsUnchanged_TheRecordIsLeftAlone()
    {
        using var db = NewDb();
        var valence = new SpyProvider("valence");
        var recordId = await SeedAsync(db, recordProvider: "valence", configProvider: "valence");

        await Svc(db, valence, new SpyProvider("superclass")).ProcessDueAsync(10);

        Assert.Equal(1, valence.Calls);
        Assert.Equal("valence", (await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId)).ProviderKey);
    }

    /// <summary>An unregistered vendor name must not strand the record on a provider that cannot run.</summary>
    [Fact]
    public async Task AnUnknownProviderInTheConfig_IsIgnoredRatherThanApplied()
    {
        using var db = NewDb();
        var valence = new SpyProvider("valence");
        var recordId = await SeedAsync(db, recordProvider: "valence", configProvider: "someothervendor");

        await Svc(db, valence).ProcessDueAsync(10);

        // Falls back to the vendor the record already names, which IS registered.
        Assert.Equal(1, valence.Calls);
        Assert.Equal("valence", (await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId)).ProviderKey);
    }
}
