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
/// Superclass's <c>/api/register</c> carries exactly one <c>course_id</c> and one <c>combo_id</c> per
/// call, so a product covering several courses needs several registrations. Before this the config
/// held a single int per field and the enqueue had no Superclass fan-out, so only the first course of
/// a combo was ever granted — silently, with no error anywhere.
/// </summary>
public class SuperclassComboTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"sccombo-{Guid.NewGuid()}")
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

    private static ProductSerialKeyConfigItem Model(Guid productId, int? courseId, string? courseCsv = null,
                                                    int? comboId = null, string? comboCsv = null) => new()
    {
        ProductId = productId,
        ProviderKey = "superclass",
        IsActive = true,
        AutoActivate = true,
        SuperclassCourseId = courseId,
        SuperclassCourseIdsCsv = courseCsv,
        SuperclassComboId = comboId,
        SuperclassComboIdsCsv = comboCsv,
        SuperclassViewType = 1,
        SuperclassValue = 2,
        SuperclassValidityType = 1,
        SuperclassValidityDays = 1000,
        SuperclassClassIdOverride = 318,
    };

    private static async Task<Guid> SeedProductAsync(RioCommerceDbContext db)
    {
        var id = Guid.NewGuid();
        db.Products.Add(new Product { Id = id, Title = "CA Inter Group 1 Regular Combo", Slug = $"p-{id:N}" });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>A paid order for the product, so EnqueueForOrderAsync will actually run.</summary>
    private static async Task<Guid> SeedPaidOrderAsync(RioCommerceDbContext db, Guid productId)
    {
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "RIO-9500", UserId = Guid.NewGuid(),
            StudentName = "Test Student", StudentPhone = "9876500000", StudentEmail = "s@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m,
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = orderId, ProductId = productId,
            ProductTitle = "CA Inter Group 1 Regular Combo", Quantity = 1,
            UnitPrice = 1000m, LineTotal = 1000m,
        });
        await db.SaveChangesAsync();
        return orderId;
    }

    private static SuperclassProductConfig Config(SerialKeyRecord rec)
    {
        var req = JsonSerializer.Deserialize<GenerateKeyRequest>(rec.RequestPayload)!;
        return JsonSerializer.Deserialize<SuperclassProductConfig>(req.ConfigJson)!;
    }

    // ── Saving a list ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeveralCourseIds_CanBeSaved_AndTheScalarBecomesTheFirst()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: "2722, 2723"), Guid.NewGuid());

        Assert.True(ok, error);
        var row = await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync(c => c.ProductId == productId);
        var cfg = JsonSerializer.Deserialize<SuperclassProductConfig>(row.ConfigJson)!;
        Assert.Equal("2722,2723", cfg.CourseIdsCsv);
        Assert.Equal(2722, cfg.CourseId);                       // scalar kept for single-id readers
        Assert.Equal("2722", row.ProviderProductCode);
    }

    [Fact]
    public async Task SeveralComboIds_CanBeSaved()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, courseId: 2722, comboCsv: "2722, 2814"), Guid.NewGuid());

        Assert.True(ok, error);
        var cfg = JsonSerializer.Deserialize<SuperclassProductConfig>(
            (await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync()).ConfigJson)!;
        Assert.Equal("2722,2814", cfg.ComboIdsCsv);
        Assert.Equal(2722, cfg.ComboId);
    }

    [Fact]
    public async Task ListingBothCoursesAndCombos_IsRejectedAsAmbiguous()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: "2722,2723", comboCsv: "2800,2801"), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("not both", error);
        Assert.Empty(db.Set<ProductSerialKeyConfig>());
    }

    [Theory]
    [InlineData("2722, abc")]
    [InlineData("2722, 0")]
    [InlineData("2722, -5")]
    public async Task AMistypedId_IsRejectedRatherThanSilentlyDropped(string csv)
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        var (ok, error) = await Svc(db).SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: csv), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("Course ID", error);
    }

    [Fact]
    public async Task DuplicateIdsInTheList_AreCollapsed()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);

        Assert.True((await Svc(db).SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: "2722, 2723, 2722"), Guid.NewGuid())).ok);

        var cfg = JsonSerializer.Deserialize<SuperclassProductConfig>(
            (await db.Set<ProductSerialKeyConfig>().AsNoTracking().FirstAsync()).ConfigJson)!;
        Assert.Equal("2722,2723", cfg.CourseIdsCsv);
    }

    // ── Fanning out at enqueue ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACourseCombo_EnqueuesOneRegistrationPerCourse()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: "2722, 2723, 2724"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);

        var inserted = await svc.EnqueueForOrderAsync(orderId);

        Assert.Equal(3, inserted);
        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        var courses = records.Select(r => Config(r).CourseId).OrderBy(x => x).ToList();
        Assert.Equal(new int?[] { 2722, 2723, 2724 }, courses);

        // Each record is a plain single-course product from the provider's point of view.
        Assert.All(records, r =>
        {
            var cfg = Config(r);
            Assert.Null(cfg.CourseIdsCsv);
            Assert.Null(cfg.ComboIdsCsv);
        });

        // ProviderProductCode mirrors the course id, so it moves with the fan-out.
        var codes = records
            .Select(r => JsonSerializer.Deserialize<GenerateKeyRequest>(r.RequestPayload)!.ProviderProductCode)
            .OrderBy(x => x).ToList();
        Assert.Equal(new[] { "2722", "2723", "2724" }, codes);
    }

    [Fact]
    public async Task AComboIdList_EnqueuesOneRegistrationPerCombo_CarryingNoCourseId()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, courseId: 2722, comboCsv: "2722, 2814"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);

        var inserted = await svc.EnqueueForOrderAsync(orderId);

        Assert.Equal(2, inserted);
        var records = await db.Set<SerialKeyRecord>().AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync();
        Assert.Equal(new int?[] { 2722, 2814 }, records.Select(r => Config(r).ComboId).OrderBy(x => x).ToList());
        // The course is not carried along. /api/register grants through a course id OR a combo id,
        // never both, so a combo fan-out sends combo_id alone — this used to pin CourseId=2722 onto
        // every record and send it beside each combo.
        Assert.All(records, r => Assert.Null(Config(r).CourseId));
    }

    [Fact]
    public async Task ASingleCourseProduct_StillEnqueuesExactlyOneRecord()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(Model(productId, courseId: 2403), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);

        var inserted = await svc.EnqueueForOrderAsync(orderId);

        Assert.Equal(1, inserted);
        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.OrderId == orderId);
        Assert.Equal(2403, Config(rec).CourseId);
    }

    [Fact]
    public async Task TheOtherFieldsSurviveTheFanOut()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: "2722,2723"), Guid.NewGuid())).ok);
        var orderId = await SeedPaidOrderAsync(db, productId);
        await svc.EnqueueForOrderAsync(orderId);

        var rec = await db.Set<SerialKeyRecord>().AsNoTracking().FirstAsync(r => r.OrderId == orderId);
        var cfg = Config(rec);
        Assert.Equal(1, cfg.ViewType);
        Assert.Equal(2, cfg.Value);
        Assert.Equal(1, cfg.ValidityType);
        Assert.Equal(1000, cfg.ValidityDays);
        Assert.Equal(318, cfg.ClassIdOverride);
    }

    [Fact]
    public async Task TheListRoundTripsBackIntoTheEditor()
    {
        using var db = NewDb();
        var productId = await SeedProductAsync(db);
        var svc = Svc(db);
        Assert.True((await svc.SaveProductConfigAsync(
            Model(productId, courseId: null, courseCsv: "2722, 2723"), Guid.NewGuid())).ok);

        var item = await svc.GetProductConfigAsync(productId);

        Assert.Equal("2722,2723", item.SuperclassCourseIdsCsv);
        Assert.Equal(2722, item.SuperclassCourseId);
    }
}
