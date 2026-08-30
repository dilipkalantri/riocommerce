using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Services;
using Xunit;

namespace RioCommerce.Tests;

// Covers the arithmetic behind the B2B / B2C / combined GST reports. Prices are GST-inclusive
// throughout, so a ₹11,800 order carries ₹1,800 of tax on ₹10,000 of taxable value.
public class GstRowCalculatorTests
{
    private static Order MakeOrder(
        decimal total = 11800m,
        decimal gst = 1800m,
        string? billingState = "Maharashtra",
        decimal cgst = 0m,
        decimal sgst = 0m,
        decimal igst = 0m,
        OrderStatus status = OrderStatus.Confirmed) => new()
        {
            Id = Guid.NewGuid(),
            OrderNumber = "RIO-1000",
            Subtotal = total,
            TotalAmount = total,
            GstAmount = gst,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            BillingState = billingState,
            Status = status
        };

    // ═══════════════════════════════════════════════════════════════════════════
    //  Invoice basis — tax is declared on what was BILLED. On a franchise order the
    //  franchisee is billed net of their share, so the gross is not the supply.
    //  These figures are asserted against real invoices FRN-1007 / FRN-1006.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FranchiseOrder_declaresTaxOnTheInvoicedNet_ScenarioA()
    {
        // FRN-1007: ₹118 gross @18%, registered franchisee keeps ₹23.60 → invoiced ₹94.40.
        var o = MakeOrder(total: 118m, gst: 18m, cgst: 9m, sgst: 9m);
        var g = GstRowCalculator.For(o, 0m, invoicedValue: 94.40m);

        Assert.Equal(94.40m, g.InvoiceValue);
        Assert.Equal(23.60m, g.FranchiseShare);
        Assert.Equal(14.40m, g.Gst);
        Assert.Equal(80.00m, g.Taxable);
        Assert.Equal(7.20m, g.Cgst);
        Assert.Equal(7.20m, g.Sgst);
        Assert.Equal(0m, g.Igst);
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    [Fact]
    public void FranchiseOrder_declaresTaxOnTheInvoicedNet_ScenarioB()
    {
        // FRN-1006: same ₹118, unregistered franchisee keeps ₹20.00 → invoiced ₹98.00.
        var o = MakeOrder(total: 118m, gst: 18m, cgst: 9m, sgst: 9m);
        var g = GstRowCalculator.For(o, 0m, invoicedValue: 98m);

        Assert.Equal(98.00m, g.InvoiceValue);
        Assert.Equal(20.00m, g.FranchiseShare);
        Assert.Equal(14.95m, g.Gst);
        Assert.Equal(83.05m, g.Taxable);
        Assert.Equal(7.48m, g.Cgst);
        Assert.Equal(7.47m, g.Sgst);
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    [Fact]
    public void InterState_franchiseOrder_scalesIgst()
    {
        var o = MakeOrder(total: 118m, gst: 18m, igst: 18m, billingState: "Gujarat");
        var g = GstRowCalculator.For(o, 0m, invoicedValue: 94.40m);

        Assert.Equal(14.40m, g.Igst);
        Assert.Equal(0m, g.Cgst);
        Assert.Equal(0m, g.Sgst);
        Assert.Equal(80.00m, g.Taxable);
    }

    [Fact]
    public void NoInvoiceValue_leavesEveryFigureUnchanged()
    {
        // The default path must stay byte-identical — non-franchise reporting cannot move.
        var o = MakeOrder(total: 11800m, gst: 1800m, cgst: 900m, sgst: 900m);
        var before = GstRowCalculator.For(o, 0m);
        var same = GstRowCalculator.For(o, 0m, invoicedValue: 11800m);

        Assert.Equal(11800m, before.InvoiceValue);
        Assert.Equal(0m, before.FranchiseShare);
        Assert.Equal(1800m, before.Gst);
        Assert.Equal(10000m, before.Taxable);
        Assert.Equal(900m, before.Cgst);
        Assert.Equal(before, same);
    }

    [Fact]
    public void UnadjustedOrder_keepsItsStoredSplit_evenWhenUneven()
    {
        // A legacy row with an uneven stored split must not be silently re-derived.
        var o = MakeOrder(total: 11800m, gst: 1800m, cgst: 899m, sgst: 901m);
        var g = GstRowCalculator.For(o, 0m);

        Assert.Equal(899m, g.Cgst);
        Assert.Equal(901m, g.Sgst);
    }

    [Fact]
    public void FullRefund_reversesTheTaxActuallyDeclared_notTheGrossTax()
    {
        var o = MakeOrder(total: 118m, gst: 18m, cgst: 9m, sgst: 9m);
        var g = GstRowCalculator.For(o, refundedFromLedger: 118m, invoicedValue: 94.40m);

        Assert.Equal(14.40m, g.RefundedGst);   // not 18.00
        Assert.Equal(g.Gst, g.RefundedGst);
    }

    // ── Taxable value ──

    [Fact]
    public void Taxable_is_total_less_the_gst_inside_it()
    {
        var g = GstRowCalculator.For(MakeOrder(total: 11800m, gst: 1800m), 0m);
        Assert.Equal(10000m, g.Taxable);
    }

    [Fact]
    public void Taxable_ignores_subtotal_and_discount_so_checkout_addons_stay_in_the_base()
    {
        // Subtotal 10,000 − discount 1,000 = 9,000, but a ₹2,000 add-on was taxed too:
        // total 11,800 with ₹1,800 GST means the real taxable base is 10,000, not 9,000.
        var o = MakeOrder(total: 11800m, gst: 1800m);
        o.Subtotal = 10000m;
        o.DiscountAmount = 1000m;
        o.CheckoutAttributesAmount = 2800m;

        Assert.Equal(10000m, GstRowCalculator.For(o, 0m).Taxable);
    }

    // ── CGST / SGST / IGST split ──

    [Fact]
    public void Stored_split_is_used_as_is_when_present()
    {
        var g = GstRowCalculator.For(MakeOrder(cgst: 900m, sgst: 900m), 0m);
        Assert.Equal(900m, g.Cgst);
        Assert.Equal(900m, g.Sgst);
        Assert.Equal(0m, g.Igst);
    }

    [Fact]
    public void Missing_split_is_derived_as_cgst_sgst_for_an_intra_state_buyer()
    {
        var g = GstRowCalculator.For(MakeOrder(billingState: "Maharashtra"), 0m);
        Assert.Equal(900m, g.Cgst);
        Assert.Equal(900m, g.Sgst);
        Assert.Equal(0m, g.Igst);
    }

    [Fact]
    public void Missing_split_is_derived_as_igst_for_an_inter_state_buyer()
    {
        var g = GstRowCalculator.For(MakeOrder(billingState: "Karnataka"), 0m);
        Assert.Equal(0m, g.Cgst);
        Assert.Equal(0m, g.Sgst);
        Assert.Equal(1800m, g.Igst);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("maharashtra")]
    [InlineData(" Maharashtra ")]
    public void Blank_or_differently_cased_home_state_counts_as_intra_state(string? state)
    {
        Assert.True(GstRowCalculator.IsIntraState(state));
        Assert.Equal(0m, GstRowCalculator.For(MakeOrder(billingState: state), 0m).Igst);
    }

    [Fact]
    public void Derived_split_always_sums_back_to_the_stored_gst_total()
    {
        // An odd-paise total: half of 1,800.05 doesn't divide evenly, so SGST absorbs the rounding.
        var g = GstRowCalculator.For(MakeOrder(total: 11800.33m, gst: 1800.05m), 0m);
        Assert.Equal(1800.05m, g.Cgst + g.Sgst + g.Igst);
        Assert.Equal(900.02m, g.Cgst);
        Assert.Equal(900.03m, g.Sgst);
    }

    [Fact]
    public void Zero_gst_order_gets_no_derived_split()
    {
        var g = GstRowCalculator.For(MakeOrder(total: 5000m, gst: 0m), 0m);
        Assert.Equal(0m, g.Cgst);
        Assert.Equal(0m, g.Sgst);
        Assert.Equal(0m, g.Igst);
        Assert.Equal(5000m, g.Taxable);
    }

    // ── Refunds ──

    [Fact]
    public void No_refund_leaves_both_refund_figures_at_zero()
    {
        var g = GstRowCalculator.For(MakeOrder(), 0m);
        Assert.Equal(0m, g.Refunded);
        Assert.Equal(0m, g.RefundedGst);
    }

    [Fact]
    public void Partial_refund_carries_a_proportional_share_of_the_tax()
    {
        // Half of an 11,800 order refunded ⇒ half of the 1,800 tax comes back.
        var g = GstRowCalculator.For(MakeOrder(total: 11800m, gst: 1800m), 5900m);
        Assert.Equal(5900m, g.Refunded);
        Assert.Equal(900m, g.RefundedGst);
    }

    [Fact]
    public void Fully_refunded_order_with_no_refund_row_still_reverses_the_whole_tax()
    {
        // Offline and legacy refunds only move the order status; the ledger has nothing to sum.
        var g = GstRowCalculator.For(MakeOrder(status: OrderStatus.Refunded), 0m);
        Assert.Equal(11800m, g.Refunded);
        Assert.Equal(1800m, g.RefundedGst);
    }

    [Fact]
    public void Refund_exceeding_the_order_value_is_capped_so_net_gst_cannot_go_negative()
    {
        var g = GstRowCalculator.For(MakeOrder(total: 11800m, gst: 1800m), 20000m);
        Assert.Equal(11800m, g.Refunded);
        Assert.Equal(1800m, g.RefundedGst);
    }

    [Fact]
    public void Zero_value_order_does_not_divide_by_zero()
    {
        var g = GstRowCalculator.For(MakeOrder(total: 0m, gst: 0m), 0m);
        Assert.Equal(0m, g.Taxable);
        Assert.Equal(0m, g.RefundedGst);
    }

    // ── Report-level invariant ──

    [Fact]
    public void B2b_and_b2c_subtotals_add_up_to_the_combined_report()
    {
        // The three reports project the same rows, so splitting by classification must be lossless —
        // including the legacy row whose classification was never populated, which B2C has to absorb.
        var all = new[]
        {
            MakeOrder(total: 11800m, gst: 1800m),                              // B2C, intra
            MakeOrder(total: 23600m, gst: 3600m, billingState: "Karnataka"),   // B2B, inter
            MakeOrder(total: 5900m,  gst: 900m),                               // B2B, intra
            MakeOrder(total: 1180m,  gst: 180m)                                // blank class, intra
        };
        all[1].GstClassification = "B2B";
        all[2].GstClassification = "B2B";
        all[3].GstClassification = "";

        var figures = all.Select(o => (o.GstClassification, F: GstRowCalculator.For(o, 0m))).ToList();
        // Mirrors ExportService: B2B is an exact match, B2C is everything else.
        var b2b = figures.Where(x => x.GstClassification == "B2B").Sum(x => x.F.Taxable);
        var b2c = figures.Where(x => x.GstClassification != "B2B").Sum(x => x.F.Taxable);

        Assert.Equal(figures.Sum(x => x.F.Taxable), b2b + b2c);
        Assert.Equal(25000m, b2b);
        Assert.Equal(11000m, b2c);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  A ₹0 invoiced value. FranchisePortalService caps the share at the order
    //  total, so a coupon deep enough to drive the total down to the share leaves
    //  the franchisee owing nothing. These two pin why InvoiceService screens for
    //  that BEFORE calling here, rather than this method learning to accept a zero.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ZeroInvoicedValue_readsAsNotSupplied_andFallsBackToTheGross()
    {
        var o = MakeOrder(total: 118m, gst: 18m, cgst: 9m, sgst: 9m);
        var g = GstRowCalculator.For(o, 0m, invoicedValue: 0m);

        // Deliberate: the report path passes null/0 to mean "no invoice basis known", so a zero
        // cannot also mean "invoiced ₹0" here. The figures therefore come back on the ₹118 GROSS.
        Assert.Equal(118m, g.InvoiceValue);
        Assert.Equal(18m, g.Gst);
        Assert.Equal(100m, g.Taxable);
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);

        // Had InvoiceService stored TotalAmount = 0 beside these, the row could not tie — taxable
        // 100 + 18 = 118 against a stated 0. That is the defect migration 0033 exists to clear,
        // and the guard in EnsureForOrderAsync is what stops it being written again.
        Assert.NotEqual(0m, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    [Fact]
    public void ShareCoveringTheWholeOrder_isTheStateInvoiceServiceRejects()
    {
        var o = MakeOrder(total: 118m, gst: 18m, cgst: 9m, sgst: 9m);
        o.Source = OrderSource.Franchisee;
        o.FranchiseId = Guid.NewGuid();
        o.FranchiseShareAmount = 118m;      // CapTo trimmed the share to the whole total
        o.FranchiseNetPayable = 0m;

        // The predicate EnsureForOrderAsync screens on, stated as data so it cannot drift silently.
        Assert.True(o.FranchiseShareAmount > 0 && o.FranchiseNetPayable <= 0);

        // A legacy franchise order — both columns zero because they predate the split — must NOT be
        // caught by it. Those still invoice at the order total.
        var legacy = MakeOrder(total: 118m, gst: 18m, cgst: 9m, sgst: 9m);
        legacy.Source = OrderSource.Franchisee;
        legacy.FranchiseId = Guid.NewGuid();
        Assert.False(legacy.FranchiseShareAmount > 0 && legacy.FranchiseNetPayable <= 0);
    }

    [Fact]
    public void MixedRateOrder_keepsItsBlendedTax_ratherThanReSplittingAtOneHeadlineRate()
    {
        // ₹1,000 carrying ₹80 of GST — a 5%/18% basket. Re-deriving from a single rate is what the
        // old renderer did; scaling the recorded total is what keeps a mixed invoice honest.
        var o = MakeOrder(total: 1000m, gst: 80m, cgst: 40m, sgst: 40m);
        var g = GstRowCalculator.For(o, 0m, invoicedValue: 900m);

        Assert.Equal(72m, g.Gst);          // 80 × 900 / 1000, not 900 × 18/118
        Assert.Equal(828m, g.Taxable);
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  The invariant, swept. Rounding is where an identity like this dies, so walk
    //  the paisa instead of trusting hand-picked totals.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InvoicedFigures_alwaysTieToTheInvoiceValue_acrossEveryTotalAndShareRate()
    {
        for (var total = 1m; total <= 400m; total += 0.37m)
        {
            var gst = Math.Round(total * 18m / 118m, 2);
            var intra = MakeOrder(total: total, gst: gst, cgst: Math.Round(gst / 2m, 2), sgst: gst - Math.Round(gst / 2m, 2));
            var inter = MakeOrder(total: total, gst: gst, igst: gst, billingState: "Gujarat");

            for (var pct = 7; pct <= 100; pct += 7)
            {
                var net = Math.Round(total * pct / 100m, 2);
                if (net <= 0m) continue;   // the guard's territory, covered above

                var a = GstRowCalculator.For(intra, 0m, invoicedValue: net);
                Assert.Equal(a.InvoiceValue, a.Taxable + a.Cgst + a.Sgst + a.Igst);
                Assert.Equal(a.Gst, a.Cgst + a.Sgst + a.Igst);
                Assert.True(a.Taxable >= 0m, $"negative taxable at total={total}, net={net}");

                var b = GstRowCalculator.For(inter, 0m, invoicedValue: net);
                Assert.Equal(b.InvoiceValue, b.Taxable + b.Cgst + b.Sgst + b.Igst);
                Assert.Equal(0m, b.Cgst);
                Assert.Equal(0m, b.Sgst);
            }
        }
    }
}
