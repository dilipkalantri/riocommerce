using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class WishlistService : IWishlistService
{
    private readonly RioCommerceDbContext _db;
    public WishlistService(RioCommerceDbContext db) => _db = db;

    public async Task<bool> ToggleAsync(Guid userId, Guid productId)
    {
        var existing = await _db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);
        if (existing != null)
        {
            _db.WishlistItems.Remove(existing);
            await _db.SaveChangesAsync();
            return false;
        }
        _db.WishlistItems.Add(new WishlistItem { UserId = userId, ProductId = productId });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task RemoveAsync(Guid userId, Guid productId)
    {
        var existing = await _db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);
        if (existing != null) { _db.WishlistItems.Remove(existing); await _db.SaveChangesAsync(); }
    }

    public Task<bool> IsInWishlistAsync(Guid userId, Guid productId) =>
        _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.ProductId == productId);

    public Task<int> CountAsync(Guid userId) =>
        _db.WishlistItems.CountAsync(w => w.UserId == userId);

    public async Task<List<ProductListItem>> GetAsync(Guid userId)
    {
        var ids = await _db.WishlistItems.Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt).Select(w => w.ProductId).ToListAsync();
        if (ids.Count == 0) return new();

        var products = await _db.Products
            .Where(p => ids.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Include(p => p.PrimaryFaculty).Include(p => p.Inclusions)
            .Select(p => new ProductListItem
            {
                Id = p.Id, Title = p.Title, Slug = p.Slug, Level = p.Level, CourseType = p.CourseType,
                FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                Mrp = p.Mrp, SellingPrice = p.SellingPrice,
                SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
                Badge = p.Badge, TotalOrders = p.TotalOrders,
                AvgRating = p.AvgRating, RatingCount = p.RatingCount, Status = p.Status,
                BatchStatus = p.BatchStatus,
                HomeCardImageUrl = p.HomeCardImageUrl,
                PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                  ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                InclusionTitles = p.Inclusions.OrderBy(i => i.DisplayOrder).Select(i => i.Title).ToList()
            }).ToListAsync();

        // preserve most-recently-added-first order
        return products.OrderBy(p => ids.IndexOf(p.Id)).ToList();
    }
}
