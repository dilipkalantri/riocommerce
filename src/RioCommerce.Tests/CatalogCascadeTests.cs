using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using RioCommerce.Infrastructure.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// Phase 2 — the shared catalog cascade, and the Orders and Products pickers built on it.
///
/// <para>One algorithm serves every screen: UNION within a dimension, INTERSECTION across them, a
/// dimension never applies its own selection, and nothing selected means no narrowing at all.</para>
///
/// <para>The one deliberate difference is how a faculty is matched. Reports and the admin order list
/// resolve through <c>ProductFaculty</c>, because that is how they filter their results. The
/// storefront matches <c>PrimaryFacultyId</c>, because <c>ProductRepository</c> does — a picker that
/// disagrees with its own result list offers names that return nothing.</para>
/// </summary>
public class CatalogCascadeTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"p2-{Guid.NewGuid()}")
            .Options);

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────
    //
    //  Product            Level          Subject   Category   Primary   Also teaches
    //  ─────────────────────────────────────────────────────────────────────────────
    //  Audit Fastrack     Intermediate Audit     Inter      Harshad   —
    //  Costing Fastrack   Intermediate Costing   Inter      Harshad   —
    //  Foundation Law     Beginner   Law       Foundation Tejal     —
    //  Law Crash Course   Beginner   Law       Foundation Tejal     Harshad  ← co-taught
    //  Orphan Book        Books          (none)    (none)     (none)    —

    private sealed record Fx(
        Guid Harshad, Guid Tejal, Guid Nobody,
        Guid AuditSub, Guid CostingSub, Guid LawSub, Guid UnusedSub,
        Guid InterCat, Guid FoundationCat, Guid EmptyCat,
        Guid AuditFt, Guid CostingFt, Guid FoundationLaw, Guid LawCrash, Guid Orphan);

    private static async Task<Fx> SeedAsync(RioCommerceDbContext db)
    {
        var f = new Fx(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.Faculty.AddRange(
            new Faculty { Id = f.Harshad, DisplayName = "CA Harshad Jaju", IsActive = true, DisplayOrder = 1 },
            new Faculty { Id = f.Tejal, DisplayName = "CA CS Tejal Katariya", IsActive = true, DisplayOrder = 2 },
            new Faculty { Id = f.Nobody, DisplayName = "CA Nobody", IsActive = true, DisplayOrder = 3 });

        db.Subjects.AddRange(
            new Subject { Id = f.AuditSub, Name = "Audit", Slug = "audit", IsActive = true, DisplayOrder = 1 },
            new Subject { Id = f.CostingSub, Name = "Costing", Slug = "cost", IsActive = true, DisplayOrder = 2 },
            new Subject { Id = f.LawSub, Name = "Law", Slug = "law", IsActive = true, DisplayOrder = 3 },
            new Subject { Id = f.UnusedSub, Name = "Unused", Slug = "un", IsActive = true, DisplayOrder = 4 });

        db.Categories.AddRange(
            new Category { Id = f.InterCat, Name = "CA Inter", Slug = "inter", IsActive = true, DisplayOrder = 1 },
            new Category { Id = f.FoundationCat, Name = "Beginner", Slug = "found", IsActive = true, DisplayOrder = 2 },
            new Category { Id = f.EmptyCat, Name = "Empty", Slug = "empty", IsActive = true, DisplayOrder = 3 });

        db.Products.AddRange(
            New(f.AuditFt, "Audit Fastrack", CourseLevel.Intermediate, f.AuditSub, f.InterCat, f.Harshad),
            New(f.CostingFt, "Costing Fastrack", CourseLevel.Intermediate, f.CostingSub, f.InterCat, f.Harshad),
            New(f.FoundationLaw, "Foundation Law", CourseLevel.Beginner, f.LawSub, f.FoundationCat, f.Tejal),
            New(f.LawCrash, "Law Crash Course", CourseLevel.Beginner, f.LawSub, f.FoundationCat, f.Tejal),
            New(f.Orphan, "Orphan Book", CourseLevel.Books, null, null, null));

        db.Set<ProductFaculty>().AddRange(
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.AuditFt, FacultyId = f.Harshad, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.CostingFt, FacultyId = f.Harshad, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.FoundationLaw, FacultyId = f.Tejal, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.LawCrash, FacultyId = f.Tejal, IsPrimary = true },
            // Co-taught: Harshad teaches it but is NOT the primary.
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.LawCrash, FacultyId = f.Harshad, IsPrimary = false });

        await db.SaveChangesAsync();
        return f;

        static Product New(Guid id, string title, CourseLevel level, Guid? subject, Guid? category, Guid? primary) => new()
        {
            Id = id, Title = title, Slug = $"p-{id:N}", Level = level,
            SubjectId = subject, CategoryId = category, PrimaryFacultyId = primary,
            Status = ProductStatus.Active,
        };
    }

    private static CatalogCascade.Selection Pick(
        Guid? faculty = null, Guid? product = null, Guid? subject = null, Guid? category = null,
        CourseLevel? level = null, CatalogCascade.FacultyMatch match = CatalogCascade.FacultyMatch.Mapped)
        => CatalogCascade.Selection.Of(faculty, product, subject, category, level, match);

    // ── The shared algorithm ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NothingSelected_NarrowsNothing()
    {
        using var db = NewDb();
        await SeedAsync(db);

        foreach (var d in new[] { CatalogCascade.Dimension.Faculty, CatalogCascade.Dimension.Product,
                                  CatalogCascade.Dimension.Subject, CatalogCascade.Dimension.Category })
            Assert.False(CatalogCascade.Constrains(Pick(), d));
    }

    [Fact]
    public async Task ADimensionNeverAppliesItsOwnSelection()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        // Only Faculty is picked, so building the Faculty list must not narrow at all.
        Assert.False(CatalogCascade.Constrains(Pick(faculty: f.Harshad), CatalogCascade.Dimension.Faculty));
        Assert.True(CatalogCascade.Constrains(Pick(faculty: f.Harshad), CatalogCascade.Dimension.Product));
    }

    [Fact]
    public async Task LevelNarrowsProducts()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await CatalogCascade
            .ProductsMatching(db, Pick(level: CourseLevel.Beginner), CatalogCascade.Dimension.Product)
            .Select(p => p.Id).ToListAsync();

        Assert.Equal(new[] { f.FoundationLaw, f.LawCrash }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public async Task CategoryNarrowsProducts()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await CatalogCascade
            .ProductsMatching(db, Pick(category: f.InterCat), CatalogCascade.Dimension.Product)
            .Select(p => p.Id).ToListAsync();

        Assert.Equal(new[] { f.AuditFt, f.CostingFt }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public async Task DimensionsIntersect()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await CatalogCascade
            .ProductsMatching(db, Pick(faculty: f.Harshad, level: CourseLevel.Beginner),
                              CatalogCascade.Dimension.Product)
            .Select(p => p.Id).ToListAsync();

        // Harshad AND Beginner → only the co-taught crash course.
        Assert.Equal(new[] { f.LawCrash }, ids.ToArray());
    }

    // ── Faculty matching: the one deliberate difference between screens ─────────────────────────

    [Fact]
    public async Task MappedMatch_CountsACoTaughtCourseForEveryTeacher()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await CatalogCascade
            .FacultyIdsFor(db, db.Products.Where(p => p.Id == f.LawCrash), CatalogCascade.FacultyMatch.Mapped)
            .ToListAsync();

        Assert.Equal(new[] { f.Harshad, f.Tejal }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public async Task PrimaryOnlyMatch_ReturnsJustThePrimaryTeacher()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await CatalogCascade
            .FacultyIdsFor(db, db.Products.Where(p => p.Id == f.LawCrash), CatalogCascade.FacultyMatch.PrimaryOnly)
            .ToListAsync();

        // PrimaryOnly is no longer used by any screen — every one of them now matches through
        // ProductFaculty. The mode is kept and tested because it is the correct answer for anything
        // that means "whose headline course is this", and a silently broken option is worse than none.
        Assert.Equal(new[] { f.Tejal }, ids.ToArray());
    }

    [Fact]
    public async Task PrimaryOnlyMatch_AlsoNarrowsProductsByPrimaryOnly()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await CatalogCascade
            .ProductsMatching(db, Pick(faculty: f.Harshad, match: CatalogCascade.FacultyMatch.PrimaryOnly),
                              CatalogCascade.Dimension.Product)
            .Select(p => p.Id).ToListAsync();

        // The crash course is excluded — Harshad teaches it but is not its primary.
        Assert.Equal(new[] { f.AuditFt, f.CostingFt }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    // ── Orders screen ───────────────────────────────────────────────────────────────────────────

    private static OrderAdminService Orders(RioCommerceDbContext db) =>
        new(db,
            new Mock<IAuditService>().Object, new Mock<IRealtimeBus>().Object,
            new Mock<INotificationCenterService>().Object, new Mock<IOrderCalculationService>().Object,
            new Mock<INotificationService>().Object, new Mock<IFranchiseService>().Object,
            new Mock<IFacultySharingService>().Object, new Mock<ISerialKeyService>().Object,
            new Mock<IInvoiceService>().Object, new Mock<IInstallmentService>().Object,
            new Mock<INotificationSender>().Object, new Mock<IPermissionService>().Object,
            NullLogger<OrderAdminService>.Instance);

    [Fact]
    public async Task Orders_FacultyNarrowsTheProductPicker()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var products = await Orders(db).ListProductsForFilterAsync(null, 100, f.Harshad);

        // Mapped match: the co-taught crash course counts for Harshad here, matching how the admin
        // order list itself filters by faculty.
        Assert.Equal(
            new[] { f.AuditFt, f.CostingFt, f.LawCrash }.OrderBy(x => x),
            products.Select(p => p.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Orders_ProductNarrowsTheFacultyList()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Orders(db).GetFilterMetaAsync(f.LawCrash);

        Assert.Equal(new[] { f.Harshad, f.Tejal }.OrderBy(x => x), meta.Faculty.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Orders_FranchiseesAreNeverNarrowed()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        db.Franchises.AddRange(
            new Franchise { Id = Guid.NewGuid(), Name = "Zeroinfy", Code = "ZI" },
            new Franchise { Id = Guid.NewGuid(), Name = "CA Point", Code = "CAP" });
        await db.SaveChangesAsync();
        var svc = Orders(db);

        var all = await svc.GetFilterMetaAsync();
        var narrowed = await svc.GetFilterMetaAsync(f.AuditFt);

        Assert.Equal(2, all.Franchises.Count);
        Assert.Equal(all.Franchises.Count, narrowed.Franchises.Count);
    }

    [Fact]
    public async Task Orders_NoSelectionLeavesBothListsWhole()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var svc = Orders(db);

        var meta = await svc.GetFilterMetaAsync();
        var products = await svc.ListProductsForFilterAsync(null, 100);

        Assert.Equal(3, meta.Faculty.Count);     // including the faculty who teaches nothing
        Assert.Equal(5, products.Count);
    }

    // NOT TESTED HERE: the product picker's text search combined with the faculty cascade.
    // ListProductsForFilterAsync narrows by ILIKE, which is Npgsql-only and throws on the in-memory
    // provider these tests run against. The two Where clauses are independent and both applied, but
    // that combination is only verifiable against a real Postgres database.

    // ── Products screen ─────────────────────────────────────────────────────────────────────────

    private static ProductAdminService Products(RioCommerceDbContext db) =>
        new(db, new Mock<IPublicFileStorage>().Object, new Mock<ISeoUrlService>().Object);

    [Fact]
    public async Task Products_NoSelectionLeavesEveryListWhole()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync();

        Assert.Equal(3, meta.Categories.Count);   // including the category with no products
        Assert.Equal(4, meta.Subjects.Count);     // including the unused subject
        Assert.Equal(3, meta.Faculties.Count);    // including the faculty who teaches nothing
    }

    [Fact]
    public async Task Products_FacultyNarrowsSubjectsAndCategories()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(facultyId: f.Harshad);

        Assert.Equal(
            new[] { f.AuditSub, f.CostingSub, f.LawSub }.OrderBy(x => x),   // Law via the co-taught course
            meta.Subjects.Select(s => s.Id).OrderBy(x => x));
        Assert.DoesNotContain(f.EmptyCat, meta.Categories.Select(c => c.Id));
    }

    [Fact]
    public async Task Products_SubjectNarrowsFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(subjectId: f.AuditSub);

        Assert.Equal(new[] { f.Harshad }, meta.Faculties.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task Products_LevelNarrowsSubjectsAndFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(level: CourseLevel.Intermediate);

        Assert.Equal(new[] { f.AuditSub, f.CostingSub }.OrderBy(x => x), meta.Subjects.Select(s => s.Id).OrderBy(x => x));
        Assert.Equal(new[] { f.Harshad }, meta.Faculties.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task Products_CategoryNarrowsSubjectsAndFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(categoryId: f.FoundationCat);

        Assert.Equal(new[] { f.LawSub }, meta.Subjects.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { f.Harshad, f.Tejal }.OrderBy(x => x), meta.Faculties.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Products_ADimensionStillOffersItsOwnAlternatives()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(subjectId: f.AuditSub);

        // Subject is what was picked, so its own list must stay complete — otherwise a different
        // subject could never be chosen.
        Assert.Equal(4, meta.Subjects.Count);
    }

    [Fact]
    public async Task Products_DimensionsIntersect()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(
            facultyId: f.Harshad, level: CourseLevel.Beginner);

        // Harshad AND Beginner → the crash course only, so Law is the one subject left.
        Assert.Equal(new[] { f.LawSub }, meta.Subjects.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task Products_AnImpossibleCombinationEmptiesTheDerivedLists()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(subjectId: f.AuditSub, level: CourseLevel.Beginner);

        Assert.Empty(meta.Faculties);
        Assert.Empty(meta.Categories);
    }

    [Fact]
    public async Task Products_ACategoryParentSurvivesWhenOnlyItsChildHasProducts()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        // Re-parent CA Inter under a new root that holds no products of its own.
        var root = Guid.NewGuid();
        db.Categories.Add(new Category { Id = root, Name = "All Courses", Slug = "all", IsActive = true, DisplayOrder = 0 });
        (await db.Categories.FirstAsync(c => c.Id == f.InterCat)).ParentId = root;
        await db.SaveChangesAsync();

        var meta = await Products(db).GetFilterMetaAsync(facultyId: f.Harshad);

        // Dropping the parent would orphan CA Inter out of the tree walk and lose it from the list.
        Assert.Contains(root, meta.Categories.Select(c => c.Id));
        Assert.Contains(f.InterCat, meta.Categories.Select(c => c.Id));
    }
}
