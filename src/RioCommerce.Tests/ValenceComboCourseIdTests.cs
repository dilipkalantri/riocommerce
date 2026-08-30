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
/// A Valence COMBO must yield one key per subject.
///
/// <para>Valence resolves the pack from the <c>course</c> — the registration call carries no pack —
/// and a pack belongs to ONE course. So a combo cannot claim packs that its standalone products
/// already own: <c>save_product_pack</c> reports success but registration answers "No pack found for
/// this course". (A synthetic per-pack course id was tried against the live API and failed the same
/// way.) The combo therefore registers each pack under the course of the product that owns it, which
/// already resolves — proven on FRN-1012, where it produced two distinct keys.</para>
/// </summary>
public class ValenceComboCourseIdTests
{
    private const string Base = "https://edubeessecurelms.com/edubeessecurelms/index.php";
    private const string Path = "secretsegment";

    /// <summary>Mimics Valence: one key per (student, course). The same course again → the same key.</summary>
    private sealed class SpyProvider : ISerialKeyProvider
    {
        public string Key => "valence";
        public string DisplayName => "Valence (spy)";
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;

        public List<GenerateKeyRequest> Seen { get; } = new();
        private readonly Dictionary<string, string> _keysByCourse = new();

        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest r, CancellationToken ct)
        {
            Seen.Add(r);
            var course = r.ProviderProductCode ?? "";
            if (!_keysByCourse.TryGetValue(course, out var key))
            {
                key = $"KEY{_keysByCourse.Count + 1:D4}";
                _keysByCourse[course] = key;
            }
            return Task.FromResult(new GenerateKeyResult
            {
                Success = true, SerialKey = key, Activated = true,
                RawRequest = "{}", RawResponse = """{"status":"success"}""",
            });
        }

        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SpyPacks : IValencePackService
    {
        public List<(string CourseId, int PackId)> Mapped { get; } = new();
        public Task<(bool ok, string? error)> MapProductToPackAsync(
            string baseUrl, string pathSegment, string courseId, string productName, int packExternalId,
            CancellationToken ct = default)
        {
            Mapped.Add((courseId, packExternalId));
            return Task.FromResult((true, (string?)null));
        }
        public Task<List<ValencePackItem>> ListAsync(bool activeOnly = true, CancellationToken ct = default)
            => Task.FromResult(new List<ValencePackItem>());
        public Task<ValencePackSyncResult> SyncAsync(string? b, string? p, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"combo-{Guid.NewGuid()}")
            .Options);

    private static SerialKeyService Svc(RioCommerceDbContext db, SpyProvider provider, SpyPacks packs) =>
        new(db,
            new SerialKeyProviderFactory(new ISerialKeyProvider[] { provider }),
            new Mock<INotificationService>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            packs,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);

    private static async Task<Guid> AddProductAsync(RioCommerceDbContext db, string title, params int[] knownPacks)
    {
        var id = Guid.NewGuid();
        db.Products.Add(new Product { Id = id, Title = title, Slug = $"p-{id:N}" });
        foreach (var p in knownPacks)
            if (!await db.ValencePacks.AnyAsync(x => x.ExternalId == p))
                db.ValencePacks.Add(new ValencePack
                {
                    Id = Guid.NewGuid(), ExternalId = p, PackName = $"PACK{p}",
                    IsActive = true, LastSyncedAt = DateTime.UtcNow,
                });
        await db.SaveChangesAsync();
        return id;
    }

    private static ProductSerialKeyConfigItem Model(Guid productId, int scalarPack, string? csv = null) => new()
    {
        ProductId = productId, ProviderKey = "valence", IsActive = true, AutoActivate = true,
        ValenceBaseUrl = Base, ValencePathSegment = Path, ValenceClassId = "5702", ValenceKeyViews = 1,
        ValencePackId = scalarPack, ValencePackIdsCsv = csv,
    };

    private static async Task<Guid> SeedPaidOrderAsync(RioCommerceDbContext db, Guid productId, string number = "FRN-1012")
    {
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = number, UserId = Guid.NewGuid(),
            StudentName = "DHWANI MISTRY", StudentPhone = "9876500000", StudentEmail = "d@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m, BillingPincode = "411001",
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = orderId, ProductId = productId,
            ProductTitle = "CA Inter Audit & Costing Fastrack COMBO",
            Quantity = 1, UnitPrice = 1000m, LineTotal = 1000m,
        });
        await db.SaveChangesAsync();
        return orderId;
    }

    /// <summary>The production shape: two standalone subjects plus a combo that sells both packs.</summary>
    private static async Task<(Guid Audit, Guid Costing, Guid Combo, SerialKeyService Svc, SpyProvider Provider, SpyPacks Packs)>
        SeedFamilyAsync(RioCommerceDbContext db)
    {
        var provider = new SpyProvider();
        var packs = new SpyPacks();
        var svc = Svc(db, provider, packs);

        var audit = await AddProductAsync(db, "CA Inter Audit Fastrack", 449);
        var costing = await AddProductAsync(db, "CA Inter Costing Fastrack", 450);
        var combo = await AddProductAsync(db, "CA Inter Audit & Costing Fastrack COMBO");

        Assert.True((await svc.SaveProductConfigAsync(Model(audit, 449), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(costing, 450), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(combo, 449, "449,450"), Guid.NewGuid())).ok);

        packs.Mapped.Clear();
        provider.Seen.Clear();
        return (audit, costing, combo, svc, provider, packs);
    }

    // ── The outcome that matters ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AComboOrder_YieldsOneKeyPerSubject()
    {
        using var db = NewDb();
        var (audit, costing, combo, svc, provider, _) = await SeedFamilyAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo);
        Assert.Equal(2, await svc.EnqueueForOrderAsync(orderId));
        await svc.ProcessDueAsync(10);

        // Registered under the OWNING products' courses — the two that actually resolve on Valence.
        Assert.Equal(
            new[] { audit.ToString(), costing.ToString() }.OrderBy(x => x).ToArray(),
            provider.Seen.Select(r => r.ProviderProductCode).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(provider.Seen, r => r.ProviderProductCode == combo.ToString());

        // Two distinct courses → two distinct keys, and nothing rejected as a duplicate.
        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(SerialKeyStatus.Activated, r.Status));
        Assert.Equal(2, records.Select(r => r.SerialKey).Distinct().Count());
        Assert.DoesNotContain(records, r => r.ErrorCode == "duplicate_key");
    }

    [Fact]
    public async Task EachRecordStillCarriesItsOwnPack()
    {
        using var db = NewDb();
        var (_, _, combo, svc, provider, _) = await SeedFamilyAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo);
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        Assert.Equal(
            new int?[] { 449, 450 },
            provider.Seen.Select(r => JsonSerializer.Deserialize<ValenceProductConfig>(r.ConfigJson)!.PackId)
                         .OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task TheGuardMapsExactlyTheCourseThatWillBeRegistered()
    {
        using var db = NewDb();
        var (_, _, combo, svc, provider, packs) = await SeedFamilyAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo);
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        // A mismatch here is exactly what produces "No pack found for this course".
        Assert.Equal(
            provider.Seen.Select(r => r.ProviderProductCode).OrderBy(x => x).ToArray(),
            packs.Mapped.Select(m => m.CourseId).OrderBy(x => x).ToArray());
    }

    // ── Single-pack products are untouched ──────────────────────────────────────────────────────

    [Fact]
    public async Task ASinglePackProduct_StillRegistersUnderItsOwnCourse()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var packs = new SpyPacks();
        var svc = Svc(db, provider, packs);
        var audit = await AddProductAsync(db, "CA Inter Audit Fastrack", 449);
        Assert.True((await svc.SaveProductConfigAsync(Model(audit, 449), Guid.NewGuid())).ok);

        var orderId = await SeedPaidOrderAsync(db, audit, "RIO-9001");
        Assert.Equal(1, await svc.EnqueueForOrderAsync(orderId));
        await svc.ProcessDueAsync(10);

        var sent = Assert.Single(provider.Seen);
        Assert.Equal(audit.ToString(), sent.ProviderProductCode);
        Assert.Equal(449, JsonSerializer.Deserialize<ValenceProductConfig>(sent.ConfigJson)!.PackId);
    }

    // ── A pack nobody else owns ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APackNoStandaloneProductSells_StaysOnTheCombosOwnCourse()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var packs = new SpyPacks();
        var svc = Svc(db, provider, packs);

        var audit = await AddProductAsync(db, "CA Inter Audit Fastrack", 449);
        var combo = await AddProductAsync(db, "COMBO with one exclusive pack", 999);
        Assert.True((await svc.SaveProductConfigAsync(Model(audit, 449), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(combo, 449, "449,999"), Guid.NewGuid())).ok);
        provider.Seen.Clear();

        var orderId = await SeedPaidOrderAsync(db, combo);
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        // 449 belongs to the standalone product; 999 belongs to nobody, so the combo's own course
        // can hold it — and on Valence that mapping does take, because no other course claims it.
        var byPack = provider.Seen.ToDictionary(
            r => JsonSerializer.Deserialize<ValenceProductConfig>(r.ConfigJson)!.PackId,
            r => r.ProviderProductCode);
        Assert.Equal(audit.ToString(), byPack[449]);
        Assert.Equal(combo.ToString(), byPack[999]);
    }

    /// <summary>Two combos must not point at each other — a combo never owns a pack on Valence.</summary>
    [Fact]
    public async Task AnotherCombosPack_IsNotTreatedAsAnOwner()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var packs = new SpyPacks();
        var svc = Svc(db, provider, packs);

        var otherCombo = await AddProductAsync(db, "Some other COMBO", 449, 450);
        var combo = await AddProductAsync(db, "CA Inter Audit & Costing Fastrack COMBO");
        Assert.True((await svc.SaveProductConfigAsync(Model(otherCombo, 449, "449,450"), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(combo, 449, "449,450"), Guid.NewGuid())).ok);
        provider.Seen.Clear();

        var orderId = await SeedPaidOrderAsync(db, combo);
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        Assert.DoesNotContain(provider.Seen, r => r.ProviderProductCode == otherCombo.ToString());
        Assert.All(provider.Seen, r => Assert.Equal(combo.ToString(), r.ProviderProductCode));
    }

    // ── A retry must not undo any of it ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryingAComboRecord_KeepsItsOwnPackAndOwnerCourse()
    {
        using var db = NewDb();
        var (audit, costing, combo, svc, provider, _) = await SeedFamilyAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo);
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        foreach (var r in await db.Set<SerialKeyRecord>().Where(r => r.OrderId == orderId).ToListAsync())
        {
            r.Status = SerialKeyStatus.Pending;
            r.SerialKey = null;
            r.ErrorCode = null;
            r.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
        }
        await db.SaveChangesAsync();
        provider.Seen.Clear();

        await svc.ProcessDueAsync(10);

        // The refresh overlays the PRODUCT's config, which lists every pack — before the fix that
        // collapsed both records onto pack 449, and with it onto one course.
        Assert.Equal(
            new int?[] { 449, 450 },
            provider.Seen.Select(r => JsonSerializer.Deserialize<ValenceProductConfig>(r.ConfigJson)!.PackId)
                         .OrderBy(x => x).ToArray());
        Assert.Equal(
            new[] { audit.ToString(), costing.ToString() }.OrderBy(x => x).ToArray(),
            provider.Seen.Select(r => r.ProviderProductCode).OrderBy(x => x).ToArray());
    }

    // ── Pack ownership when a standalone restates its one pack in the CSV ───────────────────────
    //
    // Everything above seeds the standalones with a bare PackId and no CSV. Production does not look
    // like that: the Audit and Costing products each carry PackIdsCsv listing their single pack
    // ("449", "450"). Owner lookup used to demand a BLANK csv, so those two were rejected, no owner
    // was found, and the combo fell back to registering both packs under its own course — which is
    // exactly the "No pack found for this course" failure seen on RIO-1077.
    //
    // The fixture below is the real production shape, so these tests fail against the old condition
    // and pass against the effective-pack-list one.

    /// <summary>Production's shape: each standalone repeats its own pack in the CSV.</summary>
    private static async Task<(Guid Audit, Guid Costing, Guid Combo, SerialKeyService Svc, SpyProvider Provider, SpyPacks Packs)>
        SeedFamilyWithRestatedCsvAsync(RioCommerceDbContext db)
    {
        var provider = new SpyProvider();
        var packs = new SpyPacks();
        var svc = Svc(db, provider, packs);

        var audit = await AddProductAsync(db, "CA Inter Audit Fastrack", 449);
        var costing = await AddProductAsync(db, "CA Inter Costing Fastrack", 450);
        var combo = await AddProductAsync(db, "CA Inter Audit & Costing Fastrack COMBO");

        Assert.True((await svc.SaveProductConfigAsync(Model(audit, 449, "449"), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(costing, 450, "450"), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(combo, 449, "449,450"), Guid.NewGuid())).ok);

        packs.Mapped.Clear();
        provider.Seen.Clear();
        return (audit, costing, combo, svc, provider, packs);
    }

    // 1, 2, 5, 6 — a standalone owns its pack even with the CSV filled in, and the combo resolves
    // each pack to the product that owns it.
    [Fact]
    public async Task AStandaloneThatRestatesItsPackInTheCsv_StillOwnsThatPack()
    {
        using var db = NewDb();
        var (audit, costing, combo, svc, provider, _) = await SeedFamilyWithRestatedCsvAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo, "RIO-1077");
        Assert.Equal(2, await svc.EnqueueForOrderAsync(orderId));
        await svc.ProcessDueAsync(10);

        var byPack = provider.Seen.ToDictionary(
            r => JsonSerializer.Deserialize<ValenceProductConfig>(r.ConfigJson)!.PackId,
            r => r.ProviderProductCode);

        Assert.Equal(audit.ToString(), byPack[449]);      // pack 449 → Audit standalone
        Assert.Equal(costing.ToString(), byPack[450]);    // pack 450 → Costing standalone
    }

    // 3, 4 — the combo is never its own owner, and never the owner of either pack.
    [Fact]
    public async Task TheComboIsNeverUsedAsTheCourseForEitherPack()
    {
        using var db = NewDb();
        var (_, _, combo, svc, provider, packs) = await SeedFamilyWithRestatedCsvAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo, "RIO-1077");
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        // The exact regression: both registrations used to carry the combo id and Valence answered
        // "No pack found for this course".
        Assert.DoesNotContain(provider.Seen, r => r.ProviderProductCode == combo.ToString());
        Assert.DoesNotContain(packs.Mapped, m => m.CourseId == combo.ToString());
    }

    // 8 — the corrected resolution is the ONLY change: still two records, two packs, two keys.
    [Fact]
    public async Task TheRestOfTheComboBehaviourIsUnchanged()
    {
        using var db = NewDb();
        var (_, _, combo, svc, provider, packs) = await SeedFamilyWithRestatedCsvAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo, "RIO-1077");
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(SerialKeyStatus.Activated, r.Status));
        Assert.Equal(2, records.Select(r => r.SerialKey).Distinct().Count());
        Assert.DoesNotContain(records, r => r.ErrorCode == "duplicate_key");

        // Each record still carries its own pack, and the mapping call still precedes registration
        // for exactly the course that will be registered.
        Assert.Equal(
            new int?[] { 449, 450 },
            provider.Seen.Select(r => JsonSerializer.Deserialize<ValenceProductConfig>(r.ConfigJson)!.PackId)
                         .OrderBy(x => x).ToArray());
        foreach (var seen in provider.Seen)
        {
            var pack = JsonSerializer.Deserialize<ValenceProductConfig>(seen.ConfigJson)!.PackId!.Value;
            Assert.Contains(packs.Mapped, m => m.PackId == pack && m.CourseId == seen.ProviderProductCode);
        }
    }

    // 7 — a standalone order is untouched by the widened owner rule: it is not a combo, so the
    // resolution short-circuits and its own course is used, CSV or no CSV.
    [Fact]
    public async Task AStandaloneOrder_StillRegistersUnderItsOwnCourse_WhenTheCsvRestatesItsPack()
    {
        using var db = NewDb();
        var (audit, _, _, svc, provider, _) = await SeedFamilyWithRestatedCsvAsync(db);

        var orderId = await SeedPaidOrderAsync(db, audit, "FRN-1024");
        Assert.Equal(1, await svc.EnqueueForOrderAsync(orderId));
        await svc.ProcessDueAsync(10);

        var seen = Assert.Single(provider.Seen);
        Assert.Equal(audit.ToString(), seen.ProviderProductCode);
        Assert.Equal(449, JsonSerializer.Deserialize<ValenceProductConfig>(seen.ConfigJson)!.PackId);
    }

    // 10 — retry keeps the corrected owner course rather than drifting back to the combo.
    [Fact]
    public async Task RetryAfterTheFix_KeepsTheOwnerCourses()
    {
        using var db = NewDb();
        var (audit, costing, combo, svc, provider, _) = await SeedFamilyWithRestatedCsvAsync(db);

        var orderId = await SeedPaidOrderAsync(db, combo, "RIO-1077");
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        foreach (var r in await db.Set<SerialKeyRecord>().Where(r => r.OrderId == orderId).ToListAsync())
        {
            r.Status = SerialKeyStatus.Pending;
            r.SerialKey = null;
            r.ErrorCode = null;
            r.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
        }
        await db.SaveChangesAsync();
        provider.Seen.Clear();

        await svc.ProcessDueAsync(10);

        Assert.Equal(
            new[] { audit.ToString(), costing.ToString() }.OrderBy(x => x).ToArray(),
            provider.Seen.Select(r => r.ProviderProductCode).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(provider.Seen, r => r.ProviderProductCode == combo.ToString());
    }

    // A pack nobody sells standalone still falls back to the combo's own course — the widened rule
    // must not invent an owner where there is none.
    [Fact]
    public async Task APackWithNoStandaloneOwner_StillFallsBackToTheCombo()
    {
        using var db = NewDb();
        var provider = new SpyProvider();
        var packs = new SpyPacks();
        var svc = Svc(db, provider, packs);

        var audit = await AddProductAsync(db, "CA Inter Audit Fastrack", 449);
        var combo = await AddProductAsync(db, "Audit + Exclusive COMBO", 451);

        Assert.True((await svc.SaveProductConfigAsync(Model(audit, 449, "449"), Guid.NewGuid())).ok);
        Assert.True((await svc.SaveProductConfigAsync(Model(combo, 449, "449,451"), Guid.NewGuid())).ok);
        provider.Seen.Clear();

        var orderId = await SeedPaidOrderAsync(db, combo, "RIO-1078");
        await svc.EnqueueForOrderAsync(orderId);
        await svc.ProcessDueAsync(10);

        var byPack = provider.Seen.ToDictionary(
            r => JsonSerializer.Deserialize<ValenceProductConfig>(r.ConfigJson)!.PackId,
            r => r.ProviderProductCode);

        Assert.Equal(audit.ToString(), byPack[449]);   // owned → the owner's course
        Assert.Equal(combo.ToString(), byPack[451]);   // unowned → the combo's own course
    }
}
