using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Services;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The arithmetic the GST report row must satisfy, pinned against real franchise invoices.
///
/// <para><b>Why this exists.</b> The GST report was first written to read <c>TaxableAmount</c> and
/// the CGST/SGST/IGST columns straight off the invoice. That looks reasonable and is wrong: for a
/// franchise order <c>InvoiceService</c> snapshots those on the GROSS basis while snapshotting
/// <c>TotalAmount</c> as the NET the franchisee is billed. FRN-1007 exported as taxable ₹100 +
/// tax ₹18 against an invoice amount of ₹94.40 — a row that does not add up, and a return that
/// would overstate output tax by ₹3.60 on that invoice alone.</para>
///
/// <para>These tests assert the two identities a reader checks by eye, so a future change that
/// reverts to reading the snapshot columns fails here rather than in a filed return.</para>
/// </summary>
public class GstReportRowTests
{
    /// <summary>An order as InvoiceService would have seen it: gross figures throughout.</summary>
    private static Order GrossOrder(decimal total, decimal gst, decimal cgst, decimal sgst, decimal igst = 0m) => new()
    {
        TotalAmount = total,
        GstAmount = gst,
        CgstAmount = cgst,
        SgstAmount = sgst,
        IgstAmount = igst,
        BillingState = "Maharashtra",
        Status = OrderStatus.Confirmed
    };

    [Theory]
    // FRN-1007 — registered franchisee keeps ₹23.60 of a ₹118 sale.
    [InlineData(118.00, 18.00, 94.40, 80.00, 7.20, 7.20, 14.40, 23.60)]
    // FRN-1006 — unregistered franchisee keeps ₹20.00 of the same ₹118 sale.
    [InlineData(118.00, 18.00, 98.00, 83.05, 7.48, 7.47, 14.95, 20.00)]
    public void Franchise_invoice_row_reconciles_both_ways(
        double gross, double grossGst, double invoiced,
        double expectedTaxable, double expectedCgst, double expectedSgst,
        double expectedGst, double expectedShare)
    {
        var o = GrossOrder((decimal)gross, (decimal)grossGst, (decimal)grossGst / 2m, (decimal)grossGst / 2m);

        var g = GstRowCalculator.For(o, refundedFromLedger: 0m, invoicedValue: (decimal)invoiced);

        // Identity 1 — the tax chain: Taxable + CGST + SGST + IGST = Invoice Amount.
        // This is the one the buggy report broke: it reported ₹100 + ₹18 against ₹94.40.
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);

        // Identity 2 — the bifurcation chain: Gross − Franchisee Discount = Invoice Amount.
        Assert.Equal((decimal)gross, g.InvoiceValue + g.FranchiseShare);

        Assert.Equal((decimal)invoiced, g.InvoiceValue);
        Assert.Equal((decimal)expectedTaxable, g.Taxable);
        Assert.Equal((decimal)expectedCgst, g.Cgst);
        Assert.Equal((decimal)expectedSgst, g.Sgst);
        Assert.Equal((decimal)expectedGst, g.Gst);
        Assert.Equal((decimal)expectedShare, g.FranchiseShare);
    }

    [Fact]
    public void Reading_the_invoice_snapshot_directly_would_not_reconcile()
    {
        // Documents the defect rather than the fix: these are the exact columns InvoiceService
        // writes for FRN-1007, and they cannot be used as a report row.
        const decimal snapshotTaxable = 100.00m;   // order.TotalAmount - order.GstAmount, gross basis
        const decimal snapshotCgst = 9.00m;        // order.CgstAmount, gross basis
        const decimal snapshotSgst = 9.00m;
        const decimal snapshotTotal = 94.40m;      // invoiceTotal, NET of the franchisee's share

        Assert.NotEqual(snapshotTotal, snapshotTaxable + snapshotCgst + snapshotSgst);

        // The calculator resolves it to a row that does reconcile.
        var g = GstRowCalculator.For(
            GrossOrder(118m, 18m, 9m, 9m), refundedFromLedger: 0m, invoicedValue: snapshotTotal);

        Assert.Equal(snapshotTotal, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    [Fact]
    public void Non_franchise_invoice_is_unchanged_by_the_scaling()
    {
        // A direct sale is invoiced for its full gross, so there is nothing to scale and the
        // order's own recorded split must survive byte-for-byte.
        var o = GrossOrder(1180m, 180m, 90m, 90m);

        var g = GstRowCalculator.For(o, refundedFromLedger: 0m, invoicedValue: 1180m);

        Assert.Equal(0m, g.FranchiseShare);
        Assert.Equal(90m, g.Cgst);
        Assert.Equal(90m, g.Sgst);
        Assert.Equal(1000m, g.Taxable);
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    [Fact]
    public void Inter_state_franchise_invoice_puts_the_scaled_tax_in_igst()
    {
        var o = GrossOrder(118m, 18m, cgst: 0m, sgst: 0m, igst: 18m);
        o.BillingState = "Karnataka";

        var g = GstRowCalculator.For(o, refundedFromLedger: 0m, invoicedValue: 94.40m);

        Assert.Equal(0m, g.Cgst);
        Assert.Equal(0m, g.Sgst);
        Assert.Equal(14.40m, g.Igst);
        Assert.Equal(g.InvoiceValue, g.Taxable + g.Cgst + g.Sgst + g.Igst);
    }

    [Fact]
    public void Refund_reverses_only_the_tax_that_was_declared()
    {
        // The refund is recorded against the ₹118 order gross, but only ₹14.40 of tax was ever
        // declared on this invoice — reversing the gross ₹18 would claim back tax never paid.
        var o = GrossOrder(118m, 18m, 9m, 9m);

        var g = GstRowCalculator.For(o, refundedFromLedger: 118m, invoicedValue: 94.40m);

        Assert.Equal(14.40m, g.RefundedGst);
        Assert.Equal(g.Gst, g.RefundedGst);
    }
}
