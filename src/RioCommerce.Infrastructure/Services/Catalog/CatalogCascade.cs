using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Catalog;

/// <summary>
/// The one place that answers "given what the user has already picked, which catalog options are
/// still worth offering?".
///
/// <para>Extracted from the Reports filter bar so the Orders, Products and storefront pickers behave
/// identically instead of each growing its own near-miss version. The rules it encodes:</para>
///
/// <list type="bullet">
///   <item>Within one dimension the selected ids are a UNION — picking a second faculty widens.</item>
///   <item>Across dimensions they INTERSECT — that faculty AND that subject.</item>
///   <item>A dimension never applies its own selection, so a second value can always be added.</item>
///   <item>Nothing selected means no narrowing at all, leaving each screen's existing list untouched.</item>
/// </list>
///
/// <para>Faculty is read one of two ways, chosen per screen via <see cref="FacultyMatch"/>, because
/// the two halves of the system genuinely disagree: reports and the admin order list resolve a
/// faculty through <see cref="ProductFaculty"/> (so a co-taught course counts for both teachers),
/// while the storefront result query matches <c>Product.PrimaryFacultyId</c> only. A picker must use
/// whichever its own results use, or it will offer a name that returns nothing.</para>
///
/// <para>This is <b>catalog</b> only. Order history never defines which options exist: a course a
/// faculty teaches stays on offer even if nobody has bought it in the chosen date range, and the
/// results themselves are still filtered by whatever date/status filters the screen applies.</para>
/// </summary>
public static class CatalogCascade
{
    /// <summary>The dimension whose own list is being built, and whose selection is therefore ignored.</summary>
    public enum Dimension { None, Faculty, Product, Subject, Category, Level }

    /// <summary>
    /// How a screen decides that a product belongs to a faculty.
    ///
    /// <para>The two exist because the two halves of the system disagree, and a picker that
    /// disagrees with its own result list is worse than no cascade at all.</para>
    /// </summary>
    public enum FacultyMatch
    {
        /// <summary>Every teacher mapped to the product. What reports and the admin order list use —
        /// <c>OrderLineSource</c> filters this way, so a co-taught course counts for both teachers.</summary>
        Mapped,

        /// <summary>Only <c>Product.PrimaryFacultyId</c>. The storefront's own result query filters
        /// this way (<c>ProductRepository</c>), so offering a co-teacher there would put a name in
        /// the dropdown that returns nothing when picked.</summary>
        PrimaryOnly,
    }

    /// <summary>
    /// How a screen decides that a product belongs to a subject. Same shape, and same reason, as
    /// <see cref="FacultyMatch"/>.
    ///
    /// <para>A combo course covers several subjects, so browsing must find it under every one of
    /// them. Money must not: a settlement or re-invoice total that counted one combo under two
    /// subjects would report revenue that was never earned twice.</para>
    /// </summary>
    public enum SubjectMatch
    {
        /// <summary>Every subject linked through <see cref="ProductSubject"/>. Browsing, filtering
        /// and the pickers — a combo taught as Audit + Costing appears under both.</summary>
        Mapped,

        /// <summary>Only <c>Product.SubjectId</c>, the primary. What the storefront card shows, and
        /// what every financial report groups by, so those figures cannot move.</summary>
        PrimaryOnly,
    }

    /// <summary>What the user has picked. Every list is optional; empty means "All".</summary>
    public sealed class Selection
    {
        /// <summary>Defaults to <see cref="FacultyMatch.Mapped"/> — the admin/report behaviour.</summary>
        public FacultyMatch Faculty { get; init; } = FacultyMatch.Mapped;

        /// <summary>Defaults to <see cref="SubjectMatch.Mapped"/> so browsing finds a combo under
        /// every subject it teaches. Financial reports pass PrimaryOnly.</summary>
        public SubjectMatch Subject { get; init; } = SubjectMatch.Mapped;

        public IReadOnlyCollection<Guid> FacultyIds { get; init; } = Array.Empty<Guid>();
        public IReadOnlyCollection<Guid> ProductIds { get; init; } = Array.Empty<Guid>();
        public IReadOnlyCollection<Guid> SubjectIds { get; init; } = Array.Empty<Guid>();
        public IReadOnlyCollection<Guid> CategoryIds { get; init; } = Array.Empty<Guid>();
        public IReadOnlyCollection<CourseLevel> Levels { get; init; } = Array.Empty<CourseLevel>();

        /// <summary>Convenience for the screens whose pickers are single-select.</summary>
        public static Selection Of(
            Guid? facultyId = null, Guid? productId = null, Guid? subjectId = null,
            Guid? categoryId = null, CourseLevel? level = null,
            FacultyMatch facultyMatch = FacultyMatch.Mapped,
            SubjectMatch subjectMatch = SubjectMatch.Mapped) => new()
            {
                Faculty = facultyMatch,
                Subject = subjectMatch,
                FacultyIds = facultyId is { } f ? new[] { f } : Array.Empty<Guid>(),
                ProductIds = productId is { } p ? new[] { p } : Array.Empty<Guid>(),
                SubjectIds = subjectId is { } s ? new[] { s } : Array.Empty<Guid>(),
                CategoryIds = categoryId is { } c ? new[] { c } : Array.Empty<Guid>(),
                Levels = level is { } l ? new[] { l } : Array.Empty<CourseLevel>(),
            };
    }

    /// <summary>
    /// True when some OTHER dimension has a selection, so this one has something to narrow to.
    ///
    /// <para>False must leave the caller's query completely alone. That is what keeps an untouched
    /// filter bar identical to its previous behaviour, including entries — a faculty who teaches
    /// nothing, a subject no product uses — that a cascade would otherwise quietly drop.</para>
    /// </summary>
    public static bool Constrains(Selection? s, Dimension ignore)
    {
        if (s == null) return false;
        return (ignore != Dimension.Faculty && s.FacultyIds.Count > 0)
            || (ignore != Dimension.Product && s.ProductIds.Count > 0)
            || (ignore != Dimension.Subject && s.SubjectIds.Count > 0)
            || (ignore != Dimension.Category && s.CategoryIds.Count > 0)
            || (ignore != Dimension.Level && s.Levels.Count > 0);
    }

    /// <summary>
    /// Products satisfying every selection except <paramref name="ignore"/>'s own — the set every
    /// other picker's options are derived from.
    /// </summary>
    public static IQueryable<Product> ProductsMatching(RioCommerceDbContext db, Selection s, Dimension ignore)
    {
        var products = db.Products.AsNoTracking();

        if (ignore != Dimension.Product && s.ProductIds.Count > 0)
            products = products.Where(p => s.ProductIds.Contains(p.Id));

        if (ignore != Dimension.Subject && s.SubjectIds.Count > 0)
            products = s.Subject == SubjectMatch.PrimaryOnly
                ? products.Where(p => p.SubjectId != null && s.SubjectIds.Contains(p.SubjectId.Value))
                // The primary column is checked as well as the join table, deliberately. A product
                // whose links have not been written yet - one created before this table existed, on
                // a database the backfill has not reached - must still be found by the subject it
                // has always had. Where the links ARE present the primary is among them, so the
                // extra clause matches nothing new. That is what keeps this change purely additive:
                // no product becomes harder to find than it was.
                : products.Where(p => db.Set<ProductSubject>()
                        .Any(ps => ps.ProductId == p.Id && s.SubjectIds.Contains(ps.SubjectId))
                    || (p.SubjectId != null && s.SubjectIds.Contains(p.SubjectId.Value)));

        if (ignore != Dimension.Category && s.CategoryIds.Count > 0)
            products = products.Where(p => p.CategoryId != null && s.CategoryIds.Contains(p.CategoryId.Value));

        if (ignore != Dimension.Level && s.Levels.Count > 0)
            products = products.Where(p => s.Levels.Contains(p.Level));

        if (ignore != Dimension.Faculty && s.FacultyIds.Count > 0)
            products = s.Faculty == FacultyMatch.PrimaryOnly
                ? products.Where(p => p.PrimaryFacultyId != null && s.FacultyIds.Contains(p.PrimaryFacultyId.Value))
                : products.Where(p => db.Set<ProductFaculty>()
                    .Any(pf => pf.ProductId == p.Id && s.FacultyIds.Contains(pf.FacultyId)));

        return products;
    }

    /// <summary>
    /// Faculty ids for the given products, read the way <paramref name="match"/> asks.
    ///
    /// <para>Callers must pass the same mode they used to build <paramref name="products"/>, and the
    /// mode their own result query filters by — that agreement is the whole point of the flag.</para>
    /// </summary>
    public static IQueryable<Guid> FacultyIdsFor(
        RioCommerceDbContext db, IQueryable<Product> products, FacultyMatch match = FacultyMatch.Mapped)
        => match == FacultyMatch.PrimaryOnly
            ? products.Where(p => p.PrimaryFacultyId != null).Select(p => p.PrimaryFacultyId!.Value).Distinct()
            : db.Set<ProductFaculty>().AsNoTracking()
                 .Where(pf => products.Any(p => p.Id == pf.ProductId))
                 .Select(pf => pf.FacultyId)
                 .Distinct();

    /// <summary>
    /// Subject ids for the given products, read the way <paramref name="match"/> asks. Products with
    /// no subject at all drop out either way.
    ///
    /// <para>As with <see cref="FacultyIdsFor"/>, callers must pass the same mode their own result
    /// query filters by — otherwise the picker offers a subject that returns nothing.</para>
    /// </summary>
    public static IQueryable<Guid> SubjectIdsFor(
        RioCommerceDbContext db, IQueryable<Product> products, SubjectMatch match = SubjectMatch.Mapped)
        => match == SubjectMatch.PrimaryOnly
            ? products.Where(p => p.SubjectId != null).Select(p => p.SubjectId!.Value).Distinct()
            : db.Set<ProductSubject>().AsNoTracking()
                 .Where(ps => products.Any(p => p.Id == ps.ProductId))
                 .Select(ps => ps.SubjectId)
                 // Union with the primary column, for the same reason ProductsMatching checks it.
                 .Concat(products.Where(p => p.SubjectId != null).Select(p => p.SubjectId!.Value))
                 .Distinct();

    /// <summary>Category ids used by any of <paramref name="products"/>.</summary>
    public static IQueryable<Guid> CategoryIdsFor(IQueryable<Product> products)
        => products.Where(p => p.CategoryId != null).Select(p => p.CategoryId!.Value).Distinct();
}
