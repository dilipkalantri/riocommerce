using RioCommerce.Core.DTOs.Recommendations;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ProductRecommendationAdminService : IProductRecommendationAdminService
{
    private readonly RioCommerceDbContext _db;
    public ProductRecommendationAdminService(RioCommerceDbContext db) => _db = db;

    public async Task<List<ProductRecommendationItem>> ListAsync(Guid productId)
    {
        // LEFT-join the recommended product so the admin row can preview title / image / faculty / price.
        var rows = await (from r in _db.ProductRecommendations
                          where r.ProductId == productId
                          join p in _db.Products on r.RecommendedProductId equals p.Id
                          orderby r.Priority descending, r.DisplayOrder
                          select new
                          {
                              r.Id, r.RecommendedProductId, r.DisplayOrder, r.CustomTitle, r.Type,
                              r.BadgeText, r.BadgeColor, r.Priority, r.IsActive,
                              p.Title, p.SellingPrice,
                              Thumb = p.OfferImageUrl
                                      ?? p.HomeCardImageUrl
                                      ?? p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                      ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                              Faculty = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null
                          }).ToListAsync();

        return rows.Select(x => new ProductRecommendationItem
        {
            Id = x.Id,
            RecommendedProductId = x.RecommendedProductId,
            RecommendedTitle = x.Title,
            RecommendedThumbUrl = x.Thumb,
            RecommendedFacultyName = x.Faculty,
            RecommendedPrice = x.SellingPrice,
            DisplayOrder = x.DisplayOrder,
            CustomTitle = x.CustomTitle,
            Type = x.Type,
            BadgeText = x.BadgeText,
            BadgeColor = x.BadgeColor,
            Priority = x.Priority,
            IsActive = x.IsActive
        }).ToList();
    }

    public async Task<(bool ok, string? error)> SaveAsync(Guid productId, List<ProductRecommendationItem> incoming, Guid? actorId)
    {
        _db.ChangeTracker.Clear();
        if (productId == Guid.Empty) return (false, "Source product id missing.");

        // Sanity: source product must exist and not be soft-deleted
        if (!await _db.Products.AnyAsync(p => p.Id == productId)) return (false, "Source product not found.");

        // Reject self-references and dedupe by RecommendedProductId
        var clean = (incoming ?? new()).Where(x => x.RecommendedProductId != Guid.Empty && x.RecommendedProductId != productId)
                                       .GroupBy(x => x.RecommendedProductId).Select(g => g.First()).ToList();

        // Verify every referenced target product is currently published before persisting — never let a soft-deleted
        // target sneak back in via a stale admin payload.
        var ids = clean.Select(x => x.RecommendedProductId).ToList();
        var validIds = await _db.Products.Where(p => ids.Contains(p.Id) && p.Status == ProductStatus.Active)
                                          .Select(p => p.Id).ToListAsync();
        clean = clean.Where(x => validIds.Contains(x.RecommendedProductId)).ToList();

        var existing = await _db.ProductRecommendations.Where(r => r.ProductId == productId).ToListAsync();
        var keepIds = clean.Where(c => c.Id != Guid.Empty).Select(c => c.Id).ToHashSet();

        // Soft-delete rows the admin removed.
        foreach (var e in existing.Where(e => !keepIds.Contains(e.Id)))
        {
            e.IsDeleted = true; e.DeletedAt = DateTime.UtcNow; e.IsActive = false; e.UpdatedById = actorId;
        }

        var existingById = existing.ToDictionary(e => e.Id);
        foreach (var x in clean)
        {
            ProductRecommendation row;
            if (x.Id != Guid.Empty && existingById.TryGetValue(x.Id, out var found))
            {
                row = found; row.UpdatedById = actorId; row.IsDeleted = false; row.DeletedAt = null;
            }
            else
            {
                row = new ProductRecommendation { ProductId = productId, CreatedById = actorId };
                _db.ProductRecommendations.Add(row);
            }
            row.RecommendedProductId = x.RecommendedProductId;
            row.DisplayOrder = x.DisplayOrder;
            row.CustomTitle = Clean(x.CustomTitle);
            row.Type = x.Type;
            row.BadgeText = Clean(x.BadgeText);
            row.BadgeColor = Clean(x.BadgeColor);
            row.Priority = x.Priority;
            row.IsActive = x.IsActive;
        }

        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<RecommendationSearchHit>> SearchAsync(string query, Guid excludeProductId, int max = 12)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var q = query.Trim().ToLower();
        return await _db.Products.AsNoTracking()
            .Where(p => p.Id != excludeProductId && p.Status == ProductStatus.Active)
            .Where(p => p.Title.ToLower().Contains(q)
                     || (p.Sku != null && p.Sku.ToLower().Contains(q))
                     || (p.PrimaryFaculty != null && p.PrimaryFaculty.DisplayName.ToLower().Contains(q))
                     || (p.Category != null && p.Category.Name.ToLower().Contains(q)))
            .OrderBy(p => p.Title).Take(max)
            .Select(p => new RecommendationSearchHit(
                p.Id, p.Title, p.Sku,
                p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                p.Category != null ? p.Category.Name : null,
                p.OfferImageUrl
                    ?? p.HomeCardImageUrl
                    ?? p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                    ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                p.SellingPrice))
            .ToListAsync();
    }

    public async Task<List<RecommendationSearchHit>> ListAllActiveAsync(Guid excludeProductId, int max = 1000) =>
        await _db.Products.AsNoTracking()
            .Where(p => p.Id != excludeProductId && p.Status == ProductStatus.Active)
            .OrderBy(p => p.Title).Take(max)
            .Select(p => new RecommendationSearchHit(
                p.Id, p.Title, p.Sku,
                p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                p.Category != null ? p.Category.Name : null,
                p.OfferImageUrl
                    ?? p.HomeCardImageUrl
                    ?? p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                    ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                p.SellingPrice))
            .ToListAsync();

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public class ProductRecommendationService : IProductRecommendationService
{
    private readonly RioCommerceDbContext _db;
    public ProductRecommendationService(RioCommerceDbContext db) => _db = db;

    public Task<List<RecommendationCard>> GetForProductAsync(Guid productId, int max = 8) =>
        Build(_db.ProductRecommendations.AsNoTracking().Where(r => r.ProductId == productId && r.IsActive)
                .OrderByDescending(r => r.Priority).ThenBy(r => r.DisplayOrder), max);

    public Task<List<RecommendationCard>> GetForProductsAsync(IEnumerable<Guid> productIds, int max = 4)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return Task.FromResult(new List<RecommendationCard>());
        // De-dupe so we don't suggest a product the user already has in their cart.
        return Build(_db.ProductRecommendations.AsNoTracking()
                .Where(r => ids.Contains(r.ProductId) && r.IsActive && !ids.Contains(r.RecommendedProductId))
                .OrderByDescending(r => r.Priority).ThenBy(r => r.DisplayOrder), max);
    }

    private async Task<List<RecommendationCard>> Build(IQueryable<ProductRecommendation> baseQ, int max)
    {
        // Project + filter: only published recommended products survive (publish gate enforced here, not in admin save).
        var rows = await (from r in baseQ
                          join p in _db.Products on r.RecommendedProductId equals p.Id
                          where p.Status == ProductStatus.Active
                          select new
                          {
                              r.RecommendedProductId, p.Slug, p.Title, p.SellingPrice, p.Mrp,
                              p.BatchStatus,
                              r.Type, r.CustomTitle, r.BadgeText, r.BadgeColor,
                              FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                              CategoryName = p.Category != null ? p.Category.Name : null,
                              ImageUrl = p.OfferImageUrl
                                          ?? p.HomeCardImageUrl
                                          ?? p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                          ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault()
                          }).Take(max * 2).ToListAsync();
        // De-dup recommended products in case multiple rows from different source products surface the same target.
        return rows.GroupBy(x => x.RecommendedProductId).Select(g => g.First())
                   .Take(max)
                   .Select(x => new RecommendationCard(
                       x.RecommendedProductId, x.Slug, x.Title, x.FacultyName, x.CategoryName, x.ImageUrl,
                       x.SellingPrice, x.Mrp > 0 ? x.Mrp : (decimal?)null,
                       x.Mrp > 0 && x.Mrp > x.SellingPrice ? (int)Math.Round((x.Mrp - x.SellingPrice) / x.Mrp * 100) : 0,
                       0.0, 0,
                       x.BadgeText, x.BadgeColor, x.Type, x.CustomTitle, x.BatchStatus)).ToList();
    }
}
