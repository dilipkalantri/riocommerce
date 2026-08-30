using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Reviews;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ReviewService : IReviewService
{
    private readonly RioCommerceDbContext _db;
    public ReviewService(RioCommerceDbContext db) => _db = db;

    public async Task<ProductReviews> GetForProductAsync(Guid productId, Guid? currentUserId)
    {
        var approved = await _db.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();

        var summary = new ReviewSummary
        {
            RatingCount = approved.Count,
            AvgRating = approved.Count == 0 ? 0 : Math.Round((decimal)approved.Average(r => r.Rating), 2)
        };
        foreach (var r in approved)
            if (r.Rating is >= 1 and <= 5) summary.StarCounts[r.Rating - 1]++;

        var result = new ProductReviews
        {
            Summary = summary,
            Items = approved.Select(r => new ReviewItem(
                r.Id, r.AuthorName, r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.CreatedAt)).ToList()
        };

        if (currentUserId.HasValue)
        {
            var mine = await _db.Reviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == currentUserId.Value);
            if (mine != null) result.Mine = new MyReview(mine.Rating, mine.Title, mine.Comment, mine.Status);
        }
        return result;
    }

    public async Task<(bool ok, string? error)> SubmitAsync(Guid userId, SubmitReviewRequest req)
    {
        if (req.Rating is < 1 or > 5) return (false, "Please choose a rating between 1 and 5 stars.");
        if (string.IsNullOrWhiteSpace(req.Comment)) return (false, "Please write a short review.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId);
        if (product == null) return (false, "Course not found.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");

        var verified = await _db.Enrollments.AnyAsync(e => e.UserId == userId && e.ProductId == req.ProductId);
        var existing = await _db.Reviews.FirstOrDefaultAsync(r => r.ProductId == req.ProductId && r.UserId == userId);

        if (existing == null)
        {
            _db.Reviews.Add(new Review
            {
                ProductId = req.ProductId,
                UserId = userId,
                Rating = req.Rating,
                Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim(),
                Comment = req.Comment.Trim(),
                AuthorName = user.FullName,
                IsVerifiedPurchase = verified,
                Status = ReviewStatus.Pending
            });
        }
        else
        {
            existing.Rating = req.Rating;
            existing.Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
            existing.Comment = req.Comment.Trim();
            existing.IsVerifiedPurchase = verified;
            existing.Status = ReviewStatus.Pending;   // edited reviews go back through moderation
        }

        await _db.SaveChangesAsync();
        await RecomputeAsync(req.ProductId);          // an edited-then-pending review must drop out of the average
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<PagedResult<AdminReviewItem>> AdminListAsync(ReviewStatus? status, int page, int pageSize)
    {
        var q = _db.Reviews.Include(r => r.Product).AsQueryable();
        if (status.HasValue) q = q.Where(r => r.Status == status.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new AdminReviewItem(
                r.Id, r.Product.Title, r.AuthorName, r.Rating, r.Title, r.Comment,
                r.IsVerifiedPurchase, r.Status, r.CreatedAt)).ToListAsync();
        return new PagedResult<AdminReviewItem> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    public async Task<ReviewStats> AdminStatsAsync() => new(
        await _db.Reviews.CountAsync(r => r.Status == ReviewStatus.Pending),
        await _db.Reviews.CountAsync(r => r.Status == ReviewStatus.Approved),
        await _db.Reviews.CountAsync(r => r.Status == ReviewStatus.Rejected));

    public async Task ModerateAsync(Guid reviewId, bool approve)
    {
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review == null) return;
        review.Status = approve ? ReviewStatus.Approved : ReviewStatus.Rejected;
        await _db.SaveChangesAsync();
        await RecomputeAsync(review.ProductId);
        await _db.SaveChangesAsync();
    }

    private async Task RecomputeAsync(Guid productId)
    {
        var approved = await _db.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .Select(r => r.Rating).ToListAsync();
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return;
        product.RatingCount = approved.Count;
        product.AvgRating = approved.Count == 0 ? 0 : Math.Round((decimal)approved.Average(), 2);
    }
}
