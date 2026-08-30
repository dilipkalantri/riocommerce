using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class AffiliateService : IAffiliateService
{
    private readonly RioCommerceDbContext _db;
    public AffiliateService(RioCommerceDbContext db) => _db = db;

    public async Task<List<AffiliateAdminItem>> ListAsync()
    {
        var rows = await _db.Affiliates.OrderByDescending(a => a.TotalEarned).ThenBy(a => a.Name).ToListAsync();
        return rows.Select(a => new AffiliateAdminItem(
            a.Id, a.Name, a.Code, a.Email, a.Phone, a.CommissionType, a.CommissionValue,
            a.TotalReferrals, a.TotalEarned, a.TotalPaid, a.TotalEarned - a.TotalPaid, a.IsActive)).ToList();
    }

    public async Task<AffiliateStats> StatsAsync() => new(
        await _db.Affiliates.CountAsync(),
        await _db.AffiliateReferrals.CountAsync(),
        await _db.AffiliateReferrals.SumAsync(r => (decimal?)r.Commission) ?? 0,
        await _db.AffiliateReferrals.Where(r => !r.IsPaid).SumAsync(r => (decimal?)r.Commission) ?? 0);

    public async Task<AffiliateEditModel?> GetAsync(Guid id)
    {
        var a = await _db.Affiliates.FirstOrDefaultAsync(x => x.Id == id);
        return a == null ? null : new AffiliateEditModel
        {
            Id = a.Id, Name = a.Name, Email = a.Email, Phone = a.Phone, Code = a.Code,
            CommissionType = a.CommissionType, CommissionValue = a.CommissionValue, IsActive = a.IsActive
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(AffiliateEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);
        if (m.CommissionValue <= 0) return (false, "Commission value must be greater than zero.", Guid.Empty);
        if (m.CommissionType == SharingType.Percentage && m.CommissionValue > 100)
            return (false, "A percentage commission can't exceed 100%.", Guid.Empty);

        var code = string.IsNullOrWhiteSpace(m.Code) ? await GenerateCodeAsync(m.Name) : m.Code.Trim().ToUpper();
        if (await _db.Affiliates.AnyAsync(a => a.Code.ToUpper() == code && a.Id != (m.Id ?? Guid.Empty)))
            return (false, "Another affiliate already uses that code.", Guid.Empty);

        Affiliate entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.Affiliates.FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new InvalidOperationException("Affiliate not found.");
        }
        else
        {
            entity = new Affiliate();
            _db.Affiliates.Add(entity);
        }

        entity.Name = m.Name.Trim();
        entity.Email = string.IsNullOrWhiteSpace(m.Email) ? null : m.Email.Trim();
        entity.Phone = string.IsNullOrWhiteSpace(m.Phone) ? null : m.Phone.Trim();
        entity.Code = code;
        entity.CommissionType = m.CommissionType;
        entity.CommissionValue = m.CommissionValue;
        entity.IsActive = m.IsActive;

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task ToggleAsync(Guid id)
    {
        var a = await _db.Affiliates.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return;
        a.IsActive = !a.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        if (await _db.AffiliateReferrals.AnyAsync(r => r.AffiliateId == id))
            return (false, "Can't delete — this affiliate has commission records. Deactivate it instead.");

        var a = await _db.Affiliates.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return (false, "Affiliate not found.");
        _db.Affiliates.Remove(a);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<AffiliateReferralItem>> ReferralsAsync(Guid? affiliateId = null)
    {
        var q = _db.AffiliateReferrals.Include(r => r.Affiliate).Include(r => r.Order).AsQueryable();
        if (affiliateId.HasValue) q = q.Where(r => r.AffiliateId == affiliateId.Value);
        return await q.OrderByDescending(r => r.CreatedAt)
            .Select(r => new AffiliateReferralItem(
                r.Id, r.Affiliate.Name, r.Affiliate.Code, r.OrderNumber, r.OrderAmount, r.Commission,
                r.Order.CouponCode, r.Order.DiscountAmount, r.IsPaid, r.CreatedAt))
            .ToListAsync();
    }

    public async Task MarkPaidAsync(Guid referralId)
    {
        var r = await _db.AffiliateReferrals.Include(x => x.Affiliate).FirstOrDefaultAsync(x => x.Id == referralId);
        if (r == null || r.IsPaid) return;
        r.IsPaid = true;
        r.PaidAt = DateTime.UtcNow;
        r.Affiliate.TotalPaid += r.Commission;
        await _db.SaveChangesAsync();
    }

    private async Task<string> GenerateCodeAsync(string name)
    {
        var baseCode = new string(name.ToUpper().Where(char.IsLetterOrDigit).Take(6).ToArray());
        if (baseCode.Length < 3) baseCode = "AFF";
        string code;
        do { code = $"{baseCode}{Random.Shared.Next(100, 999)}"; }
        while (await _db.Affiliates.AnyAsync(a => a.Code == code));
        return code;
    }
}
