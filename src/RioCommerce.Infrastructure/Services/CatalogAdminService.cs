using System.Text;
using System.Text.RegularExpressions;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class CatalogAdminService : ICatalogAdminService
{
    private readonly RioCommerceDbContext _db;
    private readonly IPublicFileStorage _files;
    private readonly ISeoUrlService _seo;
    public CatalogAdminService(RioCommerceDbContext db, IPublicFileStorage files, ISeoUrlService seo) { _db = db; _files = files; _seo = seo; }

    // ── Categories ──

    public async Task<List<CategoryAdminItem>> ListCategoriesAsync()
    {
        // Product counts keyed by category (includes products assigned via the legacy single FK).
        var countsByLegacy = await _db.Products.Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // Also count products assigned via the new many-to-many join table.
        var countsByJoin = await _db.ProductCategories
            .GroupBy(pc => pc.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var cats = await _db.Categories.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync();
        var byParent = cats.ToLookup(c => c.ParentId);
        var nameMap = cats.ToDictionary(c => c.Id, c => c.Name);

        // Depth-first walk → flat list with depth and parent info for hierarchical UI rendering.
        var result = new List<CategoryAdminItem>();
        void Walk(Guid? parentId, int depth)
        {
            foreach (var c in byParent[parentId])
            {
                var legacyCount = countsByLegacy.TryGetValue(c.Id, out var n1) ? n1 : 0;
                var joinCount = countsByJoin.TryGetValue(c.Id, out var n2) ? n2 : 0;
                var productCount = Math.Max(legacyCount, joinCount); // avoid double-counting

                result.Add(new CategoryAdminItem(
                    c.Id, c.Name, c.Slug, c.Description, c.DisplayOrder, c.IsActive, productCount,
                    c.ParentId,
                    c.ParentId.HasValue && nameMap.TryGetValue(c.ParentId.Value, out var pn) ? pn : null,
                    depth));
                Walk(c.Id, depth + 1);
            }
        }
        Walk(null, 0);

        // Surface any orphans whose parent was deleted/missing at root level so nothing disappears.
        var known = cats.Select(c => c.Id).ToHashSet();
        foreach (var c in cats.Where(c => c.ParentId != null && !known.Contains(c.ParentId!.Value)))
        {
            if (result.All(r => r.Id != c.Id))
            {
                var count = countsByLegacy.TryGetValue(c.Id, out var n1) ? n1 : (countsByJoin.TryGetValue(c.Id, out var n2) ? n2 : 0);
                result.Add(new CategoryAdminItem(c.Id, c.Name, c.Slug, c.Description, c.DisplayOrder, c.IsActive, count));
            }
        }

        return result;
    }

    public async Task<CategoryEditModel?> GetCategoryAsync(Guid id)
    {
        var c = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return c == null ? null : new CategoryEditModel
        {
            Id = c.Id, Name = c.Name, Slug = c.Slug, Description = c.Description,
            ImageUrl = c.ImageUrl, ParentId = c.ParentId, DisplayOrder = c.DisplayOrder, IsActive = c.IsActive,
            ShowOnHomePage = c.ShowOnHomePage, IncludeInTopMenu = c.IncludeInTopMenu,
            SeoTitle = c.SeoTitle, SeoKeywords = c.SeoKeywords, SeoDescription = c.SeoDescription
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveCategoryAsync(CategoryEditModel m)
    {
        _db.ChangeTracker.Clear();   // self-contained write on the circuit-lived context
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);
        var slug = string.IsNullOrWhiteSpace(m.Slug) ? Slugify(m.Name) : Slugify(m.Slug);

        // Global URL check: one public URL = one owner (across ALL entity types), excluding self.
        var url = await _seo.CheckAsync(slug, SeoEntityTypes.Category, m.Id);
        if (!url.IsAvailable)
        {
            var who = url.IsReserved ? "a reserved system route" : $"{url.ExistingEntityType}: {url.ExistingEntityName}";
            return (false, $"The URL “/{url.NormalizedSlug}” is already used by {who}. Suggested available URL: /{url.SuggestedSlug}", Guid.Empty);
        }

        // Parent must not be the category itself or one of its descendants (would create a cycle).
        if (m.ParentId is { } pid && m.Id is { } self && pid != Guid.Empty && self != Guid.Empty)
        {
            if (pid == self || (await DescendantCategoryIdsAsync(self)).Contains(pid))
                return (false, "A category can't be its own parent or a child of itself.", Guid.Empty);
        }

        Category entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("Category not found.");
        }
        else
        {
            entity = new Category();
            _db.Categories.Add(entity);
        }

        var newImage = string.IsNullOrWhiteSpace(m.ImageUrl) ? null : m.ImageUrl.Trim();
        if (!string.IsNullOrEmpty(entity.ImageUrl) && entity.ImageUrl != newImage) _files.Delete(entity.ImageUrl);

        entity.Name = m.Name.Trim();
        entity.Slug = slug;
        entity.Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim();
        entity.ImageUrl = newImage;
        entity.ParentId = m.ParentId == Guid.Empty ? null : m.ParentId;
        entity.DisplayOrder = m.DisplayOrder;
        entity.IsActive = m.IsActive;
        entity.ShowOnHomePage = m.ShowOnHomePage;
        entity.IncludeInTopMenu = m.IncludeInTopMenu;
        entity.SeoTitle = string.IsNullOrWhiteSpace(m.SeoTitle) ? null : m.SeoTitle.Trim();
        entity.SeoKeywords = string.IsNullOrWhiteSpace(m.SeoKeywords) ? null : m.SeoKeywords.Trim();
        entity.SeoDescription = string.IsNullOrWhiteSpace(m.SeoDescription) ? null : m.SeoDescription.Trim();

        await _db.SaveChangesAsync();
        // Register/point this category's public URL in the global registry (preserves old URL as a 301 on change).
        await _seo.RegisterOrUpdateAsync(SeoEntityTypes.Category, entity.Id, entity.Name, slug);
        return (true, null, entity.Id);
    }

    public async Task ToggleCategoryAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return;
        c.IsActive = !c.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteCategoryAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        if (await _db.Products.AnyAsync(p => p.CategoryId == id) || await _db.ProductCategories.AnyAsync(pc => pc.CategoryId == id))
            return (false, "Can't delete — courses are still assigned to this category.");
        if (await _db.Categories.AnyAsync(c => c.ParentId == id))
            return (false, "Can't delete — it has sub-categories.");

        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Category not found.");
        _files.Delete(c.ImageUrl);
        _db.Categories.Remove(c);
        await _db.SaveChangesAsync();
        await _seo.ReleaseAsync(SeoEntityTypes.Category, id);   // free the public URL
        return (true, null);
    }

    // ── Category editor support ──
    public async Task<List<IdName>> GetCategoryParentOptionsAsync(Guid? excludeId)
    {
        var exclude = excludeId is { } id && id != Guid.Empty ? await DescendantCategoryIdsAsync(id) : new HashSet<Guid>();
        var cats = await _db.Categories.AsNoTracking().OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        return cats.Where(c => !exclude.Contains(c.Id)).Select(c => new IdName(c.Id, c.Name)).ToList();
    }

    public Task<string> UploadCategoryImageAsync(string extension, Stream content) =>
        _files.SaveAsync("categories", extension, content);

    // Products assigned to THIS category via the ProductCategory mapping. DisplayOrder is the
    // per-category order held on the mapping row (nopCommerce-style), ordered before Name.
    public Task<List<CategoryProductRow>> ListCategoryProductsAsync(Guid categoryId) =>
        _db.ProductCategories.AsNoTracking().Where(pc => pc.CategoryId == categoryId)
            .OrderBy(pc => pc.DisplayOrder).ThenBy(pc => pc.Product.Title)
            .Select(pc => new CategoryProductRow(
                pc.Product.Id, pc.Product.Title, pc.Product.Slug, pc.Product.IsFeatured, pc.Product.SellingPrice, pc.DisplayOrder))
            .ToListAsync();

    public async Task<List<IdName>> SearchUnassignedProductsAsync(Guid categoryId, string? query, int take = 20)
    {
        // Exclude products already mapped to this category.
        var q = _db.Products.AsNoTracking().Where(p => !p.ProductCategories.Any(pc => pc.CategoryId == categoryId));
        if (!string.IsNullOrWhiteSpace(query)) q = q.Where(p => EF.Functions.ILike(p.Title, $"%{query.Trim()}%"));
        return await q.OrderBy(p => p.Title).Take(take).Select(p => new IdName(p.Id, p.Title)).ToListAsync();
    }

    public async Task AssignProductAsync(Guid categoryId, Guid productId)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Products.Include(x => x.ProductCategories).FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return;
        if (p.ProductCategories.Any(pc => pc.CategoryId == categoryId)) return;   // already mapped — idempotent

        p.ProductCategories.Add(new ProductCategory
        {
            ProductId = p.Id,
            CategoryId = categoryId,
            IsPrimary = p.ProductCategories.Count == 0,   // first mapping becomes primary
            DisplayOrder = 0                               // default; admin sets it in the grid (no auto-renumber)
        });
        // Keep the legacy single-category FK populated for code paths still reading it.
        p.CategoryId ??= categoryId;
        await _db.SaveChangesAsync();
    }

    public async Task UnassignProductAsync(Guid categoryId, Guid productId)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Products.Include(x => x.ProductCategories).FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return;
        var row = p.ProductCategories.FirstOrDefault(pc => pc.CategoryId == categoryId);
        if (row == null) return;
        p.ProductCategories.Remove(row);
        _db.ProductCategories.Remove(row);
        // Keep the legacy FK sane: if it pointed here, move it to any remaining mapping (or null).
        if (p.CategoryId == categoryId)
            p.CategoryId = p.ProductCategories.FirstOrDefault(pc => pc.CategoryId != categoryId)?.CategoryId;
        await _db.SaveChangesAsync();
    }

    // Updates ONLY the ProductCategory mapping rows for this category — never the product's own data.
    public async Task SaveCategoryProductOrderAsync(Guid categoryId, IReadOnlyList<CategoryProductOrder> orders)
    {
        _db.ChangeTracker.Clear();
        if (orders.Count == 0) return;
        var byProduct = orders.GroupBy(o => o.ProductId).ToDictionary(g => g.Key, g => g.Last().DisplayOrder);
        var productIds = byProduct.Keys.ToList();
        var rows = await _db.ProductCategories
            .Where(pc => pc.CategoryId == categoryId && productIds.Contains(pc.ProductId))
            .ToListAsync();
        foreach (var row in rows)
            if (byProduct.TryGetValue(row.ProductId, out var order)) row.DisplayOrder = order;
        await _db.SaveChangesAsync();
    }

    private async Task<HashSet<Guid>> DescendantCategoryIdsAsync(Guid rootId)
    {
        var edges = await _db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var byParent = edges.ToLookup(c => c.ParentId);
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var child in byParent[id])
                if (result.Add(child.Id)) queue.Enqueue(child.Id);
        }
        return result;
    }

    // ── Faculty ──

    public async Task<List<FacultyAdminItem>> ListFacultyAsync()
    {
        var counts = await _db.Products.Where(p => p.PrimaryFacultyId != null)
            .GroupBy(p => p.PrimaryFacultyId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var faculty = await _db.Faculty.OrderBy(f => f.DisplayOrder).ThenBy(f => f.DisplayName).ToListAsync();
        return faculty.Select(f => new FacultyAdminItem(
            f.Id, f.DisplayName, f.ShortCode, f.Designation, f.Qualifications, f.ShortDescription,
            counts.TryGetValue(f.Id, out var n) ? n : 0, f.DisplayOrder, f.IsActive)).ToList();
    }

    public async Task<FacultyEditModel?> GetFacultyAsync(Guid id)
    {
        var f = await _db.Faculty.FirstOrDefaultAsync(x => x.Id == id);
        return f == null ? null : new FacultyEditModel
        {
            Id = f.Id, DisplayName = f.DisplayName, ShortCode = f.ShortCode, Designation = f.Designation,
            Qualifications = f.Qualifications, ShortDescription = f.ShortDescription, Bio = f.Bio,
            PhotoUrl = f.PhotoUrl, YoutubeUrl = f.YoutubeUrl,
            WhatsappNumber = f.WhatsappNumber, CallNumber = f.CallNumber,
            SubjectsCsv = f.Subjects == null ? null : string.Join(", ", f.Subjects),
            YearsOfExperience = f.YearsOfExperience, StudentsTaught = f.StudentsTaught,
            HoursOfTeaching = f.HoursOfTeaching, StudentSatisfaction = f.StudentSatisfaction,
            AirHoldersNote = f.AirHoldersNote,
            DisplayOrder = f.DisplayOrder, IsActive = f.IsActive, ShowOnHomePage = f.ShowOnHomePage
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveFacultyAsync(FacultyEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.DisplayName)) return (false, "Display name is required.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(m.ShortCode)) return (false, "Short code is required.", Guid.Empty);
        var code = m.ShortCode.Trim().ToUpper();

        if (await _db.Faculty.AnyAsync(f => f.ShortCode.ToUpper() == code && f.Id != (m.Id ?? Guid.Empty)))
            return (false, "Another faculty already uses that short code.", Guid.Empty);

        Faculty entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.Faculty.FirstOrDefaultAsync(f => f.Id == id)
                ?? throw new InvalidOperationException("Faculty not found.");
        }
        else
        {
            entity = new Faculty();
            _db.Faculty.Add(entity);
        }

        entity.DisplayName = m.DisplayName.Trim();
        entity.ShortCode = code;
        entity.Designation = Clean(m.Designation);
        entity.Qualifications = Clean(m.Qualifications);
        entity.ShortDescription = Clean(m.ShortDescription);
        entity.Bio = Clean(m.Bio);
        entity.PhotoUrl = Clean(m.PhotoUrl);
        entity.YoutubeUrl = Clean(m.YoutubeUrl);
        entity.WhatsappNumber = Clean(m.WhatsappNumber);
        entity.CallNumber = Clean(m.CallNumber);
        entity.Subjects = string.IsNullOrWhiteSpace(m.SubjectsCsv)
            ? null
            : m.SubjectsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        entity.YearsOfExperience = Clean(m.YearsOfExperience);
        entity.StudentsTaught = Clean(m.StudentsTaught);
        entity.HoursOfTeaching = Clean(m.HoursOfTeaching);
        entity.StudentSatisfaction = Clean(m.StudentSatisfaction);
        entity.AirHoldersNote = Clean(m.AirHoldersNote);
        entity.DisplayOrder = m.DisplayOrder;
        entity.IsActive = m.IsActive;
        entity.ShowOnHomePage = m.ShowOnHomePage;

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task ToggleFacultyAsync(Guid id)
    {
        var f = await _db.Faculty.FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return;
        f.IsActive = !f.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteFacultyAsync(Guid id)
    {
        if (await _db.Products.AnyAsync(p => p.PrimaryFacultyId == id))
            return (false, "Can't delete — this faculty is the primary teacher on one or more courses.");
        if (await _db.FacultySharingRules.AnyAsync(r => r.FacultyId == id))
            return (false, "Can't delete — revenue-sharing rules reference this faculty.");
        if (await _db.ProductFaculty.AnyAsync(pf => pf.FacultyId == id))
            return (false, "Can't delete — this faculty is linked to courses.");

        var f = await _db.Faculty.FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return (false, "Faculty not found.");
        _db.Faculty.Remove(f);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error, string? url)> UploadFacultyPhotoAsync(string extension, Stream content)
    {
        var ext = (extension ?? "").Trim().ToLowerInvariant().TrimStart('.');
        if (ext is not ("jpg" or "jpeg" or "png" or "webp" or "gif"))
            return (false, "Unsupported file type. Use JPG, PNG, WEBP or GIF.", null);
        try
        {
            var url = await _files.SaveAsync("faculty", ext, content);
            return (true, null, url);
        }
        catch (Exception ex)
        {
            return (false, "Upload failed: " + ex.Message, null);
        }
    }

    // ── helpers ──

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Slugify(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? Guid.NewGuid().ToString("n")[..8] : s;
    }
}
