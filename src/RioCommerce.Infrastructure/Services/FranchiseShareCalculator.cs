using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Pure, date-driven implementation of <see cref="IFranchiseShareCalculator"/>.
/// See the interface for the resolution-priority contract. Rounding is to 2 decimals at the
/// per-unit level so the portal display and the per-item order commission agree.
/// </summary>
public sealed class FranchiseShareCalculator : IFranchiseShareCalculator
{
    private readonly RioCommerceDbContext _db;

    public FranchiseShareCalculator(RioCommerceDbContext db) => _db = db;

    // ── Effective price ─────────────────────────────────────────────────────
    public bool IsSpecialPriceActive(Product product, DateTime? asOfUtc = null)
    {
        if (product.SpecialPrice is not { } sp || sp <= 0) return false;
        var now = asOfUtc ?? DateTime.UtcNow;
        if (product.SpecialPriceStartDateUtc is { } start && now < start) return false;
        if (product.SpecialPriceEndDateUtc is { } end && now > end) return false;
        return true;
    }

    public decimal EffectivePrice(Product product, DateTime? asOfUtc = null)
        => IsSpecialPriceActive(product, asOfUtc) ? product.SpecialPrice!.Value : product.SellingPrice;

    // ── Share calculation ───────────────────────────────────────────────────
    public FranchiseShareResult Calculate(
        Product product, FranchiseCommission? overrideRule, FranchiseSettings settings,
        bool franchiseeIsGstRegistered, DateTime? asOfUtc = null)
    {
        var specialActive = IsSpecialPriceActive(product, asOfUtc);
        // The base the share is applied to: the special price when active AND settings permit it,
        // otherwise always the regular price. (Display always reports the true effective price.)
        var effective = EffectivePrice(product, asOfUtc);
        var shareBase = settings.ApplyShareOnSpecialPrice ? effective : product.SellingPrice;
        var gstRate = product.GstRate;

        // Priority: franchise override (if allowed) → product default → none.
        CommissionType type;
        decimal value;
        ShareSource source;

        if (overrideRule is { IsActive: true } && settings.AllowFranchiseSpecificOverride)
        {
            type = overrideRule.Type;
            value = overrideRule.Value;
            source = ShareSource.FranchiseOverride;
        }
        else if (product.EnableDefaultFranchiseShare && product.DefaultFranchiseShareValue > 0)
        {
            type = product.DefaultFranchiseShareType;
            value = product.DefaultFranchiseShareValue;
            source = ShareSource.ProductDefault;
        }
        else
        {
            return FranchiseShareResult.NoShare(product.SellingPrice, product.SpecialPrice, effective, specialActive, gstRate);
        }

        // Steps 1–3 of the share specification. A percentage applies to the pre-GST base; a fixed
        // ₹ value is the agreed payout and is decomposed back out. GST rides on the commission
        // only when the franchisee is registered to charge it.
        var split = type == CommissionType.Percent
            ? FranchiseCommissionMath.FromPercent(shareBase, gstRate, value, franchiseeIsGstRegistered)
            : FranchiseCommissionMath.FromFixedPayout(value, gstRate, franchiseeIsGstRegistered);

        if (split.TotalPayout <= 0)
            return FranchiseShareResult.NoShare(product.SellingPrice, product.SpecialPrice, effective, specialActive, gstRate);

        return new FranchiseShareResult
        {
            ProductPrice = product.SellingPrice,
            SpecialPrice = product.SpecialPrice,
            EffectivePrice = effective,
            IsSpecialPriceActive = specialActive,
            ShareType = type,
            ShareValue = value,
            CalculatedFranchiseAmount = split.TotalPayout,
            GstRate = gstRate,
            FranchiseeIsGstRegistered = franchiseeIsGstRegistered,
            TaxableBase = FranchiseCommissionMath.BaseFromGross(shareBase, gstRate),
            CommissionAmount = split.Commission,
            GstOnCommission = split.GstOnCommission,
            Source = source
        };
    }

    // ── Portal listing ──────────────────────────────────────────────────────
    public async Task<List<FranchiseProductShareRow>> GetProductSharesAsync(
        Guid franchiseId, bool onlyWithShare = false, CancellationToken ct = default)
    {
        var settings = await _db.FranchiseSettings.AsNoTracking().FirstOrDefaultAsync(ct)
                       ?? new FranchiseSettings();

        // Whether this franchisee charges GST on their commission decides what they earn, so the
        // listing has to resolve it before pricing anything — otherwise the portal would quote a
        // registered franchisee's rate to an unregistered one.
        var gstin = await _db.Franchises.AsNoTracking()
            .Where(f => f.Id == franchiseId).Select(f => f.Gstin).FirstOrDefaultAsync(ct);
        var isGstRegistered = !string.IsNullOrWhiteSpace(gstin);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active)
            .Include(p => p.PrimaryFaculty)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title)
            .ToListAsync(ct);

        var rules = await _db.FranchiseCommissions.AsNoTracking()
            .Where(c => c.FranchiseId == franchiseId)
            .ToDictionaryAsync(c => c.ProductId, ct);

        var now = DateTime.UtcNow;
        var rows = new List<FranchiseProductShareRow>(products.Count);

        foreach (var p in products)
        {
            rules.TryGetValue(p.Id, out var rule);
            var r = Calculate(p, rule, settings, isGstRegistered, now);
            if (onlyWithShare && !r.HasShare) continue;

            rows.Add(new FranchiseProductShareRow
            {
                ProductId = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Level = p.Level,
                FacultyName = p.PrimaryFaculty?.DisplayName,
                ProductPrice = r.ProductPrice,
                SpecialPrice = r.SpecialPrice,
                EffectivePrice = r.EffectivePrice,
                IsSpecialPriceActive = r.IsSpecialPriceActive,
                ShareType = r.ShareType,
                ShareValue = r.ShareValue,
                CalculatedFranchiseAmount = r.CalculatedFranchiseAmount,
                ShareSource = r.Source,
                GstRate = r.GstRate,
                FranchiseeIsGstRegistered = r.FranchiseeIsGstRegistered,
                TaxableBase = r.TaxableBase,
                CommissionAmount = r.CommissionAmount,
                GstOnCommission = r.GstOnCommission
            });
        }

        return rows;
    }
}
