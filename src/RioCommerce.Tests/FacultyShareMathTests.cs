using RioCommerce.Infrastructure.Services;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The faculty share arithmetic. Steps 1–3 are the same specification the franchise side implements,
/// so the first block here PINS that equivalence rather than re-deriving it — if the two ever
/// diverge, a test fails instead of a payout quietly changing.
///
/// <para>The substantial new coverage is <see cref="FacultyShareMath.AllocateWithinBase"/>: the
/// multi-faculty ceiling, which has no franchise counterpart because a product has one franchisee but
/// can carry any number of faculty on independently-configured rates.</para>
/// </summary>
public class FacultyShareMathTests
{
    private const bool Registered = true;
    private const bool Unregistered = false;

    // ── Equivalence with the franchise implementation ───────────────────────
    [Theory]
    [InlineData(118, 18, 20)]
    [InlineData(1180, 18, 15)]
    [InlineData(1050, 5, 33.33)]
    [InlineData(1000, 0, 40)]
    [InlineData(999.99, 12, 7.5)]
    public void FromPercent_MatchesFranchiseMath_ForBothRegistrationStates(
        decimal gross, decimal rate, decimal pct)
    {
        foreach (var registered in new[] { Registered, Unregistered })
        {
            var faculty = FacultyShareMath.FromPercent(gross, rate, pct, registered);
            var franchise = FranchiseCommissionMath.FromPercent(gross, rate, pct, registered);

            Assert.Equal(franchise.Commission, faculty.Share);
            Assert.Equal(franchise.GstOnCommission, faculty.GstOnShare);
            Assert.Equal(franchise.TotalPayout, faculty.TotalPayout);
        }
    }

    [Theory]
    [InlineData(400, 18)]
    [InlineData(250.55, 5)]
    [InlineData(100, 0)]
    public void FromFixedPayout_MatchesFranchiseMath(decimal payout, decimal rate)
    {
        foreach (var registered in new[] { Registered, Unregistered })
        {
            var faculty = FacultyShareMath.FromFixedPayout(payout, rate, registered);
            var franchise = FranchiseCommissionMath.FromFixedPayout(payout, rate, registered);

            Assert.Equal(franchise.Commission, faculty.Share);
            Assert.Equal(franchise.GstOnCommission, faculty.GstOnShare);
            Assert.Equal(franchise.TotalPayout, faculty.TotalPayout);
        }
    }

    // ── The specification's worked example, restated for faculty ────────────
    [Fact]
    public void SpecExample_RegisteredFaculty_GetsTwentyPlusThreeSixty()
    {
        var s = FacultyShareMath.FromPercent(118m, 18m, 20m, Registered);

        Assert.Equal(20m, s.Share);
        Assert.Equal(3.60m, s.GstOnShare);
        Assert.Equal(23.60m, s.TotalPayout);
    }

    [Fact]
    public void SpecExample_UnregisteredFaculty_GetsBareTwenty()
    {
        var s = FacultyShareMath.FromPercent(118m, 18m, 20m, Unregistered);

        Assert.Equal(20m, s.Share);
        Assert.Equal(0m, s.GstOnShare);
        Assert.Equal(20m, s.TotalPayout);
    }

    [Fact]
    public void ShareOnGrossWouldOverpay_TheWholePointOfTheChange()
    {
        // The behaviour this implementation replaces: share applied to the GST-INCLUSIVE price with no
        // tax handling at all. For an UNREGISTERED faculty that overpays by exactly share × rate,
        // which is the money the specification moves.
        const decimal gross = 118m, rate = 18m, pct = 20m;
        var oldWay = Math.Round(gross * pct / 100m, 2);              // ₹23.60
        var correct = FacultyShareMath.FromPercent(gross, rate, pct, Unregistered);

        Assert.Equal(23.60m, oldWay);
        Assert.Equal(20m, correct.TotalPayout);
        Assert.Equal(Math.Round(correct.Share * rate / 100m, 2), oldWay - correct.TotalPayout);
    }

    // ── Invariants every caller depends on ──────────────────────────────────
    [Theory]
    [InlineData(1180, 18, 12.5)]
    [InlineData(777, 12, 8.33)]
    [InlineData(59, 5, 3.3)]
    public void Split_AlwaysAddsUp(decimal gross, decimal rate, decimal pct)
    {
        foreach (var registered in new[] { Registered, Unregistered })
        {
            var s = FacultyShareMath.FromPercent(gross, rate, pct, registered);
            Assert.Equal(s.TotalPayout, s.Share + s.GstOnShare);
        }
    }

    [Fact]
    public void Times_ScalesEachComponentAndStillAddsUp()
    {
        var s = FacultyShareMath.FromPercent(118m, 18m, 20m, Registered).Times(3);

        Assert.Equal(60m, s.Share);
        Assert.Equal(10.80m, s.GstOnShare);
        Assert.Equal(70.80m, s.TotalPayout);
        Assert.Equal(s.TotalPayout, s.Share + s.GstOnShare);
    }

    [Fact]
    public void ZeroRatedProduct_CollapsesBothScenarios()
    {
        var reg = FacultyShareMath.FromPercent(1000m, 0m, 25m, Registered);
        var unreg = FacultyShareMath.FromPercent(1000m, 0m, 25m, Unregistered);

        Assert.Equal(250m, reg.TotalPayout);
        Assert.Equal(reg.TotalPayout, unreg.TotalPayout);
        Assert.Equal(0m, reg.GstOnShare);
    }

    // ── SharePctOfBase: the comparable figure across mixed rule types ───────
    [Fact]
    public void SharePctOfBase_ConvertsAFixedAmountToItsPercentageEquivalent()
        => Assert.Equal(20m, FacultyShareMath.SharePctOfBase(200m, 1000m));

    [Fact]
    public void SharePctOfBase_NonPositiveBase_IsZeroRatherThanDivideByZero()
    {
        Assert.Equal(0m, FacultyShareMath.SharePctOfBase(200m, 0m));
        Assert.Equal(0m, FacultyShareMath.SharePctOfBase(200m, -50m));
    }

    // ── AllocateWithinBase: the multi-faculty ceiling ───────────────────────
    [Fact]
    public void Allocate_WithinBase_LeavesEveryShareUntouched()
    {
        // Three faculty at 20% / 20% / 10% of a ₹1000 base = 50%. Fits, so nothing moves.
        var splits = new[]
        {
            FacultyShareMath.FromPercent(1180m, 18m, 20m, Registered),
            FacultyShareMath.FromPercent(1180m, 18m, 20m, Unregistered),
            FacultyShareMath.FromPercent(1180m, 18m, 10m, Registered)
        };

        var result = FacultyShareMath.AllocateWithinBase(
            splits, 1000m, 18m, new[] { true, false, true });

        Assert.All(result, r => Assert.False(r.WasCapped));
        Assert.Equal(200m, result[0].Split.Share);
        Assert.Equal(200m, result[1].Split.Share);
        Assert.Equal(100m, result[2].Split.Share);
        // Unregistered faculty still gets no GST even when nothing is capped.
        Assert.Equal(0m, result[1].Split.GstOnShare);
    }

    [Fact]
    public void Allocate_OverBase_ScalesProportionallyAndSumsToExactlyTheBase()
    {
        // Three faculty at 40% each = 120% of the base — individually plausible, collectively a loss.
        var splits = new[]
        {
            FacultyShareMath.FromPercent(1180m, 18m, 40m, Registered),
            FacultyShareMath.FromPercent(1180m, 18m, 40m, Registered),
            FacultyShareMath.FromPercent(1180m, 18m, 40m, Registered)
        };

        var result = FacultyShareMath.AllocateWithinBase(
            splits, 1000m, 18m, new[] { true, true, true });

        Assert.All(result, r => Assert.True(r.WasCapped));
        // Sums to the base EXACTLY — not ±0.01, which is why the largest-remainder pass exists.
        Assert.Equal(1000m, result.Sum(r => r.Split.Share));

        // Equal inputs come out equal to within one paisa, and no closer: ₹1000 does not divide into
        // three equal paise amounts (₹333.33 × 3 = ₹999.99), so exactly one faculty must absorb the
        // spare paisa. The largest-remainder pass breaks the three-way tie by index, so it is always
        // the first — deterministic rather than arbitrary, which matters for reproducible payouts.
        var shares = result.Select(r => r.Split.Share).ToList();
        Assert.Equal(333.34m, shares[0]);
        Assert.Equal(333.33m, shares[1]);
        Assert.Equal(333.33m, shares[2]);
        Assert.True(shares.Max() - shares.Min() <= 0.01m);
    }

    [Fact]
    public void Allocate_OverBase_PreservesTheRelativeAgreements()
    {
        // 60/30/30 → 120% of a ₹1000 base. After scaling the first must still be twice the others.
        var splits = new[]
        {
            FacultyShareMath.FromPercent(1180m, 18m, 60m, Unregistered),
            FacultyShareMath.FromPercent(1180m, 18m, 30m, Unregistered),
            FacultyShareMath.FromPercent(1180m, 18m, 30m, Unregistered)
        };

        var result = FacultyShareMath.AllocateWithinBase(
            splits, 1000m, 18m, new[] { false, false, false });

        Assert.Equal(1000m, result.Sum(r => r.Split.Share));
        Assert.Equal(500m, result[0].Split.Share);
        Assert.Equal(250m, result[1].Split.Share);
        Assert.Equal(250m, result[2].Split.Share);
    }

    [Fact]
    public void Allocate_OverBase_RecomputesGstPerFacultyRegistration()
    {
        // A capped REGISTERED faculty still charges GST on their reduced fee; a capped UNREGISTERED
        // one still charges none. Rebuilding GST from the scaled share is what makes this hold —
        // scaling the original GST figure would have been wrong for neither-here-nor-there reasons.
        var splits = new[]
        {
            FacultyShareMath.FromPercent(1180m, 18m, 80m, Registered),
            FacultyShareMath.FromPercent(1180m, 18m, 80m, Unregistered)
        };

        var result = FacultyShareMath.AllocateWithinBase(
            splits, 1000m, 18m, new[] { true, false });

        Assert.Equal(1000m, result.Sum(r => r.Split.Share));
        Assert.Equal(500m, result[0].Split.Share);
        Assert.Equal(90m, result[0].Split.GstOnShare);        // 500 × 18%
        Assert.Equal(590m, result[0].Split.TotalPayout);
        Assert.Equal(500m, result[1].Split.Share);
        Assert.Equal(0m, result[1].Split.GstOnShare);
        Assert.Equal(500m, result[1].Split.TotalPayout);
    }

    [Fact]
    public void Allocate_CapIsOnTheBareShare_NotThePayout()
    {
        // Two registered faculty at 50% each exactly fill a ₹1000 base. Their GST (₹180 combined)
        // rides ON TOP: it is a pass-through recoverable as input tax credit, not a slice of the sale,
        // so it must not consume cap headroom and must not trigger scaling.
        var splits = new[]
        {
            FacultyShareMath.FromPercent(1180m, 18m, 50m, Registered),
            FacultyShareMath.FromPercent(1180m, 18m, 50m, Registered)
        };

        var result = FacultyShareMath.AllocateWithinBase(
            splits, 1000m, 18m, new[] { true, true });

        Assert.All(result, r => Assert.False(r.WasCapped));
        Assert.Equal(1000m, result.Sum(r => r.Split.Share));
        Assert.Equal(1180m, result.Sum(r => r.Split.TotalPayout));   // exceeds the base, by design
    }

    [Fact]
    public void Allocate_ZeroBase_PaysNothingAndFlagsIt()
    {
        // A free or fully-discounted line can support no share at all. Flagged as capped so the reason
        // for a ₹0 payout is recorded rather than looking like a missing rule.
        var splits = new[] { FacultyShareMath.FromPercent(1180m, 18m, 20m, Registered) };

        var result = FacultyShareMath.AllocateWithinBase(splits, 0m, 18m, new[] { true });

        Assert.Single(result);
        Assert.True(result[0].WasCapped);
        Assert.Equal(0m, result[0].Split.TotalPayout);
    }

    [Fact]
    public void Allocate_EmptyInput_ReturnsEmpty()
        => Assert.Empty(FacultyShareMath.AllocateWithinBase(
            Array.Empty<FacultyShareMath.ShareSplit>(), 1000m, 18m, Array.Empty<bool>()));

    [Fact]
    public void Allocate_AwkwardRatios_StillSumToTheBaseToThePaisa()
    {
        // Three-way split of a base that does not divide evenly — the case the largest-remainder pass
        // exists for. 1/3 of ₹1000 is ₹333.333…, so naive rounding would land on ₹999.99 or ₹1000.01.
        var splits = new[]
        {
            FacultyShareMath.FromPercent(1180m, 18m, 50m, Unregistered),
            FacultyShareMath.FromPercent(1180m, 18m, 50m, Unregistered),
            FacultyShareMath.FromPercent(1180m, 18m, 50m, Unregistered)
        };

        var result = FacultyShareMath.AllocateWithinBase(
            splits, 1000m, 18m, new[] { false, false, false });

        Assert.Equal(1000m, result.Sum(r => r.Split.Share));
        Assert.All(result, r => Assert.Equal(r.Split.TotalPayout, r.Split.Share + r.Split.GstOnShare));
    }

    // ── CapTo ───────────────────────────────────────────────────────────────
    [Fact]
    public void CapTo_ScalesCommissionAndGstTogetherSoTheySum()
    {
        var s = FacultyShareMath.FromPercent(1180m, 18m, 50m, Registered);   // 500 + 90 = 590
        var capped = FacultyShareMath.CapTo(s, 295m);

        Assert.Equal(295m, capped.TotalPayout);
        Assert.Equal(capped.TotalPayout, capped.Share + capped.GstOnShare);
    }

    [Fact]
    public void CapTo_AboveTheSplit_IsANoOp()
    {
        var s = FacultyShareMath.FromPercent(1180m, 18m, 20m, Registered);
        Assert.Equal(s, FacultyShareMath.CapTo(s, 9999m));
    }
}
