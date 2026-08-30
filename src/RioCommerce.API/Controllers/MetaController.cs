using RioCommerce.Infrastructure.Services.Catalog;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetaController : ControllerBase
{
    private readonly RioCommerceDbContext _db;
    public MetaController(RioCommerceDbContext db) => _db = db;

    // Lightweight options for storefront/admin filter dropdowns.
    //
    // Every parameter is optional. Called with none — as the storefront always did — the lists are
    // byte-for-byte what they were, so any existing caller (including mobile) keeps working.
    //
    // Supplied, they cascade: Faculty narrows to whoever teaches what the other filters allow,
    // Subjects to what those courses cover, and the spec facets to options those courses actually
    // carry. Faculty resolves through ProductFaculty so a co-taught course keeps every teacher.
    // Level → Subject is NOT applied here — Courses.razor already does that client-side from
    // SubjectOption.Level, and duplicating it server-side would change that page's behaviour.
    [HttpGet("filters")]
    public async Task<ActionResult<ApiResponse<FilterOptions>>> Filters(
        [FromQuery] Guid? facultyId = null,
        [FromQuery] Guid? subjectId = null,
        [FromQuery] RioCommerce.Core.Enums.CourseLevel? level = null)
    {
        // Mapped, matching ProductRepository's own faculty filter — a co-taught course counts for
        // every teacher on both sides, so the picker and the result page agree.
        var picked = CatalogCascade.Selection.Of(facultyId: facultyId, subjectId: subjectId, level: level);

        // The storefront only ever offers live products, so the cascade is scoped to those too —
        // otherwise a retired course could put a faculty back on the list who has nothing to sell.
        IQueryable<RioCommerce.Core.Entities.Product> Matching(CatalogCascade.Dimension ignore) =>
            CatalogCascade.ProductsMatching(_db, picked, ignore)
                .Where(p => p.Status == RioCommerce.Core.Enums.ProductStatus.Active);

        var facultyQuery = _db.Faculty.Where(f => f.IsActive);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Faculty))
        {
            var ids = CatalogCascade.FacultyIdsFor(_db, Matching(CatalogCascade.Dimension.Faculty));
            facultyQuery = facultyQuery.Where(f => ids.Contains(f.Id));
        }
        var faculty = await facultyQuery
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new IdName(f.Id, f.DisplayName))
            .ToListAsync();

        var subjectQuery = _db.Subjects.Where(s => s.IsActive);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Subject))
        {
            var ids = CatalogCascade.SubjectIdsFor(_db, Matching(CatalogCascade.Dimension.Subject));
            subjectQuery = subjectQuery.Where(s => ids.Contains(s.Id));
        }
        var subjects = await subjectQuery
            .OrderBy(s => s.DisplayOrder)
            // Level stays on every row: Courses.razor filters subjects by it client-side and that
            // behaviour is deliberately untouched.
            .Select(s => new SubjectOption(s.Id, s.Name, s.Level))
            .ToListAsync();

        // Spec options actually marked filterable on an active product → render as storefront facets.
        var specSource = CatalogCascade.Constrains(picked, CatalogCascade.Dimension.None)
            ? Matching(CatalogCascade.Dimension.None)
            : _db.Products.Where(p => p.Status == RioCommerce.Core.Enums.ProductStatus.Active);

        var filterableOptionIds = await _db.ProductSpecificationAttributes
            .Where(psa => psa.AllowFiltering && psa.Product.Status == RioCommerce.Core.Enums.ProductStatus.Active
                       && specSource.Any(p => p.Id == psa.ProductId))
            .Select(psa => psa.SpecificationAttributeOptionId)
            .Distinct().ToListAsync();

        var specFilters = await _db.SpecificationAttributes
            .Where(sa => sa.Options.Any(o => filterableOptionIds.Contains(o.Id)))
            .OrderBy(sa => sa.DisplayOrder).ThenBy(sa => sa.Name)
            .Select(sa => new SpecFilterGroup(sa.Id, sa.Name,
                sa.Options.Where(o => filterableOptionIds.Contains(o.Id)).OrderBy(o => o.DisplayOrder)
                    .Select(o => new IdName(o.Id, o.Name)).ToList()))
            .ToListAsync();

        return Ok(ApiResponse<FilterOptions>.Ok(new FilterOptions(faculty, subjects, specFilters)));
    }
}
