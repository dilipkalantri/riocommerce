using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The Reports filter bar's three catalog pickers narrow each other.
///
/// <para>Faculty, Product and Subject each offer only what the OTHER two allow, so picking a faculty
/// stops the Product list offering courses that faculty does not teach. Franchisee, Courier and every
/// status filter stay independent — none has a catalog relationship to cascade through, and narrowing
/// them would mean an orders query on every keystroke.</para>
///
/// <para>A dimension never applies its own selection. If it did, choosing one product would leave
/// that product as the only entry on the list and a second could never be added.</para>
/// </summary>
public class ReportFilterCascadeTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"cascade-{Guid.NewGuid()}")
            .Options);

    private static IReportingService Svc(RioCommerceDbContext db) =>
        new ReportingService(Array.Empty<IReportBuilder>(), db);

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────
    //
    //  Faculty          Subject      Product
    //  ───────────────────────────────────────────────────
    //  Harshad          Audit        Audit Fastrack
    //  Harshad          Costing      Costing Fastrack
    //  Tejal            Law          Foundation Law
    //  Harshad + Tejal  Law          Law Crash Course     ← co-taught
    //  (none)           (none)       Orphan Book          ← no faculty, no subject

    private sealed record Fixture(
        Guid Harshad, Guid Tejal, Guid Unassigned,
        Guid AuditSub, Guid CostingSub, Guid LawSub, Guid EmptySub,
        Guid AuditFastrack, Guid CostingFastrack, Guid FoundationLaw, Guid LawCrash, Guid OrphanBook);

    private static async Task<Fixture> SeedAsync(RioCommerceDbContext db)
    {
        var f = new Fixture(
            Harshad: Guid.NewGuid(), Tejal: Guid.NewGuid(), Unassigned: Guid.NewGuid(),
            AuditSub: Guid.NewGuid(), CostingSub: Guid.NewGuid(), LawSub: Guid.NewGuid(), EmptySub: Guid.NewGuid(),
            AuditFastrack: Guid.NewGuid(), CostingFastrack: Guid.NewGuid(),
            FoundationLaw: Guid.NewGuid(), LawCrash: Guid.NewGuid(), OrphanBook: Guid.NewGuid());

        db.Faculty.AddRange(
            new Faculty { Id = f.Harshad, DisplayName = "CA Harshad Jaju", IsActive = true },
            new Faculty { Id = f.Tejal, DisplayName = "CA CS Tejal Katariya", IsActive = true },
            // Teaches nothing at all — must still appear when no filter narrows the list.
            new Faculty { Id = f.Unassigned, DisplayName = "CA Nobody", IsActive = true });

        db.Subjects.AddRange(
            new Subject { Id = f.AuditSub, Name = "Audit", Slug = "audit", IsActive = true, DisplayOrder = 1 },
            new Subject { Id = f.CostingSub, Name = "Costing", Slug = "costing", IsActive = true, DisplayOrder = 2 },
            new Subject { Id = f.LawSub, Name = "Law", Slug = "law", IsActive = true, DisplayOrder = 3 },
            new Subject { Id = f.EmptySub, Name = "Unused", Slug = "unused", IsActive = true, DisplayOrder = 4 });

        db.Products.AddRange(
            new Product { Id = f.AuditFastrack, Title = "Audit Fastrack", Slug = "a", SubjectId = f.AuditSub },
            new Product { Id = f.CostingFastrack, Title = "Costing Fastrack", Slug = "c", SubjectId = f.CostingSub },
            new Product { Id = f.FoundationLaw, Title = "Foundation Law", Slug = "l", SubjectId = f.LawSub },
            new Product { Id = f.LawCrash, Title = "Law Crash Course", Slug = "lc", SubjectId = f.LawSub },
            new Product { Id = f.OrphanBook, Title = "Orphan Book", Slug = "ob", SubjectId = null });

        db.Set<ProductFaculty>().AddRange(
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.AuditFastrack, FacultyId = f.Harshad, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.CostingFastrack, FacultyId = f.Harshad, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.FoundationLaw, FacultyId = f.Tejal, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.LawCrash, FacultyId = f.Tejal, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.LawCrash, FacultyId = f.Harshad, IsPrimary = false });

        db.Franchises.AddRange(
            new Franchise { Id = Guid.NewGuid(), Name = "Zeroinfy", Code = "ZI" },
            new Franchise { Id = Guid.NewGuid(), Name = "CA Point", Code = "CAP" });

        await db.SaveChangesAsync();
        return f;
    }

    private static HashSet<Guid> Ids(List<ReportOption> options) => options.Select(o => o.Id).ToHashSet();

    // ── 15. Nothing selected → exactly today's lists ────────────────────────────────────────────

    [Fact]
    public async Task WithNoSelection_TheListsAreUnchanged()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery());

        // Including the faculty who teaches nothing and the subject nothing uses — a cascade that
        // always ran would quietly drop both.
        Assert.Equal(3, o.Faculty.Count);
        Assert.Contains(f.Unassigned, Ids(o.Faculty));
        Assert.Equal(4, o.Subjects.Count);
        Assert.Contains(f.EmptySub, Ids(o.Subjects));
        Assert.Equal(5, o.Products.Count);
    }

    [Fact]
    public async Task ANullQueryBehavesLikeNoSelection()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(null);

        Assert.Equal(3, o.Faculty.Count);
        Assert.Equal(5, o.Products.Count);
        Assert.Equal(4, o.Subjects.Count);
    }

    // ── 1 & 2. Faculty → Product, Faculty → Subject ─────────────────────────────────────────────

    [Fact]
    public async Task FacultyNarrowsProducts()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { FacultyIds = { f.Harshad } });

        Assert.Equal(
            new[] { f.AuditFastrack, f.CostingFastrack, f.LawCrash }.OrderBy(x => x),
            Ids(o.Products).OrderBy(x => x));
        Assert.DoesNotContain(f.FoundationLaw, Ids(o.Products));
        Assert.DoesNotContain(f.OrphanBook, Ids(o.Products));
    }

    [Fact]
    public async Task FacultyNarrowsSubjects()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { FacultyIds = { f.Harshad } });

        // Law survives because Harshad co-teaches the Law Crash Course.
        Assert.Equal(
            new[] { f.AuditSub, f.CostingSub, f.LawSub }.OrderBy(x => x),
            Ids(o.Subjects).OrderBy(x => x));
        Assert.DoesNotContain(f.EmptySub, Ids(o.Subjects));
    }

    // ── 3 & 4. Subject → Product, Subject → Faculty ─────────────────────────────────────────────

    [Fact]
    public async Task SubjectNarrowsProducts()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { SubjectIds = { f.LawSub } });

        Assert.Equal(
            new[] { f.FoundationLaw, f.LawCrash }.OrderBy(x => x),
            Ids(o.Products).OrderBy(x => x));
    }

    [Fact]
    public async Task SubjectNarrowsFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { SubjectIds = { f.AuditSub } });

        Assert.Equal(new[] { f.Harshad }, Ids(o.Faculty).ToArray());
    }

    // ── 5 & 6. Product → Faculty, Product → Subject ─────────────────────────────────────────────

    [Fact]
    public async Task ProductNarrowsFaculty()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { ProductIds = { f.LawCrash } });

        // Both teachers of the co-taught course.
        Assert.Equal(
            new[] { f.Harshad, f.Tejal }.OrderBy(x => x),
            Ids(o.Faculty).OrderBy(x => x));
    }

    [Fact]
    public async Task ProductNarrowsSubjects()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { ProductIds = { f.AuditFastrack } });

        Assert.Equal(new[] { f.AuditSub }, Ids(o.Subjects).ToArray());
    }

    // ── 11. A dimension never filters its own list ──────────────────────────────────────────────

    [Fact]
    public async Task SelectingOneProductStillOffersAllTheOthers()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { ProductIds = { f.AuditFastrack } });

        // Otherwise a second product could never be added to the selection.
        Assert.Equal(5, o.Products.Count);
    }

    [Fact]
    public async Task SelectingOneFacultyStillOffersAllTheOthers()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { FacultyIds = { f.Harshad } });

        Assert.Equal(3, o.Faculty.Count);
    }

    [Fact]
    public async Task SelectingOneSubjectStillOffersAllTheOthers()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { SubjectIds = { f.LawSub } });

        Assert.Equal(4, o.Subjects.Count);
    }

    // ── 7. Multiple selections ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoFacultySelectionsUnionTheirCourses()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { FacultyIds = { f.Harshad, f.Tejal } });

        // Union within a dimension — picking a second teacher widens, never narrows.
        Assert.Equal(4, o.Products.Count);
        Assert.DoesNotContain(f.OrphanBook, Ids(o.Products));
    }

    [Fact]
    public async Task DifferentDimensionsIntersect()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery
        {
            FacultyIds = { f.Harshad },
            SubjectIds = { f.LawSub },
        });

        // Harshad AND Law → only the course that is both.
        Assert.Equal(new[] { f.LawCrash }, Ids(o.Products).ToArray());
    }

    [Fact]
    public async Task AnImpossibleCombinationYieldsAnEmptyProductList()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery
        {
            FacultyIds = { f.Tejal },
            SubjectIds = { f.AuditSub },   // Tejal teaches no Audit
        });

        Assert.Empty(o.Products);
    }

    // ── 8. Removing a selection widens the lists again ──────────────────────────────────────────

    [Fact]
    public async Task ClearingTheFacultySelectionRestoresEveryProduct()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var svc = Svc(db);

        var narrowed = await svc.GetFilterOptionsAsync(new ReportQuery { FacultyIds = { f.Harshad } });
        Assert.Equal(3, narrowed.Products.Count);

        var widened = await svc.GetFilterOptionsAsync(new ReportQuery());
        Assert.Equal(5, widened.Products.Count);
    }

    // ── 9 & 10. Invalid selections dropped, valid ones preserved ────────────────────────────────
    //
    // The page prunes against the returned lists; these assert the lists give it the right answer.

    [Fact]
    public async Task AStillValidProductSelectionRemainsOnOffer()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        // Audit Fastrack was already picked; adding Harshad must not invalidate it.
        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery
        {
            ProductIds = { f.AuditFastrack },
            FacultyIds = { f.Harshad },
        });

        Assert.Contains(f.AuditFastrack, Ids(o.Products));
    }

    [Fact]
    public async Task ANowImpossibleProductSelectionDropsOffTheList()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        // Foundation Law was picked, then Harshad was chosen — he does not teach it.
        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery
        {
            ProductIds = { f.FoundationLaw },
            FacultyIds = { f.Harshad },
        });

        Assert.DoesNotContain(f.FoundationLaw, Ids(o.Products));
        Assert.Contains(f.AuditFastrack, Ids(o.Products));   // the rest survive
    }

    [Fact]
    public async Task OneInvalidSelectionDoesNotTakeTheValidOnesWithIt()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery
        {
            ProductIds = { f.AuditFastrack, f.FoundationLaw },
            FacultyIds = { f.Harshad },
        });

        var available = Ids(o.Products);
        Assert.Contains(f.AuditFastrack, available);       // kept
        Assert.DoesNotContain(f.FoundationLaw, available); // pruned
    }

    // ── 12, 13, 14. What must stay independent ──────────────────────────────────────────────────

    [Fact]
    public async Task FranchiseesAreNotNarrowedByAnyCatalogSelection()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var svc = Svc(db);

        var all = await svc.GetFilterOptionsAsync(new ReportQuery());
        var filtered = await svc.GetFilterOptionsAsync(new ReportQuery
        {
            FacultyIds = { f.Harshad }, ProductIds = { f.AuditFastrack }, SubjectIds = { f.AuditSub },
        });

        Assert.Equal(2, all.Franchises.Count);
        Assert.Equal(all.Franchises.Count, filtered.Franchises.Count);
    }

    [Fact]
    public async Task SelectingAFranchiseeDoesNotNarrowTheCatalogPickers()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var franchiseId = await db.Franchises.Select(x => x.Id).FirstAsync();

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { FranchiseIds = { franchiseId } });

        // Franchise has no catalog relationship — cascading it would mean an orders query per keystroke.
        Assert.Equal(3, o.Faculty.Count);
        Assert.Equal(5, o.Products.Count);
        Assert.Equal(4, o.Subjects.Count);
    }

    [Fact]
    public async Task CouriersAreNotNarrowedByAnyCatalogSelection()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var svc = Svc(db);

        var all = await svc.GetFilterOptionsAsync(new ReportQuery());
        var filtered = await svc.GetFilterOptionsAsync(new ReportQuery { FacultyIds = { f.Harshad } });

        Assert.Equal(all.Couriers, filtered.Couriers);
    }

    [Fact]
    public async Task StatusFiltersDoNotNarrowTheCatalogPickers()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery
        {
            OrderStatuses = { OrderStatus.Confirmed },
            PaymentStatuses = { PaymentStatus.Success },
            PaymentModes = { PaymentMode.Upi },
            Sources = { OrderSource.Website },
            ShippingStatuses = { ShipmentStatus.Delivered },
            Courier = "Delhivery",
        });

        // Status filters are fixed enums with no catalog relationship — they stay independent.
        Assert.Equal(3, o.Faculty.Count);
        Assert.Equal(5, o.Products.Count);
        Assert.Equal(4, o.Subjects.Count);
    }

    // ── Existing behaviour preserved ────────────────────────────────────────────────────────────

    [Fact]
    public async Task InactiveFacultyAndSubjectsStayHiddenWhenCascading()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var retired = Guid.NewGuid();
        db.Faculty.Add(new Faculty { Id = retired, DisplayName = "Retired", IsActive = false });
        db.Set<ProductFaculty>().Add(new ProductFaculty
        {
            Id = Guid.NewGuid(), ProductId = f.AuditFastrack, FacultyId = retired,
        });
        await db.SaveChangesAsync();

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { ProductIds = { f.AuditFastrack } });

        // The active-only rule still holds — the cascade narrows the list, it does not reopen it.
        Assert.DoesNotContain(retired, Ids(o.Faculty));
    }

    [Fact]
    public async Task ProductsWithNoSubjectAreNotOfferedUnderASubjectFilter()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var o = await Svc(db).GetFilterOptionsAsync(new ReportQuery { SubjectIds = { f.AuditSub } });

        Assert.DoesNotContain(f.OrphanBook, Ids(o.Products));
    }
}
