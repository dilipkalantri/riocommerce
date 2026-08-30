using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
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
/// Switching a product's serial-key provider.
///
/// <para>The upsert used to match on ProviderKey as well as scope, so choosing a different vendor
/// INSERTED a second active row instead of switching. Every reader resolved a scope by "oldest row
/// wins", so the editor reloaded the old provider and key generation kept dispatching to the old
/// vendor — the save looked like it had done nothing at all.</para>
/// </summary>
public class ProviderSwitchTests
{
    private sealed class StubProvider : ISerialKeyProvider
    {
        public StubProvider(string key) => Key = key;
        public string Key { get; }
        public string DisplayName => Key;
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;
        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"provswitch-{Guid.NewGuid()}")
            .Options);

    private static SerialKeyService Svc(RioCommerceDbContext db)
    {
        // Saving a Valence config pushes the product↔pack mapping to the vendor and fails the save if
        // it does not land. Left at Moq's default the tuple would be (false, null) — every Valence
        // save would "fail" for a reason that has nothing to do with what these tests are about.
        var packs = new Mock<IValencePackService>();
        packs.Setup(p => p.MapProductToPackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                                                 It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((true, (string?)null));

        return new SerialKeyService(db,
            new SerialKeyProviderFactory(new ISerialKeyProvider[]
            {
                new StubProvider("valence"), new StubProvider("superclass"), new StubProvider("rioplay"),
            }),
            new Mock<INotificationService>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            packs.Object,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);
    }

    private static async Task<Guid> SeedProductAsync(RioCommerceDbContext db)
    {
        var id = Guid.NewGuid();
        db.Products.Add(new Product { Id = id, Title = "CA Foundation LAW Regular", Slug = $"p-{id:N}" });
        db.ValencePacks.Add(new ValencePack
        {
            Id = Guid.NewGuid(), ExternalId = 570, PackName = "PACK4672", IsActive = true, LastSyncedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static ProductSerialKeyConfigItem ValenceModel(Guid productId) => new()
    {
        ProductId = productId, ProviderKey = "valence", IsActive = true, AutoActivate = true,
        ValenceBaseUrl = "https://edubeessecurelms.com/edubeessecurelms/index.php",
        ValencePathSegment = "ajsfkjsfdjsf",
        ValenceClassId = "5702", ValenceKeyViews = 11, ValencePackId = 570,
    };

    private static ProductSerialKeyConfigItem SuperclassModel(Guid productId) => new()
    {
        ProductId = productId, ProviderKey = "superclass", IsActive = true, AutoActivate = true,
        SuperclassCourseId = 2722, SuperclassViewType = 1, SuperclassValue = 2,
        SuperclassValidityType = 1, SuperclassValidityDays = 1000, SuperclassClassIdOverride = 318,
    };

    private static IQueryable<ProductSerialKeyConfig> ActiveProductLevel(RioCommerceDbContext db, Guid productId) =>
        db.Set<ProductSerialKeyConfig>().AsNoTracking()
          .Where(c => c.ProductId == productId && c.ProductModeId == null && c.IsActive);

    // ── The reported bug ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SwitchingProvider_ActuallySwitches_AndLeavesOneActiveConfig()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(SuperclassModel(productId), Guid.NewGuid())).ok);

        // Exactly one live row, and it is the provider that was just chosen.
        var active = await ActiveProductLevel(db, productId).ToListAsync();
        var row = Assert.Single(active);
        Assert.Equal("superclass", row.ProviderKey);

        // …and reopening the editor shows it, rather than reverting to Valence.
        var loaded = await svc.GetProductConfigAsync(productId);
        Assert.Equal("superclass", loaded.ProviderKey);
        Assert.Equal(2722, loaded.SuperclassCourseId);
    }

    [Fact]
    public async Task SwitchingBackAndForth_NeverAccumulatesRows()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);
            Assert.True((await svc.SaveProductConfigAsync(SuperclassModel(productId), Guid.NewGuid())).ok);
        }

        Assert.Single(await ActiveProductLevel(db, productId).ToListAsync());
        Assert.Equal("superclass", (await svc.GetProductConfigAsync(productId)).ProviderKey);
    }

    [Fact]
    public async Task SavingTheSameProviderTwice_UpdatesTheSameRow()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);
        var firstId = (await ActiveProductLevel(db, productId).FirstAsync()).Id;

        var edited = ValenceModel(productId);
        edited.ValenceKeyViews = 25;
        Assert.True((await svc.SaveProductConfigAsync(edited, Guid.NewGuid())).ok);

        var row = Assert.Single(await ActiveProductLevel(db, productId).ToListAsync());
        Assert.Equal(firstId, row.Id);          // same row, not a replacement
        Assert.Equal(25, JsonSerializer.Deserialize<ValenceProductConfig>(row.ConfigJson)!.KeyViews);
    }

    // ── Self-healing for products that already carry a duplicate ────────────────────────────────

    [Fact]
    public async Task AProductAlreadyCarryingTwoActiveConfigs_IsRepairedOnTheNextSave()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        // Exactly the production state: an old rioplay row and a newer valence row, both active in
        // the product-level scope, created by the previous provider-keyed upsert.
        db.Set<ProductSerialKeyConfig>().AddRange(
            new ProductSerialKeyConfig
            {
                Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "rioplay",
                ProviderProductCode = "5813", ConfigJson = "{}", IsActive = true,
            },
            new ProductSerialKeyConfig
            {
                Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "valence",
                ProviderProductCode = productId.ToString(), ConfigJson = "{}", IsActive = true,
            });
        await db.SaveChangesAsync();
        Assert.Equal(2, await ActiveProductLevel(db, productId).CountAsync());

        Assert.True((await Svc(db).SaveProductConfigAsync(SuperclassModel(productId), Guid.NewGuid())).ok);

        var row = Assert.Single(await ActiveProductLevel(db, productId).ToListAsync());
        Assert.Equal("superclass", row.ProviderKey);
    }

    [Fact]
    public async Task WithTwoActiveConfigs_TheMostRecentlySavedOneIsReadNotTheOldest()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var old = new ProductSerialKeyConfig
        {
            Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "rioplay",
            ProviderProductCode = "5813", ConfigJson = "{}", IsActive = true,
        };
        db.Set<ProductSerialKeyConfig>().Add(old);
        await db.SaveChangesAsync();

        var newer = new ProductSerialKeyConfig
        {
            Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "valence",
            ProviderProductCode = productId.ToString(), ConfigJson = "{}", IsActive = true,
        };
        db.Set<ProductSerialKeyConfig>().Add(newer);
        await db.SaveChangesAsync();     // stamped later than `old`

        // Before the fix this returned rioplay — the first row ever created.
        Assert.Equal("valence", (await Svc(db).GetProductConfigAsync(productId)).ProviderKey);
    }

    // ── Removing the config entirely ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChoosingNoSerialKeyGeneration_LeavesNothingActive()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);

        Assert.True(await svc.ClearProductConfigAsync(productId, Guid.NewGuid()));

        Assert.Empty(await ActiveProductLevel(db, productId).ToListAsync());
        Assert.True(string.IsNullOrEmpty((await svc.GetProductConfigAsync(productId)).ProviderKey));
    }

    [Fact]
    public async Task ClearingThenPickingAProviderAgain_ReusesTheRowRatherThanPilingUp()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);
        Assert.True(await svc.ClearProductConfigAsync(productId, Guid.NewGuid()));
        Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);

        Assert.Single(await ActiveProductLevel(db, productId).ToListAsync());
        Assert.Single(await db.Set<ProductSerialKeyConfig>().AsNoTracking()
            .Where(c => c.ProductId == productId && c.ProductModeId == null).ToListAsync());
    }

    // ── Scopes stay independent ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AModeOverrideDoesNotDisturbTheProductLevelDefault()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var modeId = Guid.NewGuid();
        db.Set<ProductMode>().Add(new ProductMode { Id = modeId, ProductId = productId, ModeName = "Pen Drive" });
        await db.SaveChangesAsync();
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(ValenceModel(productId), Guid.NewGuid())).ok);

        var modeModel = SuperclassModel(productId);
        modeModel.ProductModeId = modeId;
        Assert.True((await svc.SaveProductConfigAsync(modeModel, Guid.NewGuid())).ok);

        // Product-level default untouched…
        var productLevel = Assert.Single(await ActiveProductLevel(db, productId).ToListAsync());
        Assert.Equal("valence", productLevel.ProviderKey);

        // …and the mode carries its own override.
        Assert.Equal("superclass", (await svc.GetProductConfigAsync(productId, modeId)).ProviderKey);
    }
}
