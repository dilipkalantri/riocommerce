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
/// Superclass grants access through a course id OR a combo package id — <c>/api/register</c> never
/// wants both. The editor used to make Course ID mandatory regardless, so a combo product had the
/// same number typed into Course ID and Combo ID and both went on the wire (order RIO-1079 sent
/// <c>course_id=464</c> alongside <c>combo_id="464"</c>).
///
/// <para>These tests pin the three mapping modes, and above all the invariant that a request can
/// never carry both ids. No real Superclass call is made anywhere — the provider's HTTP handler is
/// a capturing fake.</para>
/// </summary>
public class SuperclassMappingModeTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"scmode-{Guid.NewGuid()}")
            .Options);

    private sealed class StubProvider : ISerialKeyProvider
    {
        public string Key => "superclass";
        public string DisplayName => "Superclass (stub)";
        public bool IssuesSerialKey => false;
        public bool SupportsActivate => false;
        public bool SupportsStatusLookup => false;
        public Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest r, CancellationToken ct) => throw new NotSupportedException();
        public Task<ActivateKeyResult> ActivateAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
        public Task<KeyStatusResult> GetStatusAsync(string k, string? t, CancellationToken ct) => throw new NotSupportedException();
    }

    private static SerialKeyService Svc(RioCommerceDbContext db) =>
        new(db,
            new SerialKeyProviderFactory(new ISerialKeyProvider[] { new StubProvider() }),
            new Mock<INotificationService>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IAppLogService>().Object,
            new Mock<IValencePackService>().Object,
            new Mock<ISuperclassSettingsService>().Object,
            NullLogger<SerialKeyService>.Instance);

    private static ProductSerialKeyConfigItem Model(
        Guid productId, SuperclassMappingMode? mode,
        int? courseId = null, string? courseCsv = null,
        int? comboId = null, string? comboCsv = null) => new()
    {
        ProductId = productId,
        ProviderKey = "superclass",
        IsActive = true,
        AutoActivate = true,
        SuperclassMappingMode = mode,
        SuperclassCourseId = courseId,
        SuperclassCourseIdsCsv = courseCsv,
        SuperclassComboId = comboId,
        SuperclassComboIdsCsv = comboCsv,
        SuperclassViewType = 1,
        SuperclassValue = 2,
        SuperclassValidityType = 1,
        SuperclassValidityDays = 270,
        SuperclassClassIdOverride = 318,
    };

    private static async Task<Guid> SeedProductAsync(RioCommerceDbContext db)
    {
        var id = Guid.NewGuid();
        db.Products.Add(new Product { Id = id, Title = "CA Foundation All Subjects COMBO", Slug = $"p-{id:N}" });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedPaidOrderAsync(RioCommerceDbContext db, Guid productId)
    {
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "RIO-9600", UserId = Guid.NewGuid(),
            StudentName = "Test Student", StudentPhone = "9876500000", StudentEmail = "s@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m,
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = orderId, ProductId = productId,
            ProductTitle = "CA Foundation All Subjects COMBO", Quantity = 1,
            UnitPrice = 1000m, LineTotal = 1000m,
        });
        await db.SaveChangesAsync();
        return orderId;
    }

    private static async Task<SuperclassProductConfig> StoredAsync(RioCommerceDbContext db)
    {
        var row = await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync();
        return JsonSerializer.Deserialize<SuperclassProductConfig>(row.ConfigJson)!;
    }

    private static SuperclassProductConfig Config(SerialKeyRecord rec)
    {
        var req = JsonSerializer.Deserialize<GenerateKeyRequest>(rec.RequestPayload)!;
        return JsonSerializer.Deserialize<SuperclassProductConfig>(req.ConfigJson)!;
    }

    // ── 1-2. Single course: course required, combo not ───────────────────────────────────────────

    [Fact]
    public async Task SingleCourse_RequiresACourseId()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.SingleCourse, courseId: null), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("Course ID is required", error);
    }

    [Fact]
    public async Task SingleCourse_DoesNotRequireAComboId_AndStoresNone()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.SingleCourse, courseId: 2403), Guid.NewGuid());

        Assert.True(ok, error);
        var cfg = await StoredAsync(db);
        Assert.Equal(2403, cfg.CourseId);
        Assert.Null(cfg.ComboId);
        Assert.Null(cfg.ComboIdsCsv);
    }

    /// <summary>Even when a combo id is left over in the form, single-course mode drops it — the
    /// admin is never made to keep two ids in step.</summary>
    [Fact]
    public async Task SingleCourse_DiscardsAnyComboIdLeftInTheForm()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        Assert.True((await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.SingleCourse, courseId: 464, comboId: 464), Guid.NewGuid())).ok);

        var cfg = await StoredAsync(db);
        Assert.Equal(464, cfg.CourseId);
        Assert.Null(cfg.ComboId);
    }

    // ── 5-6. Multiple courses ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleCourses_RequiresTheCourseIdList()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.MultipleCourses, courseCsv: null), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("Course ID", error);
    }

    [Fact]
    public async Task MultipleCourses_FanOutIsOneRegistrationPerCourse_AndCarriesNoComboId()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.MultipleCourses, courseCsv: "2722, 2723, 2788"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);

        var inserted = await svc.EnqueueForOrderAsync(orderId);

        Assert.Equal(3, inserted);
        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(new int?[] { 2722, 2723, 2788 }, records.Select(r => Config(r).CourseId).OrderBy(x => x).ToList());
        Assert.All(records, r => Assert.Null(Config(r).ComboId));
    }

    // ── 8-9, 12. Combo ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Combo_DoesNotRequireACourseId()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, courseId: null, comboId: 464), Guid.NewGuid());

        Assert.True(ok, error);
        var cfg = await StoredAsync(db);
        Assert.Equal(464, cfg.ComboId);
        Assert.Null(cfg.CourseId);          // never back-filled from the combo id
        Assert.Null(cfg.CourseIdsCsv);
    }

    /// <summary>
    /// The legacy-edit path. Opening the 464/464 product shows both ids; saving it as a Combo must
    /// drop the course id rather than keep the pair in step. Combo mode never keeps a course id —
    /// not one the admin typed, and not one inherited from the old shape.
    /// </summary>
    [Fact]
    public async Task Combo_SavingClearsAnyCourseIdLeftInTheForm()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        Assert.True((await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, courseId: 464, comboId: 464), Guid.NewGuid())).ok);

        var cfg = await StoredAsync(db);
        Assert.Equal(464, cfg.ComboId);
        Assert.Null(cfg.CourseId);
        Assert.Null(cfg.CourseIdsCsv);

        var row = await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync();
        Assert.Null(row.ProviderProductCode);   // nowhere for the course id to survive
    }

    [Fact]
    public async Task Combo_RequiresAComboIdOrAComboList()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, courseId: 2403), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("Combo ID is required", error);
    }

    [Fact]
    public async Task Combo_DoesNotCopyTheComboIdIntoProviderProductCode()
    {
        // ProviderProductCode is the provider's course-id fallback, so putting the combo id there
        // would smuggle it straight back in as a course_id.
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        Assert.True((await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, comboId: 464), Guid.NewGuid())).ok);

        var row = await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync();
        Assert.Null(row.ProviderProductCode);
    }

    [Fact]
    public async Task AComboList_FanOutIsOneRegistrationPerCombo_AndCarriesNoCourseId()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, comboCsv: "464, 465"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);

        var inserted = await svc.EnqueueForOrderAsync(orderId);

        Assert.Equal(2, inserted);
        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(new int?[] { 464, 465 }, records.Select(r => Config(r).ComboId).OrderBy(x => x).ToList());
        Assert.All(records, r => Assert.Null(Config(r).CourseId));
    }

    // ── End-to-end: what a fanned-out record actually puts on the wire ───────────────────────────
    //
    // The tests above check the stored config; these take the ConfigJson the enqueue really produced
    // and push it through the real provider, so the assertion is on the multipart body itself.

    /// <summary>Each combo in the list becomes its own request carrying that combo id and an empty
    /// course id — the shape the business rule requires, proven end to end.</summary>
    [Fact]
    public async Task AComboList_PutsComboIdOnlyOnTheWire_ForEveryRegistration()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, comboCsv: "464, 465"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);
        await svc.EnqueueForOrderAsync(orderId);

        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(2, records.Count);

        var sentCombos = new List<string>();
        foreach (var rec in records)
        {
            var req = JsonSerializer.Deserialize<GenerateKeyRequest>(rec.RequestPayload)!;
            var body = await SuperclassRequestMappingTests.SendAsync(req.ConfigJson, req.ProviderProductCode);

            Assert.Equal("", SuperclassRequestMappingTests.FieldValue(body, "course_id"));
            sentCombos.Add(SuperclassRequestMappingTests.FieldValue(body, "combo_id"));
        }
        Assert.Equal(new[] { "464", "465" }, sentCombos.OrderBy(x => x).ToArray());
    }

    /// <summary>The mirror: a multi-course product puts course ids on the wire and never a combo id.</summary>
    [Fact]
    public async Task ACourseList_PutsCourseIdOnlyOnTheWire_ForEveryRegistration()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.MultipleCourses, courseCsv: "2403, 2404"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);
        await svc.EnqueueForOrderAsync(orderId);

        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(2, records.Count);

        var sentCourses = new List<string>();
        foreach (var rec in records)
        {
            var req = JsonSerializer.Deserialize<GenerateKeyRequest>(rec.RequestPayload)!;
            var body = await SuperclassRequestMappingTests.SendAsync(req.ConfigJson, req.ProviderProductCode);

            Assert.Equal("", SuperclassRequestMappingTests.FieldValue(body, "combo_id"));
            sentCourses.Add(SuperclassRequestMappingTests.FieldValue(body, "course_id"));
        }
        Assert.Equal(new[] { "2403", "2404" }, sentCourses.OrderBy(x => x).ToArray());
    }

    // ── Mode survives save → reload ──────────────────────────────────────────────────────────────
    //
    // The reported bug: pick "Multiple Courses", save, reopen — and it read "Single Course". Two
    // causes, both here. A one-id list was dropped on save (the list was the only trace that the
    // mode had been chosen), and the multi-course branch validated the leftover scalar Course ID
    // instead of the list, so an empty list saved a single-course row.

    [Theory]
    [InlineData("2403, 2404")]          // the ordinary case
    [InlineData("2403,2404,2405")]      // 3+
    [InlineData("2403")]                // ONE id — still Multiple Courses, not Single
    public async Task MultipleCourses_ReloadsAsMultipleCourses(string csv)
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.MultipleCourses, courseCsv: csv), Guid.NewGuid())).ok);

        var item = await svc.GetProductConfigAsync(productId);

        Assert.Equal(SuperclassMappingMode.MultipleCourses, item.SuperclassMappingMode);
        Assert.Equal(csv.Replace(" ", ""), item.SuperclassCourseIdsCsv);
        Assert.Null(item.SuperclassComboId);
        Assert.Null(item.SuperclassComboIdsCsv);
    }

    /// <summary>The exact failure: the list mode stores its first id in the scalar too, so testing
    /// the scalar first classified a multi-course product as single-course.</summary>
    [Fact]
    public async Task MultipleCourses_IsNotMisreadAsSingle_JustBecauseTheScalarIsSet()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.MultipleCourses, courseCsv: "2403, 2404"), Guid.NewGuid())).ok);

        var cfg = await StoredAsync(db);
        Assert.Equal(2403, cfg.CourseId);                               // scalar IS set…
        Assert.Equal(SuperclassMappingMode.MultipleCourses,             // …and must not decide the mode
            SerialKeyService.DeriveSuperclassMode(cfg));
    }

    /// <summary>An empty list is rejected rather than quietly falling back to a course id left in
    /// the form by another mode — which is how a "Multiple Courses" save became a single course.</summary>
    [Fact]
    public async Task MultipleCourses_WithAnEmptyList_IsRejectedNotSilentlyDowngraded()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.MultipleCourses, courseId: 464, courseCsv: null), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("Course IDs are required", error);
        Assert.Empty(db.Set<ProductSerialKeyConfig>());
    }

    [Theory]
    [InlineData(null, "464, 465")]      // several combos
    [InlineData(464, null)]             // one combo, scalar field
    [InlineData(null, "464")]           // one combo, list field
    public async Task Combo_ReloadsAsCombo(int? comboId, string? comboCsv)
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.ComboPackages, comboId: comboId, comboCsv: comboCsv), Guid.NewGuid())).ok);

        var item = await svc.GetProductConfigAsync(productId);

        Assert.Equal(SuperclassMappingMode.ComboPackages, item.SuperclassMappingMode);
        Assert.Equal(464, item.SuperclassComboId);
        Assert.Null(item.SuperclassCourseId);
        Assert.Null(item.SuperclassCourseIdsCsv);
    }

    [Fact]
    public async Task SingleCourse_ReloadsAsSingleCourse()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.SingleCourse, courseId: 2403), Guid.NewGuid())).ok);

        var item = await svc.GetProductConfigAsync(productId);

        Assert.Equal(SuperclassMappingMode.SingleCourse, item.SuperclassMappingMode);
        Assert.Equal(2403, item.SuperclassCourseId);
        Assert.Null(item.SuperclassCourseIdsCsv);
        Assert.Null(item.SuperclassComboId);
    }

    /// <summary>Single-course mode discards a course LIST left behind by the multi-course mode, so
    /// switching down and saving does not reload as Multiple Courses.</summary>
    [Fact]
    public async Task SingleCourse_DiscardsACourseListLeftInTheForm()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);

        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, SuperclassMappingMode.SingleCourse, courseId: 2403, courseCsv: "2403,2404"), Guid.NewGuid())).ok);

        var item = await svc.GetProductConfigAsync(productId);

        Assert.Equal(SuperclassMappingMode.SingleCourse, item.SuperclassMappingMode);
        Assert.Null(item.SuperclassCourseIdsCsv);
    }

    /// <summary>Save → reload → save again must land on the same mode and the same ids. A mode that
    /// drifts on the second save is the same class of bug seen from a different angle.</summary>
    [Theory]
    [InlineData(SuperclassMappingMode.SingleCourse)]
    [InlineData(SuperclassMappingMode.MultipleCourses)]
    [InlineData(SuperclassMappingMode.ComboPackages)]
    public async Task SaveReloadSave_IsStable(SuperclassMappingMode mode)
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        var first = mode switch
        {
            SuperclassMappingMode.MultipleCourses => Model(productId, mode, courseCsv: "2403, 2404"),
            SuperclassMappingMode.ComboPackages => Model(productId, mode, comboId: 464),
            _ => Model(productId, mode, courseId: 2403),
        };
        Assert.True((await svc.SaveProductConfigAsync(first, Guid.NewGuid())).ok);

        // Reload, then save exactly what came back — the admin reopening and pressing Save.
        var reloaded = await svc.GetProductConfigAsync(productId);
        reloaded.ProviderKey = "superclass";
        Assert.True((await svc.SaveProductConfigAsync(reloaded, Guid.NewGuid())).ok);

        var again = await svc.GetProductConfigAsync(productId);
        Assert.Equal(mode, again.SuperclassMappingMode);
        Assert.Equal(reloaded.SuperclassCourseId, again.SuperclassCourseId);
        Assert.Equal(reloaded.SuperclassCourseIdsCsv, again.SuperclassCourseIdsCsv);
        Assert.Equal(reloaded.SuperclassComboId, again.SuperclassComboId);
        Assert.Equal(reloaded.SuperclassComboIdsCsv, again.SuperclassComboIdsCsv);
    }

    // ── 15-17. Backward compatibility ────────────────────────────────────────────────────────────

    [Fact]
    public void ExistingConfigurations_DeserializeAndDeriveTheirMode()
    {
        // Real shapes taken from live rows.
        var single = JsonSerializer.Deserialize<SuperclassProductConfig>(
            """{"Value":2,"CourseId":2786,"ViewType":1,"ValidityDays":270,"ValidityType":1,"ClassIdOverride":318}""")!;
        Assert.Equal(SuperclassMappingMode.SingleCourse, SerialKeyService.DeriveSuperclassMode(single));

        var multi = JsonSerializer.Deserialize<SuperclassProductConfig>(
            """{"Value":2,"CourseId":2789,"ViewType":1,"CourseIdsCsv":"2789,2790","ValidityDays":1000,"ValidityType":1}""")!;
        Assert.Equal(SuperclassMappingMode.MultipleCourses, SerialKeyService.DeriveSuperclassMode(multi));

        // The RIO-1079 shape: both ids present. The combo id is the deliberate one, so it wins.
        var legacyBoth = JsonSerializer.Deserialize<SuperclassProductConfig>(
            """{"Value":2,"CourseId":464,"ComboId":464,"ViewType":1,"ValidityDays":270,"ValidityType":1}""")!;
        Assert.Equal(SuperclassMappingMode.ComboPackages, SerialKeyService.DeriveSuperclassMode(legacyBoth));
    }

    [Fact]
    public async Task ALegacyRowHoldingBothIds_LoadsIntactAndIsFlagged_NotRewritten()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        const string legacy = """{"Value":2,"CourseId":464,"ComboId":464,"ViewType":1,"ValidityDays":270,"ValidityType":1,"ClassIdOverride":318}""";
        db.Set<ProductSerialKeyConfig>().Add(new ProductSerialKeyConfig
        {
            Id = Guid.NewGuid(), ProductId = productId, ProviderKey = "superclass",
            ProviderProductCode = "464", ConfigJson = legacy, IsActive = true, AutoActivate = true,
        });
        await db.SaveChangesAsync();

        var item = await Svc(db).GetProductConfigAsync(productId);

        // Neither value destroyed…
        Assert.Equal(464, item.SuperclassCourseId);
        Assert.Equal(464, item.SuperclassComboId);
        // …the mode is resolved…
        Assert.Equal(SuperclassMappingMode.ComboPackages, item.SuperclassMappingMode);
        // …the admin is told which id actually goes out…
        Assert.NotNull(item.SuperclassMappingWarning);
        Assert.Contains("COMBO", item.SuperclassMappingWarning);

        // …and merely opening the editor rewrote nothing on disk.
        var row = await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync();
        Assert.Equal(legacy, row.ConfigJson);
    }

    [Theory]
    [InlineData(SuperclassMappingMode.SingleCourse)]
    [InlineData(SuperclassMappingMode.MultipleCourses)]
    [InlineData(SuperclassMappingMode.ComboPackages)]
    public async Task SaveThenReload_PreservesTheModeAndItsIds(SuperclassMappingMode mode)
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var model = mode switch
        {
            SuperclassMappingMode.MultipleCourses => Model(productId, mode, courseCsv: "2722, 2723"),
            SuperclassMappingMode.ComboPackages => Model(productId, mode, comboId: 464),
            _ => Model(productId, mode, courseId: 2403),
        };
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(model, Guid.NewGuid())).ok);

        var item = await svc.GetProductConfigAsync(productId);

        Assert.Equal(mode, item.SuperclassMappingMode);
        Assert.Null(item.SuperclassMappingWarning);   // the new shapes never carry both ids
        switch (mode)
        {
            case SuperclassMappingMode.MultipleCourses:
                Assert.Equal("2722,2723", item.SuperclassCourseIdsCsv);
                break;
            case SuperclassMappingMode.ComboPackages:
                Assert.Equal(464, item.SuperclassComboId);
                Assert.Null(item.SuperclassCourseId);
                break;
            default:
                Assert.Equal(2403, item.SuperclassCourseId);
                Assert.Null(item.SuperclassComboId);
                break;
        }
    }

    /// <summary>A caller that predates the mode (the public API, an older client) sends null and the
    /// mode is inferred from the ids supplied — so nothing that worked before stops working.</summary>
    [Fact]
    public async Task AnOmittedMode_IsInferredFromTheIdsSupplied()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        Assert.True((await Svc(db).SaveProductConfigAsync(
            Model(productId, mode: null, courseId: null, courseCsv: "2722,2723"), Guid.NewGuid())).ok);

        var cfg = await StoredAsync(db);
        Assert.Equal("2722,2723", cfg.CourseIdsCsv);
        Assert.Equal(2722, cfg.CourseId);
        Assert.Null(cfg.ComboId);
    }
}

/// <summary>
/// What actually reaches <c>/api/register</c>. The HTTP handler is a capturing fake — no Superclass
/// call is ever made — so these assert on the exact multipart body the provider produces.
/// </summary>
public class SuperclassRequestMappingTests
{
    /// <summary>Captures the outgoing multipart body and answers with a canned success.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":200,"message":"ok","student_id":4321}"""),
            };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Reads one field's value out of a captured multipart/form-data body. MultipartFormDataContent
    /// writes the name unquoted (<c>name=combo_id</c>) but the quoted form is legal too, so both are tried.</summary>
    internal static string FieldValue(string body, string name)
    {
        var at = body.IndexOf($"name=\"{name}\"", StringComparison.Ordinal);
        if (at < 0) at = body.IndexOf($"name={name}", StringComparison.Ordinal);
        Assert.True(at >= 0, $"field '{name}' was not sent at all");

        // Value starts after the blank line that ends this part's headers, and runs to the next boundary.
        var valueStart = body.IndexOf("\r\n\r\n", at, StringComparison.Ordinal) + 4;
        var valueEnd = body.IndexOf("\r\n--", valueStart, StringComparison.Ordinal);
        return body[valueStart..(valueEnd < 0 ? body.Length : valueEnd)];
    }

    internal static async Task<string> SendAsync(string configJson, string? providerProductCode = null)
    {
        var handler = new CapturingHandler();
        var settings = new Mock<ISuperclassSettingsService>();
        settings.Setup(s => s.ResolveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuperclassCredentials("https://lms.example.test", "token-abc", 318, "Test", false, 5));

        var provider = new SuperclassSerialKeyProvider(
            new SingleClientFactory(handler), settings.Object,
            NullLogger<SuperclassSerialKeyProvider>.Instance);

        var result = await provider.GenerateAsync(new GenerateKeyRequest
        {
            ProviderKey = "superclass",
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            OrderNumber = "RIO-1079",
            ProductId = Guid.NewGuid(),
            ProductTitle = "CA Foundation All Subjects COMBO",
            ProviderProductCode = providerProductCode,
            CustomerName = "Harsh Soni",
            CustomerEmail = "ssjfaridabad@example.test",
            CustomerPhone = "9873998443",
            CustomerCountryCode = "91",
            CustomerPincode = "121002",
            Quantity = 1,
            ConfigJson = configJson,
        }, CancellationToken.None);

        Assert.True(result.Success, result.ErrorMessage);
        return handler.Body;
    }

    private const string Validity = "\"ViewType\":1,\"Value\":2,\"ValidityType\":1,\"ValidityDays\":270";

    // ── 3-4. Single course sends course_id only ──────────────────────────────────────────────────

    [Fact]
    public async Task SingleCourse_SendsTheCourseIdAndAnEmptyComboId()
    {
        var body = await SendAsync("{\"CourseId\":2403," + Validity + "}");

        Assert.Equal("2403", FieldValue(body, "course_id"));
        Assert.Equal("", FieldValue(body, "combo_id"));
    }

    // ── 10-12. Combo sends combo_id only ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Combo_SendsTheComboIdAndAnEmptyCourseId()
    {
        var body = await SendAsync("{\"ComboId\":464," + Validity + "}");

        Assert.Equal("464", FieldValue(body, "combo_id"));
        Assert.Equal("", FieldValue(body, "course_id"));
    }

    [Fact]
    public async Task AComboWithNoCourseId_IsAccepted()
    {
        // Previously this failed outright with "no_course" — a combo had to invent a course id.
        var body = await SendAsync("{\"CourseId\":null,\"ComboId\":464," + Validity + "}");

        Assert.Equal("464", FieldValue(body, "combo_id"));
        Assert.Equal("", FieldValue(body, "course_id"));
    }

    // ── 13. The invariant ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"CourseId":2403,"ViewType":1,"Value":2,"ValidityType":1,"ValidityDays":270}""")]
    [InlineData("""{"ComboId":464,"ViewType":1,"Value":2,"ValidityType":1,"ValidityDays":270}""")]
    [InlineData("""{"CourseId":464,"ComboId":464,"ViewType":1,"Value":2,"ValidityType":1,"ValidityDays":270}""")]
    public async Task ARequestNeverCarriesBothACourseIdAndAComboId(string configJson)
    {
        var body = await SendAsync(configJson);

        var course = FieldValue(body, "course_id");
        var combo = FieldValue(body, "combo_id");
        Assert.False(course.Length > 0 && combo.Length > 0,
            $"both ids were sent: course_id='{course}' combo_id='{combo}'");
        Assert.True(course.Length > 0 || combo.Length > 0, "neither id was sent");
    }

    /// <summary>The exact RIO-1079 shape. It used to send both; the combo now wins alone.</summary>
    [Fact]
    public async Task TheLegacyBothIdsShape_SendsTheComboOnly()
    {
        var body = await SendAsync("{\"CourseId\":464,\"ComboId\":464," + Validity + "}", providerProductCode: "464");

        Assert.Equal("464", FieldValue(body, "combo_id"));
        Assert.Equal("", FieldValue(body, "course_id"));
    }

    /// <summary>ProviderProductCode remains the course-id fallback for a course product.</summary>
    [Fact]
    public async Task ACourseIdMissingFromConfig_StillFallsBackToProviderProductCode()
    {
        var body = await SendAsync("{" + Validity + "}", providerProductCode: "2403");

        Assert.Equal("2403", FieldValue(body, "course_id"));
        Assert.Equal("", FieldValue(body, "combo_id"));
    }

    [Fact]
    public async Task NeitherIdAnywhere_IsRejectedBeforeAnyHttpCall()
    {
        var handler = new CapturingHandler();
        var settings = new Mock<ISuperclassSettingsService>();
        settings.Setup(s => s.ResolveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SuperclassCredentials("https://lms.example.test", "t", 318, "Test", false, 5));

        var provider = new SuperclassSerialKeyProvider(
            new SingleClientFactory(handler), settings.Object,
            NullLogger<SuperclassSerialKeyProvider>.Instance);

        var result = await provider.GenerateAsync(new GenerateKeyRequest
        {
            ProviderKey = "superclass",
            OrderItemId = Guid.NewGuid(),
            OrderNumber = "RIO-1",
            CustomerName = "A", CustomerEmail = "a@example.test", CustomerPhone = "9999999999",
            ConfigJson = """{"ViewType":1,"Value":2,"ValidityType":1,"ValidityDays":270}""",
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("no_course", result.ErrorCode);
        Assert.Equal("", handler.Body);   // nothing was sent
    }
}
