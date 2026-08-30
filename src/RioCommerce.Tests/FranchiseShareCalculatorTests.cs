using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Services;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// Covers every rule in the Franchise Product Share specification: effective price, percentage
/// vs fixed share, special-price recalculation/expiry, override priority, and settings gating.
/// The Calculate(...) method is pure, so these tests need no database.
///
/// <para>Every case is now stated for BOTH franchisee types. The registered cases assert the exact
/// same totals the suite asserted before the commission-GST split was introduced — that is the
/// backward-compatibility proof, and it holds because <c>base × s × (1 + r) == gross × s</c>.</para>
/// </summary>
public class FranchiseShareCalculatorTests
{
    private static readonly FranchiseShareCalculator Calc = new(null!); // DB only used by GetProductSharesAsync

    // Named for readability at the call sites — the flag decides Scenario A vs Scenario B.
    private const bool Registered = true;
    private const bool Unregistered = false;

    private static FranchiseSettings DefaultSettings(bool allowOverride = true, bool applyOnSpecial = true) => new()
    {
        AllowFranchiseSpecificOverride = allowOverride,
        ApplyShareOnSpecialPrice = applyOnSpecial
    };

    private static Product MakeProduct(
        decimal regular,
        decimal? special = null,
        DateTime? specialStart = null,
        DateTime? specialEnd = null,
        bool enableDefaultShare = true,
        CommissionType defaultType = CommissionType.Percent,
        decimal defaultValue = 15m,
        decimal gstRate = 18m) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Course",
        SellingPrice = regular,
        SpecialPrice = special,
        SpecialPriceStartDateUtc = specialStart,
        SpecialPriceEndDateUtc = specialEnd,
        EnableDefaultFranchiseShare = enableDefaultShare,
        DefaultFranchiseShareType = defaultType,
        DefaultFranchiseShareValue = defaultValue,
        GstRate = gstRate
    };

    private static FranchiseCommission Override(CommissionType type, decimal value, bool active = true) => new()
    {
        FranchiseId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        Type = type,
        Value = value,
        IsActive = active
    };

    /// <summary>The split must always reconcile — a commission invoice that doesn't add up is unusable.</summary>
    private static void AssertSplitReconciles(FranchiseShareResult r) =>
        Assert.Equal(r.CalculatedFranchiseAmount, r.CommissionAmount + r.GstOnCommission);

    // ═══════════════════════════════════════════════════════════════════════════
    //  The specification's own worked example: ₹118 gross, 18% GST, 20% share.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpecExample_ScenarioA_RegisteredFranchisee()
    {
        var p = MakeProduct(118m, defaultValue: 20m, gstRate: 18m);
        var r = Calc.Calculate(p, null, DefaultSettings(), Registered);

        Assert.Equal(100m, r.TaxableBase);              // Step 1: 118 ÷ 1.18
        Assert.Equal(20m, r.CommissionAmount);          // Step 2: 100 × 20%
        Assert.Equal(3.60m, r.GstOnCommission);         // Step 3: 20 × 18%
        Assert.Equal(23.60m, r.CalculatedFranchiseAmount);
        Assert.True(r.FranchiseeIsGstRegistered);
        AssertSplitReconciles(r);
    }

    [Fact]
    public void SpecExample_ScenarioB_UnregisteredFranchisee()
    {
        var p = MakeProduct(118m, defaultValue: 20m, gstRate: 18m);
        var r = Calc.Calculate(p, null, DefaultSettings(), Unregistered);

        Assert.Equal(100m, r.TaxableBase);
        Assert.Equal(20m, r.CommissionAmount);          // same commission…
        Assert.Equal(0m, r.GstOnCommission);            // …but no tax on it
        Assert.Equal(20m, r.CalculatedFranchiseAmount);
        Assert.False(r.FranchiseeIsGstRegistered);
        AssertSplitReconciles(r);
    }

    [Fact]
    public void SpecExample_RegisteredPayout_ExceedsUnregistered_ByGstOnCommission()
    {
        // This IS the behaviour change: before the spec, both franchisees were paid ₹23.60.
        var p = MakeProduct(118m, defaultValue: 20m, gstRate: 18m);
        var a = Calc.Calculate(p, null, DefaultSettings(), Registered);
        var b = Calc.Calculate(p, null, DefaultSettings(), Unregistered);

        Assert.Equal(a.CommissionAmount, b.CommissionAmount);
        Assert.Equal(3.60m, a.CalculatedFranchiseAmount - b.CalculatedFranchiseAmount);
    }

    [Fact]
    public void RegisteredPayout_EqualsGrossTimesSharePct_ForAnyRate()
    {
        // base × s × (1 + r) == gross × s. This identity is why registered franchisees' payouts
        // did not move when the spec was adopted — guard it so a refactor can't silently break it.
        foreach (var (price, rate, pct) in new[]
                 {
                     (118m, 18m, 20m), (2500m, 18m, 15m), (1050m, 5m, 10m), (4720m, 18m, 12.5m)
                 })
        {
            var p = MakeProduct(price, defaultValue: pct, gstRate: rate);
            var r = Calc.Calculate(p, null, DefaultSettings(), Registered);
            Assert.Equal(Math.Round(price * pct / 100m, 2), r.CalculatedFranchiseAmount);
        }
    }

    [Fact]
    public void ZeroGstProduct_BothScenariosIdentical()
    {
        // No tax in the price means no base to reverse out and nothing to charge on the commission.
        var p = MakeProduct(1000m, defaultValue: 20m, gstRate: 0m);
        var a = Calc.Calculate(p, null, DefaultSettings(), Registered);
        var b = Calc.Calculate(p, null, DefaultSettings(), Unregistered);

        Assert.Equal(1000m, a.TaxableBase);
        Assert.Equal(200m, a.CalculatedFranchiseAmount);
        Assert.Equal(0m, a.GstOnCommission);
        Assert.Equal(a.CalculatedFranchiseAmount, b.CalculatedFranchiseAmount);
    }

    // ── Rule 1 & 2: effective price + default percentage share ───────────────
    [Fact]
    public void DefaultPercentShare_OnRegularPrice()
    {
        // Spec example: Regular 2500, Default 15% → 375
        var p = MakeProduct(2500m, defaultValue: 15m);
        var r = Calc.Calculate(p, null, DefaultSettings(), Registered);

        Assert.Equal(2500m, r.ProductPrice);
        Assert.Equal(2500m, r.EffectivePrice);
        Assert.False(r.IsSpecialPriceActive);
        Assert.Equal(CommissionType.Percent, r.ShareType);
        Assert.Equal(15m, r.ShareValue);
        Assert.Equal(375m, r.CalculatedFranchiseAmount);
        Assert.Equal(ShareSource.ProductDefault, r.Source);
        // 2118.64 base × 15% = 317.80 commission + 57.20 GST = 375.00
        Assert.Equal(317.80m, r.CommissionAmount);
        Assert.Equal(57.20m, r.GstOnCommission);
        AssertSplitReconciles(r);
    }

    [Fact]
    public void DefaultPercentShare_OnRegularPrice_Unregistered()
    {
        var p = MakeProduct(2500m, defaultValue: 15m);
        var r = Calc.Calculate(p, null, DefaultSettings(), Unregistered);

        Assert.Equal(317.80m, r.CalculatedFranchiseAmount);   // the bare commission only
        Assert.Equal(0m, r.GstOnCommission);
        AssertSplitReconciles(r);
    }

    // ── Rule 4 & example: special price active recalculates the share ────────
    [Fact]
    public void DefaultPercentShare_RecalculatesOnActiveSpecialPrice()
    {
        // Spec example: Special 2000, 15% → 300
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var p = MakeProduct(2500m, special: 2000m,
            specialStart: now.AddDays(-1), specialEnd: now.AddDays(1), defaultValue: 15m);

        var r = Calc.Calculate(p, null, DefaultSettings(), Registered, now);

        Assert.True(r.IsSpecialPriceActive);
        Assert.Equal(2000m, r.EffectivePrice);
        Assert.Equal(300m, r.CalculatedFranchiseAmount);
        Assert.Equal(ShareSource.ProductDefault, r.Source);
        AssertSplitReconciles(r);
    }

    [Fact]
    public void SpecialPrice_RevertsToRegular_WhenExpired()
    {
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        // Window already ended yesterday.
        var p = MakeProduct(2500m, special: 2000m,
            specialStart: now.AddDays(-5), specialEnd: now.AddDays(-1), defaultValue: 15m);

        var r = Calc.Calculate(p, null, DefaultSettings(), Registered, now);

        Assert.False(r.IsSpecialPriceActive);
        Assert.Equal(2500m, r.EffectivePrice);     // reverted to regular
        Assert.Equal(375m, r.CalculatedFranchiseAmount);
    }

    [Fact]
    public void SpecialPrice_NotYetStarted_UsesRegular()
    {
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var p = MakeProduct(2500m, special: 2000m,
            specialStart: now.AddDays(2), specialEnd: now.AddDays(5), defaultValue: 15m);

        var r = Calc.Calculate(p, null, DefaultSettings(), Registered, now);

        Assert.False(r.IsSpecialPriceActive);
        Assert.Equal(2500m, r.EffectivePrice);
    }

    [Fact]
    public void SpecialPrice_OpenEndedWindow_IsActive()
    {
        var now = DateTime.UtcNow;
        var p = MakeProduct(2500m, special: 2000m, specialStart: null, specialEnd: null, defaultValue: 15m);
        var r = Calc.Calculate(p, null, DefaultSettings(), Registered, now);
        Assert.True(r.IsSpecialPriceActive);
        Assert.Equal(2000m, r.EffectivePrice);
    }

    // ── Rule 2: fixed share ──────────────────────────────────────────────────
    [Fact]
    public void FixedShare_IsFlat_RegardlessOfPrice()
    {
        var p = MakeProduct(2500m, special: 2000m, specialStart: DateTime.UtcNow.AddDays(-1),
            specialEnd: DateTime.UtcNow.AddDays(1), defaultType: CommissionType.Fixed, defaultValue: 400m);

        var rRegular = Calc.Calculate(MakeProduct(2500m, defaultType: CommissionType.Fixed, defaultValue: 400m), null, DefaultSettings(), Registered);
        var rSpecial = Calc.Calculate(p, null, DefaultSettings(), Registered);

        Assert.Equal(400m, rRegular.CalculatedFranchiseAmount);
        Assert.Equal(400m, rSpecial.CalculatedFranchiseAmount);   // fixed doesn't change with price
    }

    [Fact]
    public void FixedShare_PayoutUnchangedByRegistration_ButDecomposed()
    {
        // Local policy (the spec is silent on fixed shares): the configured ₹ is what the
        // franchisee is paid either way, so no existing agreement changes value. A registered
        // franchisee's ₹400 is treated as commission-inclusive-of-tax and split back out.
        var p = MakeProduct(2500m, defaultType: CommissionType.Fixed, defaultValue: 400m);

        var a = Calc.Calculate(p, null, DefaultSettings(), Registered);
        var b = Calc.Calculate(p, null, DefaultSettings(), Unregistered);

        Assert.Equal(400m, a.CalculatedFranchiseAmount);
        Assert.Equal(400m, b.CalculatedFranchiseAmount);

        Assert.Equal(338.98m, a.CommissionAmount);    // 400 ÷ 1.18
        Assert.Equal(61.02m, a.GstOnCommission);
        AssertSplitReconciles(a);

        Assert.Equal(400m, b.CommissionAmount);
        Assert.Equal(0m, b.GstOnCommission);
        AssertSplitReconciles(b);
    }

    // ── Rule 5 & 6: override priority ────────────────────────────────────────
    [Fact]
    public void FranchiseOverride_TakesPriorityOverDefault_OnRegularPrice()
    {
        // Spec example: Default 15%, Override 20%, Regular 2500 → 500
        var p = MakeProduct(2500m, defaultValue: 15m);
        var ov = Override(CommissionType.Percent, 20m);

        var r = Calc.Calculate(p, ov, DefaultSettings(), Registered);

        Assert.Equal(ShareSource.FranchiseOverride, r.Source);
        Assert.Equal(20m, r.ShareValue);
        Assert.Equal(500m, r.CalculatedFranchiseAmount);
        AssertSplitReconciles(r);
    }

    [Fact]
    public void FranchiseOverride_AppliesToSpecialPrice()
    {
        // Spec example: Override 20%, Special 2000 → 400
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var p = MakeProduct(2500m, special: 2000m,
            specialStart: now.AddDays(-1), specialEnd: now.AddDays(1), defaultValue: 15m);
        var ov = Override(CommissionType.Percent, 20m);

        var r = Calc.Calculate(p, ov, DefaultSettings(), Registered, now);

        Assert.Equal(ShareSource.FranchiseOverride, r.Source);
        Assert.Equal(2000m, r.EffectivePrice);
        Assert.Equal(400m, r.CalculatedFranchiseAmount);
    }

    [Fact]
    public void InactiveOverride_FallsBackToProductDefault()
    {
        var p = MakeProduct(2500m, defaultValue: 15m);
        var ov = Override(CommissionType.Percent, 20m, active: false);

        var r = Calc.Calculate(p, ov, DefaultSettings(), Registered);

        Assert.Equal(ShareSource.ProductDefault, r.Source);
        Assert.Equal(375m, r.CalculatedFranchiseAmount);
    }

    // ── Rule 10: settings gating ─────────────────────────────────────────────
    [Fact]
    public void OverrideIgnored_WhenSettingsDisallowOverride()
    {
        var p = MakeProduct(2500m, defaultValue: 15m);
        var ov = Override(CommissionType.Percent, 20m);

        var r = Calc.Calculate(p, ov, DefaultSettings(allowOverride: false), Registered);

        Assert.Equal(ShareSource.ProductDefault, r.Source);   // override row ignored
        Assert.Equal(375m, r.CalculatedFranchiseAmount);
    }

    [Fact]
    public void ShareAppliesToRegular_WhenApplyOnSpecialDisabled()
    {
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var p = MakeProduct(2500m, special: 2000m,
            specialStart: now.AddDays(-1), specialEnd: now.AddDays(1), defaultValue: 15m);

        var r = Calc.Calculate(p, null, DefaultSettings(applyOnSpecial: false), Registered, now);

        // EffectivePrice still reports the true active price for DISPLAY...
        Assert.True(r.IsSpecialPriceActive);
        Assert.Equal(2000m, r.EffectivePrice);
        // ...but the share is computed on the regular price per settings: 2500 × 15% = 375.
        Assert.Equal(375m, r.CalculatedFranchiseAmount);
    }

    // ── No-share cases ───────────────────────────────────────────────────────
    [Fact]
    public void NoShare_WhenDefaultDisabledAndNoOverride()
    {
        var p = MakeProduct(2500m, enableDefaultShare: false);
        var r = Calc.Calculate(p, null, DefaultSettings(), Registered);

        Assert.False(r.HasShare);
        Assert.Equal(ShareSource.None, r.Source);
        Assert.Equal(0m, r.CalculatedFranchiseAmount);
        Assert.Equal(0m, r.CommissionAmount);
        Assert.Equal(0m, r.GstOnCommission);
        Assert.Equal(2500m, r.EffectivePrice);   // pricing still reported
    }

    [Fact]
    public void NoShare_WhenDefaultValueIsZero()
    {
        var p = MakeProduct(2500m, defaultValue: 0m);
        var r = Calc.Calculate(p, null, DefaultSettings(), Registered);
        Assert.False(r.HasShare);
    }

    // ── Rounding ─────────────────────────────────────────────────────────────
    [Fact]
    public void PercentShare_RoundsToTwoDecimals()
    {
        var p = MakeProduct(999m, defaultValue: 12.5m);   // 999 × 12.5% = 124.875 → 124.88
        var r = Calc.Calculate(p, null, DefaultSettings(), Registered);
        Assert.Equal(124.88m, r.CalculatedFranchiseAmount);
        // 846.61 base × 12.5% = 105.83 commission + 19.05 GST = 124.88
        Assert.Equal(105.83m, r.CommissionAmount);
        Assert.Equal(19.05m, r.GstOnCommission);
        AssertSplitReconciles(r);
    }
}
