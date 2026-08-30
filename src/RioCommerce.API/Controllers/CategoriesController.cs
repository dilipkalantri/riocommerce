using RioCommerce.Core.Entities;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.API.Controllers;

// Public, read-only category lookup for the storefront (admin CRUD lives in CategoriesAdminController).
[ApiController]
[Route("api/categories")]
public class CategoriesController : ControllerBase
{
    private readonly RioCommerceDbContext _db;
    public CategoriesController(RioCommerceDbContext db) => _db = db;

    [HttpGet("{slug}")]
    // subjectId/facultyId are optional and reflect what the visitor has already picked on the page,
    // so the two dropdowns narrow each other within this category. Omitted — as every existing
    // caller does — the response is byte-for-byte what it was.
    public async Task<ActionResult<ApiResponse<CategoryBrowseView>>> Get(
        string slug, [FromQuery] Guid? subjectId = null, [FromQuery] Guid? facultyId = null)
    {
        var cat = await _db.Categories.AsNoTracking()
            .Where(x => x.Slug == slug && x.IsActive)
            .Select(x => new { x.Id, x.Name, x.Slug, x.Description, x.ImageUrl, x.ParentId })
            .FirstOrDefaultAsync();
        if (cat == null) return NotFound(ApiResponse<CategoryBrowseView>.Fail("Category not found"));

        // Same scope the storefront product query uses (category + descendants).
        var scope = await CategoryAndDescendantIdsAsync(cat.Id);

        // Filter options for THIS category, computed over its full ACTIVE product set (via the
        // ProductCategory mapping) — never from a paginated/filtered slice. AsNoTracking, DISTINCT in SQL.
        var inCategory = _db.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active && p.ProductCategories.Any(pc => scope.Contains(pc.CategoryId)));

        // Cascade WITHIN the category: picking a subject narrows the faculty list to whoever teaches
        // it here, and picking a faculty narrows the subjects to what they teach here. Each ignores
        // its own selection, so a different value in the same dropdown can always be chosen; with
        // nothing picked both lists are exactly what they were.
        //
        // Faculty is read through ProductFaculty, matching this page's own result query
        // (/api/products?FacultyId=… → ProductRepository). A co-taught course therefore appears for
        // every teacher mapped to it, on both the dropdown and the results.
        var forSubjects = facultyId is { } fid
            ? inCategory.Where(p => _db.Set<ProductFaculty>().Any(pf => pf.ProductId == p.Id && pf.FacultyId == fid))
            : inCategory;
        // Subject is read through ProductSubject for the same reason, and matching this page's own
        // result query (ProductRepository). A combo appears under every subject it teaches.
        var forFaculty = subjectId is { } sid
            ? inCategory.Where(p => _db.Set<ProductSubject>().Any(ps => ps.ProductId == p.Id && ps.SubjectId == sid)
                                 || p.SubjectId == sid)
            : inCategory;

        // DISTINCT in SQL over the small option set, then order in memory (EF can't translate
        // OrderBy after Distinct on a projected type combined with the correlated Any() filter).
        var subjectRows = await _db.Set<ProductSubject>()
            .Where(ps => forSubjects.Any(p => p.Id == ps.ProductId))
            .Select(ps => new { Id = ps.Subject.Id, ps.Subject.Name })
            .Concat(forSubjects.Where(p => p.SubjectId != null)
                .Select(p => new { Id = p.Subject!.Id, p.Subject.Name }))
            .Distinct().ToListAsync();
        var subjects = subjectRows.OrderBy(x => x.Name).Select(x => new IdName(x.Id, x.Name)).ToList();

        var facultyRows = await _db.Set<ProductFaculty>().AsNoTracking()
            .Where(pf => forFaculty.Any(p => p.Id == pf.ProductId) && pf.Faculty.IsActive)
            .Select(pf => new { Id = pf.Faculty.Id, Name = pf.Faculty.DisplayName })
            .Distinct().ToListAsync();
        var faculty = facultyRows.OrderBy(x => x.Name).Select(x => new IdName(x.Id, x.Name)).ToList();

        // ── Sub-category navigation cards ──
        // Direct active children of THIS category, ordered by DisplayOrder asc then Name asc
        // (deterministic tiebreak). One flat query — no N+1.
        var children = await ActiveChildrenAsync(cat.Id);

        // If this category is itself a leaf (no children) but has a parent, show sibling
        // navigation instead and highlight the current one — a single extra query, only for leaves.
        Guid? activeChildId = null;
        if (children.Count == 0 && cat.ParentId is Guid parentId)
        {
            children = await ActiveChildrenAsync(parentId);
            activeChildId = cat.Id;
        }

        var view = new CategoryBrowseView(cat.Id, cat.Name, cat.Slug, cat.Description, cat.ImageUrl,
            subjects, faculty, cat.ParentId, children, activeChildId);
        return Ok(ApiResponse<CategoryBrowseView>.Ok(view));
    }

    // Direct active child categories of a parent, storefront-projected and deterministically ordered.
    private Task<List<CategoryChildView>> ActiveChildrenAsync(Guid parentId) =>
        _db.Categories.AsNoTracking()
            .Where(c => c.ParentId == parentId && c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryChildView(c.Id, c.Name, c.Slug, c.Description, c.DisplayOrder))
            .ToListAsync();

    // The category id plus every descendant id (categories form a shallow tree → in-memory walk).
    private async Task<HashSet<Guid>> CategoryAndDescendantIdsAsync(Guid rootId)
    {
        var edges = await _db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var childrenOf = edges.ToLookup(e => e.ParentId);
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in childrenOf[current])
                if (result.Add(child.Id)) queue.Enqueue(child.Id);
        }
        return result;
    }
}
