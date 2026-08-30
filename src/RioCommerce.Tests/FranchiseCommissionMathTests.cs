using RioCommerce.Infrastructure.Services;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The commission arithmetic from "Franchisee Share Calculation Logic", exercised directly.
/// Covers the spec's worked example, the identity that kept registered payouts stable, the
/// zero-rate and cap edge cases, and the invariant every caller depends on: a split always adds up.
/// </summary>
public class FranchiseCommissionMathTests
{
    // ── Step 1: reverse-calculate the base ───────────────────────────────────
    [Theory]
    [InlineData(118, 18, 100)]        // the spec's example
    [InlineData(1180, 18, 1000)]
    [InlineData(1050, 5, 1000)]
    [InlineData(1000, 0, 1000)]       // untaxed product is its own base
    public void BaseFromGross_ReversesTheEmbeddedTax(decimal gross, decimal rate, decimal expected)
        => Assert.Equal(expected, FranchiseCommissionMath.BaseFromGross(gross, rate));

    [Fact]
    public void BaseFromGross_NegativeRate_TreatedAsUntaxed()
        => Assert.Equal(500m, FranchiseCommissionMath.BaseFromGross(500m, -5m));

    // ── Steps 2 & 3: the specification's worked example ──────────────────────
    [Fact]
    public void SpecExample_Registered_YieldsTwentyPlusThreeSixty()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, franchiseeIsGstRegistered: true);

        Assert.Equal(20m, s.Commission);
        Assert.Equal(3.60m, s.GstOnCommission);
        Assert.Equal(23.60m, s.TotalPayout);
    }

    [Fact]
    public void SpecExample_Unregistered_YieldsBareTwenty()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, franchiseeIsGstRegistered: false);

        Assert.Equal(20m, s.Commission);
        Assert.Equal(0m, s.GstOnCommission);
        Assert.Equal(20m, s.TotalPayout);
    }

    [Fact]
    public void TransferToInstitute_MatchesTheSpecSummaryTable()
    {
        const decimal gross = 118m;
        var a = FranchiseCommissionMath.FromPercent(gross, 18m, 20m, true);
        var b = FranchiseCommissionMath.FromPercent(gross, 18m, 20m, false);

        Assert.Equal(94.40m, gross - a.TotalPayout);   // Scenario A
        Assert.Equal(98.00m, gross - b.TotalPayout);   // Scenario B
    }

    [Fact]
    public void RegisteredPayout_EqualsGrossTimesShare()
    {
        // base × s × (1 + r) == gross × s — the reason registered franchisees' pay did not move.
        foreach (var (gross, rate, pct) in new[]
                 {
                     (118m, 18m, 20m), (2500m, 18m, 15m), (1050m, 5m, 10m), (7080m, 18m, 33m)
                 })
        {
            var s = FranchiseCommissionMath.FromPercent(gross, rate, pct, true);
            Assert.Equal(Math.Round(gross * pct / 100m, 2), s.TotalPayout);
        }
    }

    [Fact]
    public void ZeroGstRate_AddsNoTax_EvenWhenRegistered()
    {
        var s = FranchiseCommissionMath.FromPercent(1000m, 0m, 20m, true);

        Assert.Equal(200m, s.Commission);
        Assert.Equal(0m, s.GstOnCommission);
        Assert.Equal(200m, s.TotalPayout);
    }

    [Theory]
    [InlineData(0, 20)]     // nothing to take a share of
    [InlineData(118, 0)]    // no share configured
    public void NonPositiveInputs_YieldZeroSplit(decimal gross, decimal pct)
    {
        var s = FranchiseCommissionMath.FromPercent(gross, 18m, pct, true);
        Assert.Equal(0m, s.TotalPayout);
    }

    // ── Fixed ₹ shares — local policy, payout preserved ──────────────────────
    [Fact]
    public void FixedPayout_Registered_IsDecomposedNotInflated()
    {
        var s = FranchiseCommissionMath.FromFixedPayout(400m, 18m, true);

        Assert.Equal(400m, s.TotalPayout);        // the agreed cheque does not change size
        Assert.Equal(338.98m, s.Commission);      // 400 ÷ 1.18
        Assert.Equal(61.02m, s.GstOnCommission);
    }

    [Fact]
    public void FixedPayout_Unregistered_IsAllCommission()
    {
        var s = FranchiseCommissionMath.FromFixedPayout(400m, 18m, false);

        Assert.Equal(400m, s.TotalPayout);
        Assert.Equal(400m, s.Commission);
        Assert.Equal(0m, s.GstOnCommission);
    }

    [Fact]
    public void FixedPayout_NonPositive_YieldsZeroSplit()
        => Assert.Equal(0m, FranchiseCommissionMath.FromFixedPayout(0m, 18m, true).TotalPayout);

    // ── Quantity + aggregation ───────────────────────────────────────────────
    [Fact]
    public void Times_ScalesBothComponents_AndStillReconciles()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, true).Times(3);

        Assert.Equal(60m, s.Commission);
        Assert.Equal(10.80m, s.GstOnCommission);
        Assert.Equal(70.80m, s.TotalPayout);
    }

    [Fact]
    public void Add_FoldsAddOnsIntoTheLine()
    {
        var line = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, true);
        var addOn = FranchiseCommissionMath.FromPercent(59m, 18m, 20m, true);   // e.g. a lecture mode
        var total = line.Add(addOn);

        Assert.Equal(30m, total.Commission);
        Assert.Equal(5.40m, total.GstOnCommission);
        Assert.Equal(35.40m, total.TotalPayout);
    }

    // ── Capping when a discount drives the total below the earned share ──────
    [Fact]
    public void CapTo_AboveTheSplit_LeavesItUntouched()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, true);
        Assert.Equal(s, FranchiseCommissionMath.CapTo(s, 1000m));
    }

    [Fact]
    public void CapTo_ScalesComponentsProportionally_AndStillReconciles()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, true);   // 20 + 3.60 = 23.60
        var capped = FranchiseCommissionMath.CapTo(s, 11.80m);               // exactly half

        Assert.Equal(11.80m, capped.TotalPayout);
        Assert.Equal(10m, capped.Commission);
        Assert.Equal(1.80m, capped.GstOnCommission);
        Assert.Equal(capped.TotalPayout, capped.Commission + capped.GstOnCommission);
    }

    [Fact]
    public void CapTo_Unregistered_PutsTheWholeCapInCommission()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, false);   // 20, no GST
        var capped = FranchiseCommissionMath.CapTo(s, 12m);

        Assert.Equal(12m, capped.Commission);
        Assert.Equal(0m, capped.GstOnCommission);
        Assert.Equal(12m, capped.TotalPayout);
    }

    [Fact]
    public void CapTo_ZeroOrNegative_YieldsZeroSplit()
    {
        var s = FranchiseCommissionMath.FromPercent(118m, 18m, 20m, true);
        Assert.Equal(0m, FranchiseCommissionMath.CapTo(s, 0m).TotalPayout);
        Assert.Equal(0m, FranchiseCommissionMath.CapTo(s, -5m).TotalPayout);
    }

    // ── The invariant every consumer relies on ───────────────────────────────
    [Fact]
    public void SplitAlwaysReconciles_AcrossAWideInputSweep()
    {
        foreach (var gross in new[] { 1m, 99m, 118m, 999m, 2500m, 49999.99m })
            foreach (var rate in new[] { 0m, 5m, 12m, 18m, 28m })
                foreach (var pct in new[] { 1m, 12.5m, 20m, 33.33m })
                    foreach (var registered in new[] { true, false })
                    {
                        var s = FranchiseCommissionMath.FromPercent(gross, rate, pct, registered);
                        Assert.Equal(s.TotalPayout, s.Commission + s.GstOnCommission);
                        Assert.True(s.TotalPayout <= gross, $"payout {s.TotalPayout} exceeded gross {gross}");
                        if (!registered) Assert.Equal(0m, s.GstOnCommission);
                    }
    }
}
