using RioCommerce.Infrastructure.Services.Catalog;
using RioCommerce.Core.Entities;
using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// Routes a report request to the builder that owns it, and supplies the shared filter bar's
/// lookup lists.
///
/// <para>The builders are injected as a set, so registering a new one is the whole cost of adding
/// a ninth report — the page, filter bar, pager and both exporters need no change (§29).</para>
/// </summary>
public class ReportingService : IReportingService
{
    private readonly Dictionary<ReportType, IReportBuilder> _builders;
    private readonly RioCommerceDbContext _db;

    internal ReportingService(IEnumerable<IReportBuilder> builders, RioCommerceDbContext db)
    {
        _builders = builders.ToDictionary(b => b.Type);
        _db = db;
    }

    public Task<ReportTable> RunAsync(ReportType type, ReportQuery query, CancellationToken ct = default)
    {
        if (!_builders.TryGetValue(type, out var builder))
            throw new ArgumentOutOfRangeException(nameof(type), type, "No builder is registered for this report.");

        // A hand-edited page or size must not be able to ask for a negative offset or pull the
        // whole table down in one request. Zero stays meaningful — it is how export asks for
        // everything — so only positive sizes are capped.
        if (query.Page < 1) query.Page = 1;
        if (query.PageSize > 500) query.PageSize = 500;

        return builder.BuildAsync(query, ct);
    }

    public async Task<ReportFilterOptions> GetFilterOptionsAsync(ReportQuery? query = null, CancellationToken ct = default)
    {
        // Active-only, because these are pickers for running a report rather than an audit of
        // everything that ever existed. Products are the exception — a discontinued course still
        // has sales history worth reporting on, so all of them stay selectable.
        //
        // ── Cascade ──────────────────────────────────────────────────────────────────────────────
        // Each of the three catalog pickers narrows to what the OTHER two allow. A dimension never
        // applies its own selection: doing so would leave the one product you picked as the only
        // one on the list, and a second could never be added.
        //
        // When nothing constrains a dimension the query is left completely alone, so an untouched
        // filter bar returns exactly the lists it always did — including faculty and subjects that
        // have no products at all, which a cascade would otherwise quietly drop.

        var picked = Selecting(query);

        var faculty = _db.Faculty.AsNoTracking().Where(f => f.IsActive);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Faculty))
        {
            var ids = CatalogCascade.FacultyIdsFor(_db,
                CatalogCascade.ProductsMatching(_db, picked!, CatalogCascade.Dimension.Faculty));
            faculty = faculty.Where(f => ids.Contains(f.Id));
        }
        var facultyList = await faculty
            .OrderBy(f => f.DisplayName)
            .Select(f => new ReportOption(f.Id, f.DisplayName))
            .ToListAsync(ct);

        var productQuery = CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Product)
            ? CatalogCascade.ProductsMatching(_db, picked!, CatalogCascade.Dimension.Product)
            : _db.Products.AsNoTracking();
        var products = await productQuery
            .OrderBy(p => p.Title)
            .Select(p => new ReportOption(p.Id, p.Title))
            .ToListAsync(ct);

        var subjects = _db.Subjects.AsNoTracking().Where(s => s.IsActive);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Subject))
        {
            var subjectIds = CatalogCascade.SubjectIdsFor(_db,
                CatalogCascade.ProductsMatching(_db, picked!, CatalogCascade.Dimension.Subject));
            subjects = subjects.Where(s => subjectIds.Contains(s.Id));
        }
        var subjectList = await subjects
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new ReportOption(s.Id, s.Name))
            .ToListAsync(ct);

        var franchises = await _db.Franchises.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new ReportOption(f.Id, f.Name + " (" + f.Code + ")"))
            .ToListAsync(ct);

        // Couriers are free text on the shipment, so the list is whatever has actually been used —
        // a fixed dropdown would go stale the first time someone types a new partner's name.
        var couriers = await _db.Shipments.AsNoTracking()
            .Where(s => s.Courier != null && s.Courier != "")
            .Select(s => s.Courier!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return new ReportFilterOptions
        {
            Faculty = facultyList,
            Products = products,
            Subjects = subjectList,
            Franchises = franchises,
            Couriers = couriers
        };
    }

    /// <summary>Maps the report's own query onto the shared cascade's vocabulary. Reports carry no
    /// Category or Level filter, so those stay empty here.</summary>
    private static CatalogCascade.Selection? Selecting(ReportQuery? q) => q == null ? null : new()
    {
        FacultyIds = q.FacultyIds,
        ProductIds = q.ProductIds,
        SubjectIds = q.SubjectIds,
    };
}
