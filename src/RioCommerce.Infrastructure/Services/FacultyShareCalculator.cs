using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using FacultyEntity = RioCommerce.Core.Entities.Faculty;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Pure, date-driven implementation of <see cref="IFacultyShareCalculator"/>. See the interface for
/// the resolution-priority contract. Rounding is at the per-unit level so the product editor's
/// preview and the per-item earned ledger agree to the paisa.
/// </summary>
public sealed class FacultyShareCalculator : IFacultyShareCalculator
{
    private readonly RioCommerceDbContext _db;

    public FacultyShareCalculator(RioCommerceDbContext db) => _db = db;

    // ── Effective price ─────────────────────────────────────────────────────
    // Identical semantics to FranchiseShareCalculator: a product's special-price window is a
    // property of the product, not of who is earning from it, so both parties must read it the
    // same way or the same sale would have two different taxable bases.

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
    public ProductFacultyShareResult Calculate(
        Product product,
        IReadOnlyList<FacultySharingRule> rules,
        IReadOnlyDictionary<Guid, FacultyGstInfo> faculty,
        IReadOnlySet<Guid> attachedFacultyIds,
        FacultySettings settings,
        DateTime? asOfUtc = null)
    {
        var now = asOfUtc ?? DateTime.UtcNow;
        var specialActive = IsSpecialPriceActive(product, now);
        var effective = EffectivePrice(product, now);

        // The gross the share applies to: the special price when active AND settings permit it,
        // otherwise always the regular price. Display always reports the true effective price.
        var shareGross = settings.ApplyShareOnSpecialPrice ? effective : product.SellingPrice;
        var gstRate = product.GstRate;
        var taxableBase = FacultyShareMath.BaseFromGross(shareGross, gstRate);

        ProductFacultyShareResult Empty() => ProductFacultyShareResult.NoShare(
            product.Id, product.Title, product.Level, product.SellingPrice, product.SpecialPrice,
            effective, specialActive, gstRate, taxableBase, settings.MaxTotalSharePct);

        // ── Step A: resolve one (type, value, source) per faculty ────────────────────────────
        // Rules win over the product default, but only when overrides are allowed and the rule is
        // in force today. A rule dated into the future is configuration, not an entitlement — so
        // the product default legitimately applies in the meantime.
        var resolved = new List<(FacultyGstInfo Info, SharingType Type, decimal Value, FacultyShareSource Source, Guid? RuleId)>();
        var claimed = new HashSet<Guid>();

        if (settings.AllowFacultyRuleOverride)
        {
            foreach (var rule in rules)
            {
                if (!rule.AppliesAt(now)) continue;
                if (rule.ShareValue <= 0m) continue;
                // An in-force rule earns whether or not the faculty is still attached to the product —
                // the rule is the agreement, and removing someone from a course must not silently
                // cancel it. Attachment is only consulted for the DEFAULT, below.
                if (!faculty.TryGetValue(rule.FacultyId, out var info)) continue;
                if (!claimed.Add(rule.FacultyId)) continue;   // unique index guards this; belt and braces
                resolved.Add((info, rule.ShareType, rule.ShareValue, FacultyShareSource.FacultyRule, rule.Id));
            }
        }

        if (product.EnableDefaultFacultyShare && product.DefaultFacultyShareValue > 0m)
        {
            // The default is a per-faculty FALLBACK, so it applies to every attached faculty who
            // didn't resolve a rule above — not divided among them. See Product.EnableDefaultFacultyShare.
            //
            // Iterates attachedFacultyIds, NOT the whole faculty map: the map also contains
            // rule-holders who are no longer attached, and giving them the default would mean a faculty
            // removed from a course starts earning again the moment their own rule is disabled or its
            // window closes. Covered by
            // FacultyShareCalculatorTests.LapsedRule_OnDetachedFaculty_DoesNotInheritTheProductDefault.
            foreach (var facultyId in attachedFacultyIds)
            {
                if (claimed.Contains(facultyId)) continue;
                if (!faculty.TryGetValue(facultyId, out var info)) continue;
                resolved.Add((info, product.DefaultFacultyShareType, product.DefaultFacultyShareValue,
                    FacultyShareSource.ProductDefault, null));
            }
        }

        if (resolved.Count == 0) return Empty();

        // ── Step B: the specification's Steps 1–3, per faculty ──────────────────────────────
        var splits = new List<FacultyShareMath.ShareSplit>(resolved.Count);
        foreach (var r in resolved)
        {
            splits.Add(r.Type == SharingType.Percentage
                ? FacultyShareMath.FromPercent(shareGross, gstRate, r.Value, r.Info.IsGstRegistered)
                : FacultyShareMath.FromFixedPayout(r.Value, gstRate, r.Info.IsGstRegistered));
        }

        // ── Step C: the multi-faculty cap ───────────────────────────────────────────────────
        // Unconditional money guard — the combined BARE share cannot exceed the sale's taxable
        // value. Overshoot is always a misconfiguration, but it must degrade to an arithmetically
        // sound payout rather than a loss on every unit sold.
        var registrationFlags = resolved.Select(r => r.Info.IsGstRegistered).ToArray();
        var allocated = FacultyShareMath.AllocateWithinBase(splits, taxableBase, gstRate, registrationFlags);

        var lines = new List<FacultyShareLine>(resolved.Count);
        for (var i = 0; i < resolved.Count; i++)
        {
            var (split, wasCapped) = allocated[i];
            if (split.TotalPayout <= 0m) continue;
            var r = resolved[i];

            lines.Add(new FacultyShareLine
            {
                FacultyId = r.Info.FacultyId,
                FacultyName = r.Info.DisplayName,
                FacultyShortCode = r.Info.ShortCode,
                RuleId = r.RuleId,
                ShareType = r.Type,
                ShareValue = r.Value,
                FacultyIsGstRegistered = r.Info.IsGstRegistered,
                ShareAmount = split.Share,
                GstOnShare = split.GstOnShare,
                TotalPayout = split.TotalPayout,
                Source = r.Source,
                WasCapped = wasCapped,
                SharePctOfBase = FacultyShareMath.SharePctOfBase(split.Share, taxableBase)
            });
        }

        if (lines.Count == 0) return Empty();

        lines = lines.OrderByDescending(l => l.TotalPayout).ThenBy(l => l.FacultyName).ToList();

        var totalShare = lines.Sum(l => l.ShareAmount);
        var totalGst = lines.Sum(l => l.GstOnShare);
        var totalPct = FacultyShareMath.SharePctOfBase(totalShare, taxableBase);

        return new ProductFacultyShareResult
        {
            ProductId = product.Id,
            ProductTitle = product.Title,
            Level = product.Level,
            ProductPrice = product.SellingPrice,
            SpecialPrice = product.SpecialPrice,
            EffectivePrice = effective,
            IsSpecialPriceActive = specialActive,
            GstRate = gstRate,
            TaxableBase = taxableBase,
            Lines = lines,
            TotalShareAmount = totalShare,
            TotalGstOnShare = totalGst,
            TotalPayout = totalShare + totalGst,
            TotalSharePctOfBase = totalPct,
            MaxTotalSharePct = settings.MaxTotalSharePct,
            ExceedsConfiguredCap = totalPct > settings.MaxTotalSharePct,
            WasCappedToBase = lines.Any(l => l.WasCapped)
        };
    }

    // ── Loaders ─────────────────────────────────────────────────────────────
    public async Task<ProductFacultyShareResult?> GetForProductAsync(
        Guid productId, DateTime? asOfUtc = null, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return null;

        var settings = await GetSettingsAsync(ct);
        var rules = await _db.FacultySharingRules.AsNoTracking()
            .Where(r => r.ProductId == productId).ToListAsync(ct);
        var loaded = await LoadFacultyAsync(new[] { productId }, rules, ct);

        var forProduct = loaded.TryGetValue(productId, out var v) ? v : ProductFacultyLookup.Empty;
        return Calculate(product, rules, forProduct.Faculty, forProduct.AttachedIds, settings, asOfUtc);
    }

    public async Task<List<ProductFacultyShareSummary>> GetProductSharesAsync(
        bool onlyWithShare = false, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title)
            .ToListAsync(ct);
        if (products.Count == 0) return new();

        var productIds = products.Select(p => p.Id).ToList();
        var rulesByProduct = (await _db.FacultySharingRules.AsNoTracking()
                .Where(r => productIds.Contains(r.ProductId)).ToListAsync(ct))
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FacultySharingRule>)g.ToList());

        var allRules = rulesByProduct.Values.SelectMany(r => r).ToList();
        var facultyByProduct = await LoadFacultyAsync(productIds, allRules, ct);

        var now = DateTime.UtcNow;
        var rows = new List<ProductFacultyShareSummary>(products.Count);

        foreach (var p in products)
        {
            rulesByProduct.TryGetValue(p.Id, out var rules);
            var lookup = facultyByProduct.TryGetValue(p.Id, out var v) ? v : ProductFacultyLookup.Empty;
            var r = Calculate(p, rules ?? Array.Empty<FacultySharingRule>(),
                lookup.Faculty, lookup.AttachedIds, settings, now);

            if (onlyWithShare && !r.HasShare) continue;

            rows.Add(new ProductFacultyShareSummary
            {
                ProductId = p.Id,
                ProductTitle = p.Title,
                Sku = p.Sku,
                Level = p.Level,
                EffectivePrice = r.EffectivePrice,
                TaxableBase = r.TaxableBase,
                GstRate = r.GstRate,
                FacultyCount = r.Lines.Count,
                TotalShareAmount = r.TotalShareAmount,
                TotalGstOnShare = r.TotalGstOnShare,
                TotalPayout = r.TotalPayout,
                TotalSharePctOfBase = r.TotalSharePctOfBase,
                ExceedsConfiguredCap = r.ExceedsConfiguredCap,
                WasCappedToBase = r.WasCappedToBase,
                Lines = r.Lines
            });
        }

        return rows;
    }

    public async Task<FacultySettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await _db.FacultySettings.FirstOrDefaultAsync(ct);
        if (s == null)
        {
            s = new FacultySettings { Id = Guid.NewGuid() };
            _db.FacultySettings.Add(s);
            await _db.SaveChangesAsync(ct);
        }
        return s;
    }

    // ── Faculty resolution ──────────────────────────────────────────────────
    /// <summary>
    /// The two faculty sets one product's calculation needs, kept separate on purpose.
    /// <see cref="Faculty"/> is everyone relevant (attached OR holding a rule);
    /// <see cref="AttachedIds"/> is only those actually on the product, which is what the product
    /// default is allowed to reach.
    /// </summary>
    internal sealed record ProductFacultyLookup(
        Dictionary<Guid, FacultyGstInfo> Faculty, HashSet<Guid> AttachedIds)
    {
        public static ProductFacultyLookup Empty => new(new(), new());
    }

    /// <summary>
    /// Builds the per-product faculty lookup: everyone attached to the product via
    /// <c>ProductFaculty</c>, plus anyone a rule references but who is no longer attached — with the
    /// attached subset tracked separately.
    ///
    /// <para>Both groups matter, differently. Removing a faculty from a product's faculty list does not
    /// delete their sharing rule, and dropping them from the calculation entirely would silently cancel
    /// a live agreement. But they must not inherit the <b>product default</b> either, or removing
    /// someone from a course and then disabling their rule would start paying them again at the default
    /// rate. Hence two sets rather than one.</para>
    /// </summary>
    private async Task<Dictionary<Guid, ProductFacultyLookup>> LoadFacultyAsync(
        IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<FacultySharingRule> rules, CancellationToken ct)
    {
        var attachments = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId })
            .ToListAsync(ct);

        var facultyIds = attachments.Select(a => a.FacultyId)
            .Concat(rules.Select(r => r.FacultyId))
            .Distinct().ToList();
        if (facultyIds.Count == 0) return new();

        var faculty = await _db.Faculty.AsNoTracking()
            .Where(f => facultyIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => Info(f), ct);

        var result = new Dictionary<Guid, ProductFacultyLookup>();
        foreach (var pid in productIds) result[pid] = ProductFacultyLookup.Empty;

        foreach (var a in attachments)
            if (faculty.TryGetValue(a.FacultyId, out var info))
            {
                result[a.ProductId].Faculty[a.FacultyId] = info;
                result[a.ProductId].AttachedIds.Add(a.FacultyId);
            }

        foreach (var r in rules)
            if (result.TryGetValue(r.ProductId, out var lookup)
                && !lookup.Faculty.ContainsKey(r.FacultyId)
                && faculty.TryGetValue(r.FacultyId, out var info))
                lookup.Faculty[r.FacultyId] = info;   // deliberately NOT added to AttachedIds

        return result;
    }

    /// <summary>The one place a <see cref="FacultyEntity"/> becomes calculator input.</summary>
    internal static FacultyGstInfo Info(FacultyEntity f) =>
        new(f.Id, f.DisplayName, f.ShortCode, f.IsGstRegisteredForShare);
}
