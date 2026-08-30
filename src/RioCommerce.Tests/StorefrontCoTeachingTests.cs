using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The storefront's faculty filter matches through <c>ProductFaculty</c>, so a co-taught course
/// appears for every teacher mapped to it.
///
/// <para>It used to match <c>Product.PrimaryFacultyId</c> only, which hid co-taught courses from
/// every teacher except the headline one — the CA Foundation All Subjects batch is mapped to ten
/// faculty in production and answered for exactly one of them. Reports, Orders and the Products
/// admin already matched through the mapping table; this brings the public pages in line.</para>
///
/// <para><c>ProductRepository.GetFilteredAsync</c> is the single result query behind Courses,
/// Search and Category Browse, so these tests cover all three at once. <c>PrimaryFacultyId</c> keeps
/// its other jobs — headline teacher, delete guard, faculty-card counts — and none of them is
/// touched here.</para>
/// </summary>
public class StorefrontCoTeachingTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"coteach-{Guid.NewGuid()}")
            .Options);

    private sealed record Fx(
        Guid Harshad, Guid Nipurn, Guid Tejal, Guid Unrelated,
        Guid AuditSub, Guid LawSub,
        Guid SoloAudit, Guid CoTaught, Guid TejalLaw);

    /// <summary>
    /// Mirrors the production shape the change was made for:
    /// <list type="bullet">
    ///   <item>Solo Audit — Harshad only.</item>
    ///   <item>Co-taught combo — primary Harshad, also mapped to Nipurn.</item>
    ///   <item>Tejal's Law course — nothing to do with either.</item>
    /// </list>
    /// </summary>
    private static async Task<Fx> SeedAsync(RioCommerceDbContext db)
    {
        var f = new Fx(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                       Guid.NewGuid(), Guid.NewGuid(),
                       Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.Faculty.AddRange(
            new Faculty { Id = f.Harshad, DisplayName = "CA Harshad Jaju", IsActive = true },
            new Faculty { Id = f.Nipurn, DisplayName = "CA Nipurn Modi", IsActive = true },
            new Faculty { Id = f.Tejal, DisplayName = "CA CS Tejal Katariya", IsActive = true },
            new Faculty { Id = f.Unrelated, DisplayName = "CA Nobody", IsActive = true });

        db.Subjects.AddRange(
            new Subject { Id = f.AuditSub, Name = "Audit", Slug = "audit", IsActive = true },
            new Subject { Id = f.LawSub, Name = "Law", Slug = "law", IsActive = true });

        db.Products.AddRange(
            New(f.SoloAudit, "Audit Fastrack", f.AuditSub, f.Harshad),
            New(f.CoTaught, "Audit & FMSM Regular COMBO", f.AuditSub, f.Harshad),
            New(f.TejalLaw, "Foundation Law", f.LawSub, f.Tejal));

        db.Set<ProductFaculty>().AddRange(
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.SoloAudit, FacultyId = f.Harshad, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.CoTaught, FacultyId = f.Harshad, IsPrimary = true },
            // The co-teacher. Primary stays Harshad.
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.CoTaught, FacultyId = f.Nipurn, IsPrimary = false },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.TejalLaw, FacultyId = f.Tejal, IsPrimary = true });

        await db.SaveChangesAsync();
        return f;

        static Product New(Guid id, string title, Guid subject, Guid primary) => new()
        {
            Id = id, Title = title, Slug = $"p-{id:N}", SubjectId = subject,
            PrimaryFacultyId = primary, Status = ProductStatus.Active, Level = CourseLevel.CaIntermediate,
        };
    }

    private static async Task<HashSet<Guid>> ResultsAsync(
        RioCommerceDbContext db, Guid? facultyId = null, Guid? subjectId = null)
    {
        var page = await new ProductRepository(db).GetFilteredAsync(new ProductFilterRequest
        {
            FacultyId = facultyId, SubjectId = subjectId, Page = 1, PageSize = 100,
        });
        return page.Items.Select(i => i.Id).ToHashSet();
    }

    // ── The behaviour that changed ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThePrimaryTeacherSeesTheCoTaughtCourse()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await ResultsAsync(db, facultyId: f.Harshad);

        Assert.Contains(f.CoTaught, ids);
        Assert.Contains(f.SoloAudit, ids);
    }

    [Fact]
    public async Task TheCoTeacherAlsoSeesIt()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var ids = await ResultsAsync(db, facultyId: f.Nipurn);

        // This is the whole point of the change — under PrimaryFacultyId this returned nothing.
        Assert.Equal(new[] { f.CoTaught }, ids.ToArray());
    }

    [Fact]
    public async Task AnUnmappedFacultySeesNothingOfIt()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        Assert.Empty(await ResultsAsync(db, facultyId: f.Unrelated));
        Assert.DoesNotContain(f.CoTaught, await ResultsAsync(db, facultyId: f.Tejal));
    }

    [Fact]
    public async Task NoFacultyFilterStillReturnsEverything()
    {
        using var db = NewDb();
        await SeedAsync(db);

        Assert.Equal(3, (await ResultsAsync(db)).Count);
    }

    [Fact]
    public async Task FacultyAndSubjectIntersect()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        // Nipurn teaches only the Audit combo, so pairing him with Law must return nothing.
        Assert.Empty(await ResultsAsync(db, facultyId: f.Nipurn, subjectId: f.LawSub));
        Assert.Equal(new[] { f.CoTaught }, (await ResultsAsync(db, facultyId: f.Nipurn, subjectId: f.AuditSub)).ToArray());
    }

    [Fact]
    public async Task TheCourseIsNotDuplicatedForAMultiplyMappedFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        // A second mapping row for the same pair — a join instead of an EXISTS would return the
        // product twice and inflate the result count.
        db.Set<ProductFaculty>().Add(new ProductFaculty
        {
            Id = Guid.NewGuid(), ProductId = f.CoTaught, FacultyId = f.Nipurn, IsPrimary = false,
        });
        await db.SaveChangesAsync();

        var page = await new ProductRepository(db).GetFilteredAsync(new ProductFilterRequest
        {
            FacultyId = f.Nipurn, Page = 1, PageSize = 100,
        });

        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    // ── What must NOT have changed ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrimaryFacultyIdIsStillTheHeadlineTeacherOnTheCard()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var page = await new ProductRepository(db).GetFilteredAsync(new ProductFilterRequest
        {
            FacultyId = f.Nipurn, Page = 1, PageSize = 100,
        });

        // Found via the co-teaching mapping, but still displayed under its primary teacher — only
        // the matching rule changed, not what the card shows.
        Assert.Equal("CA Harshad Jaju", Assert.Single(page.Items).FacultyName);
    }

    [Fact]
    public async Task AProductWithNoFacultyMappingIsNeverMatched()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var orphan = Guid.NewGuid();
        db.Products.Add(new Product
        {
            Id = orphan, Title = "Orphan Book", Slug = "ob",
            Status = ProductStatus.Active, Level = CourseLevel.Books,
        });
        await db.SaveChangesAsync();

        Assert.DoesNotContain(orphan, await ResultsAsync(db, facultyId: f.Harshad));
        Assert.Contains(orphan, await ResultsAsync(db));   // but it is still on the unfiltered list
    }

    [Fact]
    public async Task InactiveProductsStayOutRegardlessOfMapping()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        (await db.Products.FirstAsync(p => p.Id == f.CoTaught)).Status = ProductStatus.Draft;
        await db.SaveChangesAsync();

        Assert.Empty(await ResultsAsync(db, facultyId: f.Nipurn));
    }
}
