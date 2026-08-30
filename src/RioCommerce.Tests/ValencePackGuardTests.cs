using System.Net;
using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.SerialKeys;
using RioCommerce.Infrastructure.Services.SerialKeys.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The rule these tests exist to hold: <b>no pack configured = no serial key</b>.
///
/// <para>Valence's registration call sends only <c>course</c> — never a pack — so if the pack for the
/// purchased product is missing, Valence still returns a perfectly valid key, just for whatever pack it
/// resolves. That is silent, customer-visible, and irreversible once emailed. These tests assert the
/// registration API is <b>never reached</b> in that state, by counting calls on a fake provider rather
/// than only inspecting the resulting error.</para>
/// </summary>
public class ValencePackGuardTests
{
    private const string ValenceBase = "https://edubeessecurelms.com/edubeessecurelms/index.php";
    private const string ValencePath = "secretsegment";
    private const string SharedClassId = "5702";   // shared by every real Valence product — identifies nothing

    // ── Test doubles ────────────────────────────────────────────────────────────────────────────

    /// <summary>Stands in for Valence's registration endpoint and records whether it was reached.</summary>
    private sealed class SpyValenceProvider : ISerialKeyProvider
    {
        public string Key => "valence";
        public string DisplayName => "Valence (spy)";
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;

        public int CallCount { get; private set; }
        public List<GenerateKeyRequest> Requests { get; } = new();

        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest request, CancellationToken ct)
        {
            CallCount++;
            Requests.Add(request);
            return Task.FromResult(new GenerateKeyResult
            {
                Success = true,
                SerialKey = $"KEY{CallCount:D8}",
                Activated = true,
                RawRequest = "{}",
                RawResponse = """{"key":"x","status":"success","message":"Student registered"}""",
            });
        }

        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct)
            => throw new NotSupportedException();
    }

    /// <summary>Records every save_product_pack the guard asks for, and can be made to refuse.</summary>
    private sealed class SpyPackService : IValencePackService
    {
        public List<(string CourseId, int PackId)> Mapped { get; } = new();
        public bool FailMapping { get; set; }

        public Task<(bool ok, string? error)> MapProductToPackAsync(
            string baseUrl, string pathSegment, string courseId, string productName, int packExternalId,
            CancellationToken ct = default)
        {
            Mapped.Add((courseId, packExternalId));
            return Task.FromResult(FailMapping
                ? (false, (string?)"Valence returned HTTP 404 mapping the product.")
                : (true, (string?)null));
        }

        public Task<List<ValencePackItem>> ListAsync(bool activeOnly = true, CancellationToken ct = default)
            => Task.FromResult(new List<ValencePackItem>());
        public Task<ValencePackSyncResult> SyncAsync(string? b, string? p, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Returns one canned HTTP response — used to test the real "already exists" translation.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public CannedHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }

    private sealed class CannedFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public CannedFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"packguard-{Guid.NewGuid()}")
            .Options);

    private static SerialKeyService Svc(RioCommerceDbContext db, SpyValenceProvider provider, SpyPackService packs) =>
        new(db,
            new SerialKeyProviderFactory(new ISerialKeyProvider[] { provider }),
            new Mock<INotificationService>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            packs,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);

    private static string ConfigJson(int? packId, string? packIdsCsv = null) =>
        JsonSerializer.Serialize(new ValenceProductConfig
        {
            BaseUrl = ValenceBase,
            PathSegment = ValencePath,
            ClassId = SharedClassId,
            KeyViews = 11,
            PackId = packId,
            PackIdsCsv = packIdsCsv,
        }, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

    /// <summary>Seeds a product + order + a Pending serial-key record exactly as enqueue would.</summary>
    private static async Task<(Guid ProductId, Guid RecordId)> SeedPendingAsync(
        RioCommerceDbContext db, int? packId, string title = "CA Inter Audit Fastrack", string orderNo = "RIO-9200")
    {
        var productId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();

        db.Products.Add(new Product { Id = productId, Title = title, Slug = $"p-{productId:N}" });
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = orderNo, UserId = Guid.NewGuid(),
            StudentName = "Test Student", StudentPhone = "9876500000", StudentEmail = "s@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = orderItemId, OrderId = orderId, ProductId = productId,
            ProductTitle = title, Quantity = 1, UnitPrice = 1000m, LineTotal = 1000m,
        });

        var cfgJson = ConfigJson(packId);
        db.Set<ProductSerialKeyConfig>().Add(new ProductSerialKeyConfig
        {
            Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "valence",
            ProviderProductCode = productId.ToString(), ConfigJson = cfgJson, IsActive = true,
        });

        var req = new GenerateKeyRequest
        {
            ProviderKey = "valence", OrderId = orderId, OrderItemId = orderItemId, OrderNumber = orderNo,
            ProductId = productId, ProductTitle = title, ProviderProductCode = productId.ToString(),
            CustomerName = "Test Student", CustomerEmail = "s@example.com", CustomerPhone = "9876500000",
            ConfigJson = cfgJson,
        };
        var recordId = Guid.NewGuid();
        db.Set<SerialKeyRecord>().Add(new SerialKeyRecord
        {
            Id = recordId, OrderId = orderId, OrderItemId = orderItemId, ProductId = productId,
            ProviderKey = "valence", Status = SerialKeyStatus.Pending,
            RequestPayload = JsonSerializer.Serialize(req), NextRetryAt = DateTime.UtcNow.AddMinutes(-1),
        });

        await db.SaveChangesAsync();
        return (productId, recordId);
    }

    // ── TEST 1 / 9 — a correctly configured pack maps, then registers ───────────────────────────

    [Fact]
    public async Task Test1And9_PackConfigured_MapsThenRegisters_AndStoresTheKey()
    {
        using var db = NewDb();
        var provider = new SpyValenceProvider();
        var packs = new SpyPackService();
        var (productId, recordId) = await SeedPendingAsync(db, packId: 449);

        await Svc(db, provider, packs).ProcessDueAsync(10);

        // mapping asserted for THIS product and THIS pack, before registration
        Assert.Equal(new[] { (productId.ToString(), 449) }, packs.Mapped);
        Assert.Equal(1, provider.CallCount);

        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId);
        Assert.Equal(SerialKeyStatus.Activated, rec.Status);
        Assert.False(string.IsNullOrWhiteSpace(rec.SerialKey));
        Assert.Null(rec.ErrorCode);
    }

    // ── TEST 2 / 3 / 4 / 10 — no usable pack ⇒ registration never happens ───────────────────────

    [Theory]
    [InlineData(null)]  // TEST 2  — pack absent (the live production state that caused this)
    [InlineData(0)]     // TEST 3
    [InlineData(-1)]    // TEST 4
    public async Task Test2To4And10_NoUsablePack_BlocksRegistrationEntirely(int? packId)
    {
        using var db = NewDb();
        var provider = new SpyValenceProvider();
        var packs = new SpyPackService();
        var (_, recordId) = await SeedPendingAsync(db, packId);

        var (_, generated, failed, _) = await Svc(db, provider, packs).ProcessDueAsync(10);

        // The registration API must NOT be called — this is the assertion that matters.
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(packs.Mapped);          // nothing to map, so nothing was pushed either
        Assert.Equal(0, generated);
        Assert.Equal(1, failed);

        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId);
        Assert.Equal("no_pack_configured", rec.ErrorCode);
        Assert.Null(rec.SerialKey);
        Assert.Null(rec.GeneratedAt);
        Assert.Contains("pack", rec.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        // Retryable, not lost: fixing the config and retrying is the documented remedy.
        Assert.True(rec.Status is SerialKeyStatus.Failed or SerialKeyStatus.DeadLettered);
    }

    // ── TEST 7 — mapping refused ⇒ registration blocked ─────────────────────────────────────────

    [Fact]
    public async Task Test7_MappingFails_BlocksRegistration()
    {
        using var db = NewDb();
        var provider = new SpyValenceProvider();
        var packs = new SpyPackService { FailMapping = true };
        var (_, recordId) = await SeedPendingAsync(db, packId: 449);

        await Svc(db, provider, packs).ProcessDueAsync(10);

        Assert.Single(packs.Mapped);          // we tried
        Assert.Equal(0, provider.CallCount);  // …and refused to register when it did not land

        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == recordId);
        Assert.Equal("pack_mapping_unconfirmed", rec.ErrorCode);
        Assert.Null(rec.SerialKey);
    }

    // ── TEST 8 — "This combination already exists" is a successful mapping ──────────────────────
    // Exercises the REAL ValencePackService translation (not a stub), because that idempotency is
    // what makes asserting the mapping on every single generation affordable.

    [Fact]
    public async Task Test8_AlreadyExists_CountsAsSuccessfulMapping()
    {
        using var db = NewDb();
        var svc = new ValencePackService(
            db,
            new CannedFactory(new CannedHandler(HttpStatusCode.OK,
                """{"status":"error","message":"This combination already exists"}""")),
            NullLogger<ValencePackService>.Instance);

        var (ok, error) = await svc.MapProductToPackAsync(
            ValenceBase, ValencePath, Guid.NewGuid().ToString(), "CA Inter Audit Fastrack", 449);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public async Task Test8b_GenuineMappingRejection_IsNotTreatedAsSuccess()
    {
        using var db = NewDb();
        var svc = new ValencePackService(
            db,
            new CannedFactory(new CannedHandler(HttpStatusCode.OK,
                """{"status":"error","message":"Invalid pack"}""")),
            NullLogger<ValencePackService>.Instance);

        var (ok, error) = await svc.MapProductToPackAsync(
            ValenceBase, ValencePath, Guid.NewGuid().ToString(), "CA Inter Audit Fastrack", 449);

        Assert.False(ok);
        Assert.Equal("Invalid pack", error);
    }

    [Fact]
    public async Task Test8c_Http404_IsNotTreatedAsSuccess()
    {
        using var db = NewDb();
        var svc = new ValencePackService(
            db,
            new CannedFactory(new CannedHandler(HttpStatusCode.NotFound, "")),
            NullLogger<ValencePackService>.Instance);

        var (ok, error) = await svc.MapProductToPackAsync(
            ValenceBase, ValencePath, Guid.NewGuid().ToString(), "CA Inter Audit Fastrack", 449);

        Assert.False(ok);
        Assert.Contains("404", error!);
    }

    // ── TEST 11 / 12 — each OrderItem uses ITS OWN pack, never a shared ClassId ─────────────────

    [Fact]
    public async Task Test11And12_TwoProducts_EachUsesItsOwnPack_DespiteSharingClassId()
    {
        using var db = NewDb();
        var provider = new SpyValenceProvider();
        var packs = new SpyPackService();

        var (productA, _) = await SeedPendingAsync(db, packId: 449, title: "CA Inter Audit Fastrack",  orderNo: "RIO-9301");
        var (productB, _) = await SeedPendingAsync(db, packId: 450, title: "CA Inter Costing Fastrack", orderNo: "RIO-9302");

        await Svc(db, provider, packs).ProcessDueAsync(10);

        // Both products carry the identical ClassId 5702, so if ClassId had any say in pack selection
        // these two would be indistinguishable. They are not: each maps its own pack.
        Assert.Equal(2, packs.Mapped.Count);
        Assert.Contains((productA.ToString(), 449), packs.Mapped);
        Assert.Contains((productB.ToString(), 450), packs.Mapped);
        Assert.DoesNotContain((productA.ToString(), 450), packs.Mapped);
        Assert.DoesNotContain((productB.ToString(), 449), packs.Mapped);

        // And the course each registration sends is that item's own product id.
        Assert.Equal(2, provider.CallCount);
        Assert.Contains(provider.Requests, r => r.ProviderProductCode == productA.ToString());
        Assert.Contains(provider.Requests, r => r.ProviderProductCode == productB.ToString());
    }

    /// <summary>One blocked product must not stop a correctly configured one in the same batch.</summary>
    [Fact]
    public async Task BlockedProduct_DoesNotBlockAWellConfiguredOne()
    {
        using var db = NewDb();
        var provider = new SpyValenceProvider();
        var packs = new SpyPackService();

        var (goodProduct, goodRec) = await SeedPendingAsync(db, packId: 449, orderNo: "RIO-9401");
        var (_, badRec) = await SeedPendingAsync(db, packId: null, title: "COMBO with no pack", orderNo: "RIO-9402");

        await Svc(db, provider, packs).ProcessDueAsync(10);

        Assert.Equal(new[] { (goodProduct.ToString(), 449) }, packs.Mapped);
        Assert.Equal(1, provider.CallCount);
        Assert.NotNull((await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == goodRec)).SerialKey);
        Assert.Null((await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.Id == badRec)).SerialKey);
    }

    // ── TEST 5 / 6 — save-time validation ───────────────────────────────────────────────────────

    private static async Task<(Guid ProductId, SerialKeyService Service, SpyPackService Packs)> SeedForSaveAsync(
        RioCommerceDbContext db, params int[] knownPacks)
    {
        var productId = Guid.NewGuid();
        db.Products.Add(new Product { Id = productId, Title = "CA Inter Audit", Slug = $"p-{productId:N}" });
        foreach (var p in knownPacks)
            db.ValencePacks.Add(new ValencePack
            {
                Id = Guid.NewGuid(), ExternalId = p, PackName = $"PACK{p}", IsActive = true,
                LastSyncedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var packs = new SpyPackService();
        return (productId, Svc(db, new SpyValenceProvider(), packs), packs);
    }

    private static ProductSerialKeyConfigItem SaveModel(Guid productId, int? packId, string? csv = null) => new()
    {
        ProductId = productId,
        ProviderKey = "valence",
        IsActive = true,
        AutoActivate = true,
        ValenceBaseUrl = ValenceBase,
        ValencePathSegment = ValencePath,
        ValenceClassId = SharedClassId,
        ValenceKeyViews = 11,
        ValencePackId = packId,
        ValencePackIdsCsv = csv,
    };

    [Fact]
    public async Task Test5_SavingValenceConfigWithoutPack_IsRejected()
    {
        using var db = NewDb();
        var (productId, svc, packs) = await SeedForSaveAsync(db, 449);

        var (ok, error) = await svc.SaveProductConfigAsync(SaveModel(productId, packId: null), Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal("Valence Pack is required. Configure the correct Valence Pack before saving this product.", error);
        // Nothing half-written, and nothing registered on the vendor either.
        Assert.Empty(db.Set<ProductSerialKeyConfig>());
        Assert.Empty(packs.Mapped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Test5b_SavingWithANonPositivePack_IsRejected(int packId)
    {
        using var db = NewDb();
        var (productId, svc, _) = await SeedForSaveAsync(db, 449);

        var (ok, error) = await svc.SaveProductConfigAsync(SaveModel(productId, packId), Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal("Valence Pack is required. Configure the correct Valence Pack before saving this product.", error);
    }

    [Fact]
    public async Task Test6_SavingValenceConfigWithAValidPack_Succeeds()
    {
        using var db = NewDb();
        var (productId, svc, packs) = await SeedForSaveAsync(db, 449);

        var (ok, error) = await svc.SaveProductConfigAsync(SaveModel(productId, packId: 449), Guid.NewGuid());

        Assert.True(ok, error);
        var row = await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync(c => c.ProductId == productId);
        Assert.Equal(productId.ToString(), row.ProviderProductCode);   // course == product id
        Assert.Equal(449, JsonSerializer.Deserialize<ValenceProductConfig>(row.ConfigJson)!.PackId);
        Assert.Contains((productId.ToString(), 449), packs.Mapped);
    }

    /// <summary>A COMBO that lists its packs only in the CSV still satisfies the requirement.</summary>
    [Fact]
    public async Task SavingAComboWithOnlyTheCsv_Succeeds_AndRegistersEveryPack()
    {
        using var db = NewDb();
        var (productId, svc, packs) = await SeedForSaveAsync(db, 574, 575);

        var (ok, error) = await svc.SaveProductConfigAsync(
            SaveModel(productId, packId: null, csv: "574,575"), Guid.NewGuid());

        Assert.True(ok, error);
        Assert.Contains((productId.ToString(), 574), packs.Mapped);
        Assert.Contains((productId.ToString(), 575), packs.Mapped);
    }

    [Fact]
    public async Task SavingAPackWeHaveNeverSynced_IsRejected()
    {
        using var db = NewDb();
        var (productId, svc, _) = await SeedForSaveAsync(db, 449);

        var (ok, error) = await svc.SaveProductConfigAsync(SaveModel(productId, packId: 9999), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("9999", error!);
    }
}
