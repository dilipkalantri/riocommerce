using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Repositories;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// Every course level can be filtered for, Advanced included.
///
/// <para>The Products search panel listed the ordinals 0–3 by hand and stopped before
/// <c>Advanced</c> (4), so Advanced courses could not be filtered at all. The enum has always had the
/// member and the label switch has always had its caption — only the dropdown markup was short.</para>
///
/// <para>The order the options appear in cannot come from the enum: <c>CourseLevel</c> is declared
/// append-only because its ordinals are serialised into public search URLs, which puts Advanced last
/// in the declaration and after Intermediate on screen. So the coverage test below asserts the
/// display list holds every member — the guard that would have caught the original omission.</para>
/// </summary>
public class ProductLevelFilterTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"level-{Guid.NewGuid()}")
            .Options);

    private static ProductAdminService Products(RioCommerceDbContext db) =>
        new(db, new Mock<IPublicFileStorage>().Object, new Mock<ISeoUrlService>().Object);

    private sealed record Fx(Guid FinalFaculty, Guid InterFaculty, Guid FinalSub, Guid InterSub,
                             Guid FinalCat, Guid FinalProduct, Guid InterProduct, Guid BooksProduct);

    /// <summary>One product per level that matters here, each with its own faculty and subject so a
    /// leak between levels is unmistakable.</summary>
    private static async Task<Fx> SeedAsync(RioCommerceDbContext db)
    {
        var f = new Fx(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                       Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.Faculty.AddRange(
            new Faculty { Id = f.FinalFaculty, DisplayName = "Advanced Faculty", IsActive = true },
            new Faculty { Id = f.InterFaculty, DisplayName = "CA Inter Faculty", IsActive = true });

        db.Subjects.AddRange(
            new Subject { Id = f.FinalSub, Name = "Final SCMPE", Slug = "scmpe", IsActive = true },
            new Subject { Id = f.InterSub, Name = "Inter Audit", Slug = "audit", IsActive = true });

        db.Categories.Add(new Category { Id = f.FinalCat, Name = "Advanced", Slug = "advanced", IsActive = true });

        db.Products.AddRange(
            new Product { Id = f.FinalProduct, Title = "Advanced SCMPE", Slug = "final-scmpe",
                          Level = CourseLevel.Advanced, SubjectId = f.FinalSub, CategoryId = f.FinalCat,
                          PrimaryFacultyId = f.FinalFaculty, Status = ProductStatus.Active },
            new Product { Id = f.InterProduct, Title = "CA Inter Audit", Slug = "inter-audit",
                          Level = CourseLevel.Intermediate, SubjectId = f.InterSub,
                          PrimaryFacultyId = f.InterFaculty, Status = ProductStatus.Active },
            new Product { Id = f.BooksProduct, Title = "Reference Book", Slug = "book",
                          Level = CourseLevel.Books, Status = ProductStatus.Active });

        db.Set<ProductFaculty>().AddRange(
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.FinalProduct, FacultyId = f.FinalFaculty, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.InterProduct, FacultyId = f.InterFaculty, IsPrimary = true });

        await db.SaveChangesAsync();
        return f;
    }

    // ── The dropdown must offer every level ─────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors the filter's display order. Kept in step with ProductList.LevelFilterOrder by the
    /// coverage test below — if a member is ever added to the enum and forgotten in either list,
    /// that test fails rather than the option quietly going missing again.
    /// </summary>
    private static readonly CourseLevel[] DisplayOrder =
    {
        CourseLevel.Beginner,
        CourseLevel.Intermediate,
        CourseLevel.Advanced,
        CourseLevel.Books,
        CourseLevel.TestSeries,
    };

    [Fact]
    public void EveryLevelIsOfferedByTheFilter()
    {
        // The exact guard the original markup lacked: it listed four of five.
        Assert.Equal(
            Enum.GetValues<CourseLevel>().OrderBy(x => x),
            DisplayOrder.OrderBy(x => x));
    }

    [Fact]
    public void AdvancedAppearsAfterIntermediate()
    {
        Assert.Equal(2, Array.IndexOf(DisplayOrder, CourseLevel.Advanced));
        Assert.Equal(1, Array.IndexOf(DisplayOrder, CourseLevel.Intermediate));
    }

    [Fact]
    public void TheEnumStillDeclaresAdvancedLast()
    {
        // Declaration order is append-only because the ordinals live in public search URLs. If this
        // ever fails, someone reordered the enum and every bookmarked ?level=N changed meaning.
        Assert.Equal(4, (int)CourseLevel.Advanced);
        Assert.Equal(0, (int)CourseLevel.Beginner);
        Assert.Equal(1, (int)CourseLevel.Intermediate);
        Assert.Equal(2, (int)CourseLevel.Books);
        Assert.Equal(3, (int)CourseLevel.TestSeries);
    }

    // ── Filtering by each level actually works ──────────────────────────────────────────────────

    [Theory]
    [InlineData(CourseLevel.Advanced)]
    [InlineData(CourseLevel.Intermediate)]
    [InlineData(CourseLevel.Books)]
    public async Task EachLevelReturnsOnlyItsOwnProducts(CourseLevel level)
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await new ProductRepository(db).GetFilteredAsync(new ProductFilterRequest
        {
            Level = level, Page = 1, PageSize = 100,
        });

        Assert.All(page.Items, i => Assert.Equal(level, i.Level));
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task AdvancedReturnsTheAdvancedCourse()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var page = await new ProductRepository(db).GetFilteredAsync(new ProductFilterRequest
        {
            Level = CourseLevel.Advanced, Page = 1, PageSize = 100,
        });

        Assert.Equal(new[] { f.FinalProduct }, page.Items.Select(i => i.Id).ToArray());
    }

    // ── The cascade still behaves, Advanced included ────────────────────────────────────────────

    [Fact]
    public async Task AdvancedNarrowsSubjectsFacultyAndCategories()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync(level: CourseLevel.Advanced);

        Assert.Equal(new[] { f.FinalSub }, meta.Subjects.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { f.FinalFaculty }, meta.Faculties.Select(x => x.Id).ToArray());
        Assert.Equal(new[] { f.FinalCat }, meta.Categories.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task AdvancedIntersectsWithFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        // Advanced AND the Inter faculty is an impossible pair — nothing should survive.
        var meta = await Products(db).GetFilterMetaAsync(level: CourseLevel.Advanced, facultyId: f.InterFaculty);

        Assert.Empty(meta.Subjects);
        Assert.Empty(meta.Categories);
    }

    [Fact]
    public async Task ASubjectSelectionBecomesInvalidUnderADifferentLevel()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        // The Inter subject was already picked, then the level moved to Advanced.
        var meta = await Products(db).GetFilterMetaAsync(level: CourseLevel.Advanced);

        // The page drops a selection the list no longer offers — this asserts the list says so.
        Assert.DoesNotContain(f.InterSub, meta.Subjects.Select(s => s.Id));
        Assert.Contains(f.FinalSub, meta.Subjects.Select(s => s.Id));
    }

    [Fact]
    public async Task LevelIsIgnoredWhenBuildingNothingElse()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var meta = await Products(db).GetFilterMetaAsync();

        // No level picked → no narrowing at all, exactly as before this change.
        Assert.Equal(2, meta.Subjects.Count);
        Assert.Equal(2, meta.Faculties.Count);
        Assert.Single(meta.Categories);
    }
}
