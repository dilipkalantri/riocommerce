using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Services;
using Xunit;
using FacultyEntity = RioCommerce.Core.Entities.Faculty;

namespace RioCommerce.Tests;

/// <summary>
/// Product faculty share resolution: per-faculty rule vs product default, effective windows, special
/// pricing, settings gating, and the multi-faculty cap. <c>Calculate(...)</c> is pure, so none of this
/// needs a database.
/// </summary>
public class FacultyShareCalculatorTests
{
    private static readonly FacultyShareCalculator Calc = new(null!); // DB only used by the loaders

    private static readonly DateTime Now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    private static FacultySettings Settings(
        bool allowOverride = true, bool applyOnSpecial = true,
        decimal maxTotal = 100m, bool enforce = true) => new()
    {
        AllowFacultyRuleOverride = allowOverride,
        ApplyShareOnSpecialPrice = applyOnSpecial,
        MaxTotalSharePct = maxTotal,
        EnforceMaxTotalShare = enforce
    };

    private static Product MakeProduct(
        decimal regular = 1180m,
        decimal? special = null,
        DateTime? specialStart = null,
        DateTime? specialEnd = null,
        bool enableDefault = false,
        SharingType defaultType = SharingType.Percentage,
        decimal defaultValue = 10m,
        decimal gstRate = 18m) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Course",
        Level = CourseLevel.Beginner,
        SellingPrice = regular,
        SpecialPrice = special,
        SpecialPriceStartDateUtc = specialStart,
        SpecialPriceEndDateUtc = specialEnd,
        EnableDefaultFacultyShare = enableDefault,
        DefaultFacultyShareType = defaultType,
        DefaultFacultyShareValue = defaultValue,
        GstRate = gstRate
    };

    private static FacultyGstInfo Info(Guid id, string name, bool registered) =>
        new(id, name, name.Length >= 2 ? name[..2].ToUpperInvariant() : "XX", registered);

    /// <summary>
    /// Calls the calculator at <see cref="Now"/>. <paramref name="attached"/> defaults to "everyone in
    /// the faculty map is attached to the product", which is the ordinary case. The tests that care
    /// about a DETACHED rule-holder pass it explicitly — that distinction decides whether someone can
    /// inherit the product default, so it must be stated rather than inferred.
    /// </summary>
    private static ProductFacultyShareResult Run(
        Product product,
        IReadOnlyList<FacultySharingRule> rules,
        Dictionary<Guid, FacultyGstInfo> faculty,
        FacultySettings settings,
        IReadOnlySet<Guid>? attached = null)
        => Calc.Calculate(product, rules, faculty, attached ?? faculty.Keys.ToHashSet(), settings, Now);

    private static FacultySharingRule Rule(
        Guid productId, Guid facultyId, decimal value,
        SharingType type = SharingType.Percentage, bool active = true,
        DateTime? from = null, DateTime? to = null) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        FacultyId = facultyId,
        ShareType = type,
        ShareValue = value,
        IsActive = active,
        EffectiveFrom = from,
        EffectiveTo = to
    };

    // ── Effective price ─────────────────────────────────────────────────────
    [Fact]
    public void EffectivePrice_UsesSpecialOnlyInsideItsWindow()
    {
        var p = MakeProduct(1180m, special: 944m,
            specialStart: Now.AddDays(-1), specialEnd: Now.AddDays(1));
        Assert.Equal(944m, Calc.EffectivePrice(p, Now));
        Assert.Equal(1180m, Calc.EffectivePrice(p, Now.AddDays(5)));
        Assert.Equal(1180m, Calc.EffectivePrice(p, Now.AddDays(-5)));
    }

    // ── Resolution priority ─────────────────────────────────────────────────
    [Fact]
    public void ExplicitRule_BeatsTheProductDefault()
    {
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m) }, faculty, Settings());

        var line = Assert.Single(r.Lines);
        Assert.Equal(FacultyShareSource.FacultyRule, line.Source);
        Assert.Equal(25m, line.ShareValue);
        Assert.Equal(250m, line.ShareAmount);       // 25% of the ₹1000 pre-GST base
    }

    [Fact]
    public void ProductDefault_AppliesToEveryAttachedFacultyWithoutARule()
    {
        // The design decision this pins: the default is a per-faculty FALLBACK, not a pool that gets
        // divided. Two faculty on a 10% default cost 20% of the base in total.
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [a] = Info(a, "Anita", false),
            [b] = Info(b, "Bhavesh", false)
        };

        var r = Run(p, Array.Empty<FacultySharingRule>(), faculty, Settings());

        Assert.Equal(2, r.Lines.Count);
        Assert.All(r.Lines, l => Assert.Equal(FacultyShareSource.ProductDefault, l.Source));
        Assert.All(r.Lines, l => Assert.Equal(100m, l.ShareAmount));
        Assert.Equal(200m, r.TotalShareAmount);
        Assert.Equal(20m, r.TotalSharePctOfBase);
    }

    [Fact]
    public void MixedSources_RuledFacultyKeepsTheirRate_OthersInheritTheDefault()
    {
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [a] = Info(a, "Anita", false),
            [b] = Info(b, "Bhavesh", false)
        };

        var r = Run(p, new[] { Rule(p.Id, a, 30m) }, faculty, Settings());

        Assert.Equal(2, r.Lines.Count);
        Assert.Equal(300m, r.Lines.Single(l => l.FacultyId == a).ShareAmount);
        Assert.Equal(100m, r.Lines.Single(l => l.FacultyId == b).ShareAmount);
        Assert.Equal(40m, r.TotalSharePctOfBase);
    }

    [Fact]
    public void NoRuleAndNoDefault_YieldsNoShare()
    {
        var p = MakeProduct(enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, Array.Empty<FacultySharingRule>(), faculty, Settings());

        Assert.False(r.HasShare);
        Assert.Empty(r.Lines);
        Assert.Equal(0m, r.TotalPayout);
    }

    [Fact]
    public void OverrideDisabled_FallsBackToTheProductDefaultEvenWhenARuleExists()
    {
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m) }, faculty,
            Settings(allowOverride: false));

        var line = Assert.Single(r.Lines);
        Assert.Equal(FacultyShareSource.ProductDefault, line.Source);
        Assert.Equal(100m, line.ShareAmount);
    }

    [Fact]
    public void InactiveRule_FallsBackToTheDefault()
    {
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, active: false) }, faculty, Settings());

        Assert.Equal(FacultyShareSource.ProductDefault, Assert.Single(r.Lines).Source);
    }

    [Fact]
    public void FacultyReferencedByARuleButAbsentFromTheLookup_IsSkippedNotGuessed()
    {
        // A rule whose faculty could not be loaded must not produce a payout to an unknown party.
        var p = MakeProduct(enableDefault: false);
        var known = Guid.NewGuid();
        var ghost = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [known] = Info(known, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, known, 10m), Rule(p.Id, ghost, 50m) },
            faculty, Settings());

        Assert.Equal(known, Assert.Single(r.Lines).FacultyId);
    }

    // ── Attachment vs rule: who the product default is allowed to reach ─────
    [Fact]
    public void DetachedFaculty_WithAnInForceRule_StillEarns()
    {
        // Removing a faculty from a product's faculty list does not delete their sharing rule. The rule
        // is the agreement, so it keeps paying — otherwise an edit to a display list would silently
        // cancel a commitment.
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m) }, faculty, Settings(),
            attached: new HashSet<Guid>());   // in the map, but NOT attached

        var line = Assert.Single(r.Lines);
        Assert.Equal(FacultyShareSource.FacultyRule, line.Source);
        Assert.Equal(250m, line.ShareAmount);
    }

    [Fact]
    public void LapsedRule_OnDetachedFaculty_DoesNotInheritTheProductDefault()
    {
        // The regression this exists for: `faculty` contains rule-holders as well as attached faculty,
        // so iterating it for the DEFAULT would pay a removed faculty the default rate the moment their
        // own rule was disabled. Removed + disabled must mean zero.
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, active: false) }, faculty, Settings(),
            attached: new HashSet<Guid>());

        Assert.False(r.HasShare);
        Assert.Empty(r.Lines);
    }

    [Fact]
    public void OutOfWindowRule_OnDetachedFaculty_DoesNotInheritTheProductDefault()
    {
        // Same defect via the effective window rather than the active flag.
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, to: Now.AddDays(-1)) }, faculty, Settings(),
            attached: new HashSet<Guid>());

        Assert.False(r.HasShare);
    }

    [Fact]
    public void LapsedRule_OnStillAttachedFaculty_DoesInheritTheProductDefault()
    {
        // The other side of the same rule: still teaching the course, so the default legitimately
        // applies once their own rate stops.
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, active: false) }, faculty, Settings());

        var line = Assert.Single(r.Lines);
        Assert.Equal(FacultyShareSource.ProductDefault, line.Source);
        Assert.Equal(100m, line.ShareAmount);
    }

    [Fact]
    public void DetachedRuleHolder_DoesNotInflateAnotherFacultysDefault()
    {
        // A detached rule-holder earning their own 25% plus an attached colleague on the 10% default:
        // 35% total, not 45%. The detached one must be counted once, via their rule only.
        var p = MakeProduct(enableDefault: true, defaultValue: 10m);
        var detached = Guid.NewGuid();
        var onCourse = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [detached] = Info(detached, "Detached", false),
            [onCourse] = Info(onCourse, "OnCourse", false)
        };

        var r = Run(p, new[] { Rule(p.Id, detached, 25m) }, faculty, Settings(),
            attached: new HashSet<Guid> { onCourse });

        Assert.Equal(2, r.Lines.Count);
        Assert.Equal(250m, r.Lines.Single(l => l.FacultyId == detached).ShareAmount);
        Assert.Equal(100m, r.Lines.Single(l => l.FacultyId == onCourse).ShareAmount);
        Assert.Equal(35m, r.TotalSharePctOfBase);
    }

    // ── Effective windows ───────────────────────────────────────────────────
    [Fact]
    public void RuleDatedIntoTheFuture_DoesNotEarnYet()
    {
        var p = MakeProduct(enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, from: Now.AddDays(7)) },
            faculty, Settings());

        Assert.False(r.HasShare);
    }

    [Fact]
    public void RuleWhoseWindowHasClosed_StopsEarning()
    {
        var p = MakeProduct(enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, to: Now.AddDays(-1)) },
            faculty, Settings());

        Assert.False(r.HasShare);
    }

    [Fact]
    public void RuleInsideItsWindow_Earns()
    {
        var p = MakeProduct(enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m, from: Now.AddDays(-1), to: Now.AddDays(1)) },
            faculty, Settings());

        Assert.Equal(250m, Assert.Single(r.Lines).ShareAmount);
    }

    // ── GST registration ────────────────────────────────────────────────────
    [Fact]
    public void RegisteredFaculty_GetsGstOnTop_UnregisteredDoesNot()
    {
        var p = MakeProduct(enableDefault: false);
        var reg = Guid.NewGuid();
        var unreg = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [reg] = Info(reg, "Registered", true),
            [unreg] = Info(unreg, "Unregistered", false)
        };

        var r = Run(p, new[] { Rule(p.Id, reg, 20m), Rule(p.Id, unreg, 20m) },
            faculty, Settings());

        var regLine = r.Lines.Single(l => l.FacultyId == reg);
        var unregLine = r.Lines.Single(l => l.FacultyId == unreg);

        Assert.Equal(200m, regLine.ShareAmount);
        Assert.Equal(36m, regLine.GstOnShare);
        Assert.Equal(236m, regLine.TotalPayout);

        Assert.Equal(200m, unregLine.ShareAmount);
        Assert.Equal(0m, unregLine.GstOnShare);
        Assert.Equal(200m, unregLine.TotalPayout);

        // Same agreed rate → same FEE. Only the tax differs, which is the specification's point.
        Assert.Equal(regLine.ShareAmount, unregLine.ShareAmount);
    }

    [Fact]
    public void RegistrationTest_HonoursEitherTheGstinOrTheFlag()
    {
        // Faculty carries both a GSTIN and an explicit flag; either one means registered. An admin who
        // ticks the flag before the number arrives must not have the faculty underpaid.
        Assert.True(new FacultyEntity { Gstin = "27ABCDE1234F1Z5" }.IsGstRegisteredForShare);
        Assert.True(new FacultyEntity { GstRegistered = true }.IsGstRegisteredForShare);
        Assert.False(new FacultyEntity().IsGstRegisteredForShare);
        Assert.False(new FacultyEntity { Gstin = "   " }.IsGstRegisteredForShare);
    }

    // ── Special price ───────────────────────────────────────────────────────
    [Fact]
    public void ActiveSpecialPrice_ReducesTheShareWhenSettingsAllow()
    {
        var p = MakeProduct(1180m, special: 590m,
            specialStart: Now.AddDays(-1), specialEnd: Now.AddDays(1), enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 20m) }, faculty, Settings());

        Assert.True(r.IsSpecialPriceActive);
        Assert.Equal(590m, r.EffectivePrice);
        Assert.Equal(500m, r.TaxableBase);
        Assert.Equal(100m, Assert.Single(r.Lines).ShareAmount);
    }

    [Fact]
    public void SpecialPriceIgnoredForShare_WhenSettingsSayRegularOnly()
    {
        var p = MakeProduct(1180m, special: 590m,
            specialStart: Now.AddDays(-1), specialEnd: Now.AddDays(1), enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 20m) }, faculty,
            Settings(applyOnSpecial: false));

        // Display still reports the true effective price; the share is computed on the regular one.
        Assert.Equal(590m, r.EffectivePrice);
        Assert.Equal(1000m, r.TaxableBase);
        Assert.Equal(200m, Assert.Single(r.Lines).ShareAmount);
    }

    // ── Fixed ₹ shares ──────────────────────────────────────────────────────
    [Fact]
    public void FixedShare_IsTheTotalPayout_DecomposedForRegisteredFaculty()
    {
        var p = MakeProduct(enableDefault: false);
        var reg = Guid.NewGuid();
        var unreg = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [reg] = Info(reg, "Registered", true),
            [unreg] = Info(unreg, "Unregistered", false)
        };

        var r = Run(p, new[]
        {
            Rule(p.Id, reg, 400m, SharingType.FixedAmount),
            Rule(p.Id, unreg, 400m, SharingType.FixedAmount)
        }, faculty, Settings());

        // Both are paid exactly the agreed ₹400 — the registered one's is decomposed into fee + tax.
        Assert.All(r.Lines, l => Assert.Equal(400m, l.TotalPayout));
        var regLine = r.Lines.Single(l => l.FacultyId == reg);
        Assert.Equal(338.98m, regLine.ShareAmount);
        Assert.Equal(61.02m, regLine.GstOnShare);
    }

    [Fact]
    public void FixedAndPercentageMix_ComparedOnTheSameBaseForTheTotal()
    {
        // ₹200 fixed on a ₹1000 base counts as 20% for cap purposes, alongside a real 20%.
        var p = MakeProduct(enableDefault: false);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [a] = Info(a, "Anita", false),
            [b] = Info(b, "Bhavesh", false)
        };

        var r = Run(p, new[]
        {
            Rule(p.Id, a, 20m),
            Rule(p.Id, b, 200m, SharingType.FixedAmount)
        }, faculty, Settings());

        Assert.Equal(40m, r.TotalSharePctOfBase);
        Assert.Equal(400m, r.TotalShareAmount);
    }

    // ── The multi-faculty cap ───────────────────────────────────────────────
    [Fact]
    public void OverConfiguredCap_IsFlaggedButNotSilentlyAltered()
    {
        // 60 + 30 = 90% is under the taxable base, so nothing is scaled; it only breaches an 80%
        // business ceiling. Reported, not corrected — that call belongs to the caller.
        var p = MakeProduct(enableDefault: false);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [a] = Info(a, "Anita", false),
            [b] = Info(b, "Bhavesh", false)
        };

        var r = Run(p, new[] { Rule(p.Id, a, 60m), Rule(p.Id, b, 30m) },
            faculty, Settings(maxTotal: 80m));

        Assert.True(r.ExceedsConfiguredCap);
        Assert.False(r.WasCappedToBase);
        Assert.Equal(90m, r.TotalSharePctOfBase);
        Assert.Equal(900m, r.TotalShareAmount);
        Assert.Equal(0m, r.RemainingSharePct);
    }

    [Fact]
    public void OverTheTaxableBase_IsCappedUnconditionally()
    {
        // Three faculty at 40% each. The taxable-base cap is a money guard, so it fires regardless of
        // whether the configured ceiling is being enforced.
        var p = MakeProduct(enableDefault: false);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var faculty = ids.ToDictionary(i => i, i => Info(i, "Faculty" + i.ToString()[..2], false));

        var r = Run(p, ids.Select(i => Rule(p.Id, i, 40m)).ToArray(),
            faculty, Settings(maxTotal: 100m, enforce: false));

        Assert.True(r.WasCappedToBase);
        Assert.All(r.Lines, l => Assert.True(l.WasCapped));
        Assert.Equal(1000m, r.TotalShareAmount);          // exactly the taxable base, never more
        Assert.Equal(100m, r.TotalSharePctOfBase);
    }

    [Fact]
    public void RemainingSharePct_ReportsHeadroomForTheProductEditorMeter()
    {
        var p = MakeProduct(enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 35m) }, faculty, Settings(maxTotal: 100m));

        Assert.Equal(35m, r.TotalSharePctOfBase);
        Assert.Equal(65m, r.RemainingSharePct);
        Assert.False(r.ExceedsConfiguredCap);
    }

    // ── Totals ──────────────────────────────────────────────────────────────
    [Fact]
    public void Totals_AlwaysReconcileAgainstTheLines()
    {
        var p = MakeProduct(enableDefault: true, defaultValue: 8m);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var faculty = new Dictionary<Guid, FacultyGstInfo>
        {
            [ids[0]] = Info(ids[0], "Anita", true),
            [ids[1]] = Info(ids[1], "Bhavesh", false),
            [ids[2]] = Info(ids[2], "Chetan", true)
        };

        var r = Run(p, new[] { Rule(p.Id, ids[0], 22.5m) }, faculty, Settings());

        Assert.Equal(3, r.Lines.Count);
        Assert.Equal(r.Lines.Sum(l => l.ShareAmount), r.TotalShareAmount);
        Assert.Equal(r.Lines.Sum(l => l.GstOnShare), r.TotalGstOnShare);
        Assert.Equal(r.TotalShareAmount + r.TotalGstOnShare, r.TotalPayout);
        Assert.All(r.Lines, l => Assert.Equal(l.TotalPayout, l.ShareAmount + l.GstOnShare));
    }

    [Fact]
    public void ZeroRatedProduct_HasNoGstOnAnyShare()
    {
        var p = MakeProduct(1000m, enableDefault: false, gstRate: 0m);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", true) };

        var r = Run(p, new[] { Rule(p.Id, fid, 25m) }, faculty, Settings());

        var line = Assert.Single(r.Lines);
        Assert.Equal(1000m, r.TaxableBase);
        Assert.Equal(250m, line.ShareAmount);
        Assert.Equal(0m, line.GstOnShare);
    }

    [Fact]
    public void ZeroValuedRule_ProducesNoLine()
    {
        var p = MakeProduct(enableDefault: false);
        var fid = Guid.NewGuid();
        var faculty = new Dictionary<Guid, FacultyGstInfo> { [fid] = Info(fid, "Anita", false) };

        var r = Run(p, new[] { Rule(p.Id, fid, 0m) }, faculty, Settings());

        Assert.False(r.HasShare);
    }
}
