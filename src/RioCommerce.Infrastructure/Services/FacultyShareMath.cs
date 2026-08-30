namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Faculty revenue-share arithmetic. Pure — no DB, no clock — so it can be tested on its own the way
/// <see cref="FranchiseCommissionMath"/> and <see cref="GstRowCalculator"/> are.
///
/// <para><b>The formula is the same specification.</b> Steps 1–3 ("reverse out the GST → share on
/// the pre-GST base → GST on the share only if the payee is registered") describe a party-agnostic
/// share calculation; nothing in them is specific to a franchisee. So this class DELEGATES the
/// scalar arithmetic to <see cref="FranchiseCommissionMath"/> rather than copying it. Two
/// independent implementations of the same money formula would drift, and the one that drifted
/// silently would be whichever had fewer tests.</para>
///
/// <para>The names are the only thing that differs: a franchisee earns a <i>commission</i>, a
/// faculty member earns a <i>share</i>. <see cref="ShareSplit"/> is the faculty-facing shape of
/// <see cref="FranchiseCommissionMath.CommissionSplit"/>.
/// <c>FacultyShareMathTests.Faculty_And_Franchise_Math_Agree</c> pins the equivalence, so if the two
/// ever diverge a test fails rather than a payout quietly changing.</para>
///
/// <para><b>What is genuinely new here</b> is <see cref="AllocateWithinBase"/>. A product has one
/// franchisee but can have many faculty, so the combined share needs a ceiling that the franchise
/// code never required. See that method for why the cap sits on the bare share and not the
/// payout.</para>
/// </summary>
public static class FacultyShareMath
{
    /// <summary>
    /// One faculty member's share on one unit, decomposed the way their fee invoice has to state it.
    /// <see cref="TotalPayout"/> is always <see cref="Share"/> + <see cref="GstOnShare"/> — the sum
    /// of the rounded parts rather than a separately rounded figure, so a payout advice always
    /// reconciles against its lines instead of differing by a paisa.
    /// </summary>
    /// <param name="Share">The bare share (spec: ₹20). Taxable value of the faculty's own supply.</param>
    /// <param name="GstOnShare">GST charged on that share (spec: ₹3.60). Zero when unregistered.</param>
    /// <param name="TotalPayout">What the faculty actually receives (spec: ₹23.60 / ₹20).</param>
    public readonly record struct ShareSplit(decimal Share, decimal GstOnShare, decimal TotalPayout)
    {
        public static readonly ShareSplit Zero = new(0m, 0m, 0m);

        /// <summary>Adds two splits component-wise.</summary>
        public ShareSplit Add(ShareSplit other) => new(
            Share + other.Share,
            GstOnShare + other.GstOnShare,
            TotalPayout + other.TotalPayout);

        /// <summary>Multiplies out to a line quantity, rounding each component to paise.</summary>
        public ShareSplit Times(int quantity)
        {
            var s = Math.Round(Share * quantity, 2);
            var g = Math.Round(GstOnShare * quantity, 2);
            return new(s, g, s + g);
        }

        internal static ShareSplit From(FranchiseCommissionMath.CommissionSplit c) =>
            new(c.Commission, c.GstOnCommission, c.TotalPayout);
    }

    /// <summary>Step 1 — the taxable (pre-GST) value inside a GST-inclusive amount.</summary>
    public static decimal BaseFromGross(decimal grossInclusive, decimal gstRatePct) =>
        FranchiseCommissionMath.BaseFromGross(grossInclusive, gstRatePct);

    /// <summary>
    /// Steps 1–3 for a PERCENTAGE share. <paramref name="grossInclusive"/> is the GST-inclusive
    /// amount the share applies to (the effective unit price).
    /// </summary>
    public static ShareSplit FromPercent(
        decimal grossInclusive, decimal gstRatePct, decimal sharePct, bool facultyIsGstRegistered) =>
        ShareSplit.From(FranchiseCommissionMath.FromPercent(
            grossInclusive, gstRatePct, sharePct, facultyIsGstRegistered));

    /// <summary>
    /// A FIXED ₹-per-unit share. Same local convention the franchise side settled on and for the
    /// same reason: <b>the configured ₹ value is the total the faculty is paid</b>, decomposed back
    /// out for a registered faculty rather than having tax added on top — so no existing agreement
    /// changes what it pays. To switch to "₹value + GST on top", change
    /// <see cref="FranchiseCommissionMath.FromFixedPayout"/>; it is the only place this is decided,
    /// for both parties.
    /// </summary>
    public static ShareSplit FromFixedPayout(
        decimal payoutPerUnit, decimal gstRatePct, bool facultyIsGstRegistered) =>
        ShareSplit.From(FranchiseCommissionMath.FromFixedPayout(
            payoutPerUnit, gstRatePct, facultyIsGstRegistered));

    /// <summary>
    /// Scales a split down so its payout equals <paramref name="cappedPayout"/>, preserving the
    /// share/GST proportion.
    /// </summary>
    public static ShareSplit CapTo(ShareSplit split, decimal cappedPayout) =>
        ShareSplit.From(FranchiseCommissionMath.CapTo(
            new FranchiseCommissionMath.CommissionSplit(split.Share, split.GstOnShare, split.TotalPayout),
            cappedPayout));

    /// <summary>
    /// The multi-faculty guard: bounds the COMBINED bare share across every faculty on one product
    /// so it can never exceed <paramref name="availableBase"/>, the taxable value of the sale.
    ///
    /// <para><b>Why the cap is on the bare share, not the payout.</b> The share is the institute's
    /// cost; the GST charged on it is a pass-through the institute reclaims as input tax credit, so
    /// it is not a slice of the sale and must not consume cap headroom. Capping the payout instead
    /// would pay registered and unregistered faculty different <i>fees</i> for the same agreed
    /// percentage, which is the opposite of what the specification does.</para>
    ///
    /// <para><b>Why a cap is needed at all.</b> Unlike a franchisee, faculty shares are configured
    /// independently per person and are not required to sum to anything. Three faculty on 40% each
    /// is a legitimate data entry that would otherwise pay out 120% of the base — a loss on every
    /// sale. <c>FacultySettings.MaxTotalSharePct</c> catches this at save time; this method is the
    /// unconditional backstop for rules that predate the setting, were imported from Excel, or were
    /// each individually valid while collectively not.</para>
    ///
    /// <para>Scaling is proportional, so the relative agreements are preserved, and the remainder
    /// from rounding is distributed largest-remainder-first so the scaled shares sum to exactly
    /// <paramref name="availableBase"/> rather than to a paisa either side of it.</para>
    /// </summary>
    /// <returns>
    /// One entry per input split, in the same order, each paired with whether it was scaled down.
    /// Returned unchanged when the total already fits.
    /// </returns>
    public static List<(ShareSplit Split, bool WasCapped)> AllocateWithinBase(
        IReadOnlyList<ShareSplit> splits, decimal availableBase, decimal gstRatePct, bool[] isGstRegistered)
    {
        var result = new List<(ShareSplit, bool)>(splits.Count);
        if (splits.Count == 0) return result;

        var totalShare = splits.Sum(s => s.Share);

        // Fits (the common case), or there is nothing to distribute — hand back untouched.
        if (totalShare <= availableBase || totalShare <= 0m || availableBase <= 0m)
        {
            if (availableBase <= 0m && totalShare > 0m)
            {
                // A zero/negative base can support no share at all — a free or fully-discounted line.
                foreach (var _ in splits) result.Add((ShareSplit.Zero, true));
                return result;
            }
            foreach (var s in splits) result.Add((s, false));
            return result;
        }

        // Proportional target for each faculty, rounded down to paise, with the shortfall handed out
        // by descending fractional remainder. Rebuilding each split from its scaled bare share (per
        // that faculty's own registration status) keeps GST correct: a scaled-down registered
        // faculty still gets tax on their reduced fee, an unregistered one still gets none.
        var scaled = new decimal[splits.Count];
        var remainders = new (int Index, decimal Frac)[splits.Count];
        var allocated = 0m;

        for (var i = 0; i < splits.Count; i++)
        {
            var exact = splits[i].Share * availableBase / totalShare;
            var floor = Math.Floor(exact * 100m) / 100m;
            scaled[i] = floor;
            remainders[i] = (i, exact - floor);
            allocated += floor;
        }

        var leftoverPaise = (int)Math.Round((availableBase - allocated) * 100m, MidpointRounding.AwayFromZero);
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Frac).ThenBy(r => r.Index))
        {
            if (leftoverPaise <= 0) break;
            scaled[index] += 0.01m;
            leftoverPaise--;
        }

        for (var i = 0; i < splits.Count; i++)
        {
            var share = Math.Round(scaled[i], 2);
            var registered = i < isGstRegistered.Length && isGstRegistered[i];
            var gst = registered && gstRatePct > 0m ? Math.Round(share * gstRatePct / 100m, 2) : 0m;
            result.Add((new ShareSplit(share, gst, share + gst), true));
        }

        return result;
    }

    /// <summary>
    /// The share expressed as a percentage of the taxable base — the comparable figure for a total
    /// across faculty whose rules mix percentages and fixed ₹ amounts. A fixed ₹200 share on a ₹1000
    /// base counts as 20% for cap purposes.
    ///
    /// <para>Uses the bare share, so a registered and an unregistered faculty on the same agreed
    /// rate contribute the same amount to the total. Returns 0 for a non-positive base — there is no
    /// meaningful percentage of nothing, and the caller's cap check is not the right place to
    /// surface that.</para>
    /// </summary>
    public static decimal SharePctOfBase(decimal share, decimal taxableBase) =>
        taxableBase <= 0m ? 0m : Math.Round(share * 100m / taxableBase, 2);
}
