namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// The franchisee commission formulas from "Franchisee Share Calculation Logic" (Antargyan AI /
/// RioStack). Pure — no DB, no clock — so the arithmetic can be tested on its own, the way
/// <see cref="GstRowCalculator"/> is.
///
/// <para><b>Step 1 — reverse out the GST.</b> Prices in this system are GST-INCLUSIVE, so the
/// product's taxable value is backed out of the gross: <c>base = gross × 100 / (100 + rate)</c>.
/// A ₹118 product at 18% has a ₹100 base and ₹18 of embedded GST.</para>
///
/// <para><b>Step 2 — commission on the BASE.</b> A percentage share applies to the pre-GST base,
/// never the gross. The GST inside the sale price belongs to the government and is not part of
/// either party's revenue, so it is not commissionable. 20% of ₹100 = ₹20.</para>
///
/// <para><b>Step 3 — GST on the commission, only if the franchisee is registered.</b> A
/// GST-registered franchisee is supplying a taxable distribution service to the institute, so
/// their commission attracts GST which they will invoice and the institute will reclaim as input
/// tax credit: ₹20 + ₹3.60 = ₹23.60. An unregistered franchisee issues a bill of supply, charges
/// no tax, and is paid the bare ₹20.</para>
///
/// <para><b>Why the registered payout is unchanged from the pre-spec code.</b> The old code
/// computed the share as <c>gross × share%</c>. That is algebraically identical to this spec's
/// registered path, because <c>base × s × (1 + r) = gross × s</c>. So adopting the spec moves the
/// money only for UNREGISTERED franchisees — who were previously overpaid by exactly
/// <c>commission × rate</c>. See <c>docs/franchisee-share-calculation.md</c>.</para>
/// </summary>
public static class FranchiseCommissionMath
{
    /// <summary>
    /// One franchisee commission, decomposed the way a commission tax invoice has to state it.
    /// <see cref="TotalPayout"/> is always <see cref="Commission"/> + <see cref="GstOnCommission"/>
    /// — it is the sum of the rounded parts, not a separately rounded figure, so the payout always
    /// reconciles against the invoice lines rather than differing by a paisa.
    /// </summary>
    /// <param name="Commission">The bare commission (spec: ₹20). Taxable value of the franchisee's supply.</param>
    /// <param name="GstOnCommission">GST charged on that commission (spec: ₹3.60). Zero when unregistered.</param>
    /// <param name="TotalPayout">What the franchisee actually keeps (spec: ₹23.60 / ₹20).</param>
    public readonly record struct CommissionSplit(
        decimal Commission, decimal GstOnCommission, decimal TotalPayout)
    {
        public static readonly CommissionSplit Zero = new(0m, 0m, 0m);

        /// <summary>Adds two splits component-wise — used to fold per-line add-ons into a line total.</summary>
        public CommissionSplit Add(CommissionSplit other) => new(
            Commission + other.Commission,
            GstOnCommission + other.GstOnCommission,
            TotalPayout + other.TotalPayout);

        /// <summary>Multiplies out to a quantity, rounding each component to paise.</summary>
        public CommissionSplit Times(int quantity)
        {
            var c = Math.Round(Commission * quantity, 2);
            var g = Math.Round(GstOnCommission * quantity, 2);
            return new(c, g, c + g);
        }
    }

    /// <summary>
    /// Step 1. The taxable (pre-GST) value inside a GST-inclusive amount. A zero or negative rate
    /// means the amount carries no tax, so it is already its own base.
    /// </summary>
    public static decimal BaseFromGross(decimal grossInclusive, decimal gstRatePct) =>
        gstRatePct <= 0m
            ? Math.Round(grossInclusive, 2)
            : Math.Round(grossInclusive * 100m / (100m + gstRatePct), 2);

    /// <summary>
    /// Steps 1–3 for a PERCENTAGE share. <paramref name="grossInclusive"/> is the GST-inclusive
    /// amount the share applies to (a unit price, or an add-on such as a lecture mode / option).
    /// </summary>
    public static CommissionSplit FromPercent(
        decimal grossInclusive, decimal gstRatePct, decimal sharePct, bool franchiseeIsGstRegistered)
    {
        if (grossInclusive <= 0m || sharePct <= 0m) return CommissionSplit.Zero;

        var taxableBase = BaseFromGross(grossInclusive, gstRatePct);          // Step 1
        var commission = Math.Round(taxableBase * sharePct / 100m, 2);        // Step 2
        var gst = franchiseeIsGstRegistered && gstRatePct > 0m                // Step 3
            ? Math.Round(commission * gstRatePct / 100m, 2)
            : 0m;

        return new CommissionSplit(commission, gst, commission + gst);
    }

    /// <summary>
    /// A FIXED ₹-per-unit share. The specification only covers percentage shares, so this is a
    /// deliberate local decision, documented in <c>docs/franchisee-share-calculation.md</c>:
    /// <b>the configured ₹ value is the total the franchisee is paid</b>, and for a registered
    /// franchisee it is treated as commission-inclusive-of-GST and decomposed back out.
    ///
    /// <para>Chosen over "₹value + GST on top" because it keeps every existing fixed-share
    /// agreement paying exactly what was agreed — no franchisee's cheque changes size — while
    /// still yielding the split a commission invoice needs. If the business wants fixed shares to
    /// behave like percentages (tax added on top), this is the single method to change.</para>
    /// </summary>
    public static CommissionSplit FromFixedPayout(
        decimal payoutPerUnit, decimal gstRatePct, bool franchiseeIsGstRegistered)
    {
        var payout = Math.Round(payoutPerUnit, 2);
        if (payout <= 0m) return CommissionSplit.Zero;

        if (!franchiseeIsGstRegistered || gstRatePct <= 0m)
            return new CommissionSplit(payout, 0m, payout);

        var commission = Math.Round(payout * 100m / (100m + gstRatePct), 2);
        return new CommissionSplit(commission, payout - commission, payout);
    }

    /// <summary>
    /// Scales a split down so its payout equals <paramref name="cappedPayout"/>, preserving the
    /// commission/GST proportion. Needed when a discount drives the order total below the earned
    /// share — we cannot pay out more than the order is worth, and the capped figure still has to
    /// arrive as a commission + tax pair that adds up.
    /// </summary>
    public static CommissionSplit CapTo(CommissionSplit split, decimal cappedPayout)
    {
        if (cappedPayout >= split.TotalPayout) return split;
        if (cappedPayout <= 0m) return CommissionSplit.Zero;

        // No GST component (unregistered) — the whole cap is commission.
        if (split.GstOnCommission <= 0m || split.TotalPayout <= 0m)
            return new CommissionSplit(cappedPayout, 0m, cappedPayout);

        var commission = Math.Round(cappedPayout * split.Commission / split.TotalPayout, 2);
        return new CommissionSplit(commission, cappedPayout - commission, cappedPayout);
    }
}
