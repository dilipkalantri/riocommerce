using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Catalog;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// A combo course genuinely teaches more than one subject — "CA Inter Audit &amp; Costing" is both —
/// but <c>Product.SubjectId</c> could only ever record one, so the course was invisible to everyone
/// browsing by the other. <see cref="ProductSubject"/> carries the full set alongside that column.
///
/// <para>The line these tests defend is where the two readings apply:</para>
/// <list type="bullet">
///   <item><b>Browsing, filtering, pickers</b> read every mapped subject, so the combo turns up under
///         Audit AND Costing.</item>
///   <item><b>Money</b> reads the primary only. A settlement or re-invoice that counted one combo
///         under two subjects would report revenue nobody earned twice.</item>
///   <item><b>A single-subject course behaves exactly as it did before</b> — one row, primary, and
///         identical results under either reading. That is what makes the change safe to deploy.</item>
/// </list>
/// </summary>
public class ProductMultiSubjectTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"multisubject-{Guid.NewGuid()}")
            .Options);

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────
    //
    //  Product              Primary subject   Also covers   Shape
    //  ────────────────────────────────────────────────────────────────────────────
    //  Audit Regular        Audit             —             plain, single subject
    //  Costing Regular      Costing           —             plain, single subject
    //  Audit+Costing COMBO  Audit             Costing       the case this exists for
    //  Orphan Book          (none)            —             no subject at all
    private sealed record Fx(Guid Audit, Guid Costing, Guid Law, Guid Single, Guid Combo, Guid Other, Guid Orphan);

    private static async Task<Fx> SeedAsync(RioCommerceDbContext db)
    {
        Guid audit = Guid.NewGuid(), costing = Guid.NewGuid(), law = Guid.NewGuid();
        db.Subjects.AddRange(
            new Subject { Id = audit, Name = "Audit", Slug = "audit", IsActive = true },
            new Subject { Id = costing, Name = "Costing", Slug = "costing", IsActive = true },
            new Subject { Id = law, Name = "Law", Slug = "law", IsActive = true });

        Guid single = Guid.NewGuid(), combo = Guid.NewGuid(), other = Guid.NewGuid(), orphan = Guid.NewGuid();
        db.Products.AddRange(
            Product(single, "CA Inter Audit Regular", audit),
            Product(combo, "CA Inter Audit & Costing COMBO", audit),
            Product(other, "CA Inter Costing Regular", costing),
            Product(orphan, "Orphan Book", null));

        // What the migration's backfill writes for every existing product: one row, primary.
        db.ProductSubjects.AddRange(
            Link(single, audit, true),
            Link(combo, audit, true),
            Link(combo, costing, false),   // ← the combo's second subject
            Link(other, costing, true));

        await db.SaveChangesAsync();
        return new Fx(audit, costing, law, single, combo, other, orphan);
    }

    private static Product Product(Guid id, string title, Guid? subjectId) => new()
    {
        Id = id, Title = title, Slug = $"p-{id:N}", SubjectId = subjectId,
        Level = CourseLevel.CaIntermediate, SellingPrice = 5000m, Status = ProductStatus.Active
    };

    private static ProductSubject Link(Guid productId, Guid subjectId, bool primary) => new()
    {
        Id = Guid.NewGuid(), ProductId = productId, SubjectId = subjectId, IsPrimary = primary
    };

    private static CatalogCascade.Selection Pick(Guid subjectId, CatalogCascade.SubjectMatch match) =>
        new() { SubjectIds = new[] { subjectId }, Subject = match };

    private static async Task<List<Guid>> MatchingAsync(
        RioCommerceDbContext db, CatalogCascade.Selection sel) =>
        await CatalogCascade.ProductsMatching(db, sel, CatalogCascade.Dimension.None)
            .Select(p => p.Id).ToListAsync();

    // ── 1. The whole point: a combo is found by every subject it teaches ─────────
    [Fact]
    public async Task A_combo_is_found_under_each_of_its_subjects()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var byAudit = await MatchingAsync(db, Pick(fx.Audit, CatalogCascade.SubjectMatch.Mapped));
        var byCosting = await MatchingAsync(db, Pick(fx.Costing, CatalogCascade.SubjectMatch.Mapped));

        Assert.Contains(fx.Combo, byAudit);
        Assert.Contains(fx.Combo, byCosting);      // ← was impossible before
        Assert.Contains(fx.Single, byAudit);
        Assert.Contains(fx.Other, byCosting);
        Assert.DoesNotContain(fx.Other, byAudit);
        Assert.DoesNotContain(fx.Orphan, byAudit);
    }

    // ── 2. Money reads the primary only ──────────────────────────────────────────
    [Fact]
    public async Task PrimaryOnly_never_pulls_a_combo_in_through_its_secondary_subject()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var byCosting = await MatchingAsync(db, Pick(fx.Costing, CatalogCascade.SubjectMatch.PrimaryOnly));

        // Audit is the combo's primary, so a Costing settlement must not sweep it in — that is the
        // double-count this mode exists to prevent.
        Assert.DoesNotContain(fx.Combo, byCosting);
        Assert.Contains(fx.Other, byCosting);
    }

    // ── 3. Single-subject products are untouched by the change ───────────────────
    [Fact]
    public async Task A_single_subject_product_matches_identically_under_both_readings()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        // Audit is the single course's only subject AND the combo's primary, so here the two
        // readings cannot disagree — which is exactly the guarantee for existing products.
        var mappedAudit = await MatchingAsync(db, Pick(fx.Audit, CatalogCascade.SubjectMatch.Mapped));
        var primaryAudit = await MatchingAsync(db, Pick(fx.Audit, CatalogCascade.SubjectMatch.PrimaryOnly));

        Assert.Contains(fx.Single, mappedAudit);
        Assert.Contains(fx.Single, primaryAudit);
        Assert.Equal(mappedAudit.OrderBy(x => x), primaryAudit.OrderBy(x => x));

        // The readings diverge on ONE product and one subject only: the combo, under the secondary
        // subject it would previously have been invisible to.
        var mappedCosting = await MatchingAsync(db, Pick(fx.Costing, CatalogCascade.SubjectMatch.Mapped));
        var primaryCosting = await MatchingAsync(db, Pick(fx.Costing, CatalogCascade.SubjectMatch.PrimaryOnly));

        Assert.Equal(new[] { fx.Combo }, mappedCosting.Except(primaryCosting).ToArray());
        Assert.Contains(fx.Other, primaryCosting);      // the plain Costing course is unaffected
    }

    // ── 4. The picker offers every subject the matched products cover ────────────
    [Fact]
    public async Task SubjectIdsFor_offers_both_of_a_combos_subjects()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var justTheCombo = db.Products.Where(p => p.Id == fx.Combo);

        var mapped = await CatalogCascade.SubjectIdsFor(db, justTheCombo).ToListAsync();
        var primary = await CatalogCascade
            .SubjectIdsFor(db, justTheCombo, CatalogCascade.SubjectMatch.PrimaryOnly).ToListAsync();

        Assert.Equal(2, mapped.Count);
        Assert.Contains(fx.Audit, mapped);
        Assert.Contains(fx.Costing, mapped);
        Assert.Equal(new[] { fx.Audit }, primary);      // financial screens see one
    }

    [Fact]
    public async Task A_subject_nobody_teaches_is_never_offered()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var all = await CatalogCascade.SubjectIdsFor(db, db.Products).ToListAsync();

        Assert.DoesNotContain(fx.Law, all);
        Assert.Equal(2, all.Count);
    }

    // ── 5. Mapped is the default, so existing callers widen without being edited ─
    [Fact]
    public void Mapped_is_the_default_reading()
    {
        Assert.Equal(CatalogCascade.SubjectMatch.Mapped, new CatalogCascade.Selection().Subject);
        Assert.Equal(CatalogCascade.SubjectMatch.Mapped, CatalogCascade.Selection.Of(subjectId: Guid.NewGuid()).Subject);
        Assert.Equal(CatalogCascade.SubjectMatch.PrimaryOnly,
            CatalogCascade.Selection.Of(subjectId: Guid.NewGuid(),
                subjectMatch: CatalogCascade.SubjectMatch.PrimaryOnly).Subject);
    }

    // ── 6. Nothing selected still means no narrowing at all ──────────────────────
    [Fact]
    public async Task An_empty_selection_leaves_every_product_in()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var all = await MatchingAsync(db, new CatalogCascade.Selection());

        Assert.Equal(4, all.Count);      // the subject-less book included
    }

    // ── 7. Cross-dimension intersection still holds ──────────────────────────────
    [Fact]
    public async Task A_subject_still_intersects_with_the_other_dimensions()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        db.Products.First(p => p.Id == fx.Combo).Level = CourseLevel.CaFinal;
        await db.SaveChangesAsync();

        var inter = await MatchingAsync(db, new CatalogCascade.Selection
        {
            SubjectIds = new[] { fx.Costing },
            Levels = new[] { CourseLevel.CaIntermediate }
        });

        // Costing ∩ CaIntermediate — the combo covers Costing but is now CaFinal, so it drops out.
        Assert.Equal(new[] { fx.Other }, inter);
    }

    // ── 8. The primary is what the storefront card and the money reports read ────
    [Fact]
    public async Task The_primary_row_agrees_with_Product_SubjectId()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var combo = await db.Products.FirstAsync(p => p.Id == fx.Combo);
        var primaryLink = await db.ProductSubjects.SingleAsync(ps => ps.ProductId == fx.Combo && ps.IsPrimary);

        Assert.Equal(fx.Audit, combo.SubjectId);
        Assert.Equal(combo.SubjectId, primaryLink.SubjectId);
        Assert.Single(await db.ProductSubjects.Where(ps => ps.ProductId == fx.Combo && ps.IsPrimary).ToListAsync());
    }
}
