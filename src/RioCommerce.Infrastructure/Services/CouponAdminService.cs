using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class CouponAdminService : ICouponAdminService
{
    private readonly RioCommerceDbContext _db;
    public CouponAdminService(RioCommerceDbContext db) => _db = db;

    public async Task<List<CouponAdminItem>> ListAsync()
    {
        var now = DateTime.UtcNow;
        var rows = await _db.Coupons.OrderByDescending(c => c.CreatedAt).ToListAsync();
        var affNames = await _db.Affiliates.ToDictionaryAsync(a => a.Id, a => a.Name);
        return rows.Select(c => new CouponAdminItem(
            c.Id, c.Code, c.Name, c.CouponType, c.Value, c.MaxDiscount, c.MinOrder,
            c.TotalLimit, c.PerUserLimit, c.TotalUsed, ToDateOnly(c.StartsAt), ToDateOnly(c.ExpiresAt),
            c.IsActive, c.ExpiresAt.HasValue && c.ExpiresAt < now,
            c.AffiliateId, c.AffiliateId.HasValue && affNames.TryGetValue(c.AffiliateId.Value, out var n) ? n : null,
            c.IsCustomerApplicable, c.IsFranchiseApplicable)).ToList();
    }

    public async Task<List<AffiliateOption>> AffiliateOptionsAsync() =>
        await _db.Affiliates.Where(a => a.IsActive).OrderBy(a => a.Name)
            .Select(a => new AffiliateOption(a.Id, a.Name, a.Code)).ToListAsync();

    public async Task<CouponStats> StatsAsync()
    {
        var now = DateTime.UtcNow;
        return new CouponStats(
            await _db.Coupons.CountAsync(),
            await _db.Coupons.CountAsync(c => c.IsActive && (c.ExpiresAt == null || c.ExpiresAt >= now)),
            await _db.Coupons.CountAsync(c => c.ExpiresAt != null && c.ExpiresAt < now),
            await _db.Coupons.SumAsync(c => (int?)c.TotalUsed) ?? 0);
    }

    public Task<List<CouponUsageRow>> UsageHistoryAsync(Guid couponId) =>
        _db.Orders.Where(o => o.CouponId == couponId).OrderByDescending(o => o.CreatedAt)
            .Select(o => new CouponUsageRow(o.Id, o.OrderNumber, o.TotalAmount, o.CreatedAt)).ToListAsync();

    public async Task<CouponEditModel?> GetAsync(Guid id)
    {
        var c = await _db.Coupons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return c == null ? null : new CouponEditModel
        {
            Id = c.Id, Code = c.Code, Name = c.Name, CouponType = c.CouponType, Value = c.Value,
            MaxDiscount = c.MaxDiscount, MinOrder = c.MinOrder, TotalLimit = c.TotalLimit,
            PerUserLimit = c.PerUserLimit, StartsAt = ToDateOnly(c.StartsAt), ExpiresAt = ToDateOnly(c.ExpiresAt),
            IsActive = c.IsActive, AffiliateId = c.AffiliateId,
            IsCustomerApplicable = c.IsCustomerApplicable, IsFranchiseApplicable = c.IsFranchiseApplicable
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(CouponEditModel m)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(m.Code)) return (false, "Coupon code is required.", Guid.Empty);
        var code = m.Code.Trim().ToUpper();
        if (m.Value <= 0) return (false, "Discount value must be greater than zero.", Guid.Empty);
        if (m.CouponType == SharingType.Percentage && m.Value > 100)
            return (false, "A percentage discount can't exceed 100%.", Guid.Empty);
        if (m.StartsAt.HasValue && m.ExpiresAt.HasValue && m.ExpiresAt < m.StartsAt)
            return (false, "Expiry date can't be before the start date.", Guid.Empty);

        if (!m.IsCustomerApplicable && !m.IsFranchiseApplicable)
            return (false, "Choose at least one audience — customers, franchisees, or both.", Guid.Empty);
        // An affiliate link is a customer-referral mechanism; there is no referral link into the
        // Franchise Portal, so the two settings can't both be on.
        if (m.AffiliateId.HasValue && m.IsFranchiseApplicable)
            return (false, "An affiliate-exclusive coupon can't also be a franchise coupon — it only applies through the affiliate's referral link.", Guid.Empty);

        if (await _db.Coupons.AnyAsync(c => c.Code.ToUpper() == code && c.Id != (m.Id ?? Guid.Empty)))
            return (false, "Another coupon already uses that code.", Guid.Empty);

        Coupon entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("Coupon not found.");
        }
        else
        {
            entity = new Coupon();
            _db.Coupons.Add(entity);
        }

        entity.Code = code;
        entity.Name = string.IsNullOrWhiteSpace(m.Name) ? null : m.Name.Trim();
        entity.CouponType = m.CouponType;
        entity.Value = m.Value;
        entity.MaxDiscount = m.CouponType == SharingType.Percentage ? m.MaxDiscount : null;
        entity.MinOrder = m.MinOrder < 0 ? 0 : m.MinOrder;
        entity.TotalLimit = m.TotalLimit;
        entity.PerUserLimit = m.PerUserLimit < 1 ? 1 : m.PerUserLimit;
        entity.StartsAt = ToUtcStart(m.StartsAt);
        entity.ExpiresAt = ToUtcEnd(m.ExpiresAt);
        entity.IsActive = m.IsActive;
        entity.AffiliateId = m.AffiliateId;
        entity.IsCustomerApplicable = m.IsCustomerApplicable;
        entity.IsFranchiseApplicable = m.IsFranchiseApplicable;

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task ToggleAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var c = await _db.Coupons.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return;
        c.IsActive = !c.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        if (await _db.Orders.AnyAsync(o => o.CouponId == id))
            return (false, "Can't delete — this coupon has been used on orders. Deactivate it instead.");

        var c = await _db.Coupons.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Coupon not found.");
        _db.Coupons.Remove(c);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // Coupons store timestamptz (Npgsql requires DateTimeKind.Utc). Treat a date as the whole day in UTC.
    private static DateTime? ToUtcStart(DateOnly? d) =>
        d.HasValue ? DateTime.SpecifyKind(d.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) : null;
    private static DateTime? ToUtcEnd(DateOnly? d) =>
        d.HasValue ? DateTime.SpecifyKind(d.Value.ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Utc) : null;
    private static DateOnly? ToDateOnly(DateTime? dt) =>
        dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;
}
