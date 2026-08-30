using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Per-order GST figures for the B2B / B2C / combined tax reports: taxable value, the
/// CGST-SGST-IGST split, and the tax sitting inside any refund. Pure — no DB — so the
/// arithmetic can be tested on its own.
/// </summary>
public static class GstRowCalculator
{
    /// <summary>Seller's place of supply. Intra-state supplies attract CGST+SGST, inter-state IGST.</summary>
    public const string SellerState = "Maharashtra";

    /// <param name="InvoiceValue">The amount actually invoiced — what the tax below is charged on.</param>
    /// <param name="Gst">Total GST inside <paramref name="InvoiceValue"/> = Cgst + Sgst + Igst.</param>
    /// <param name="FranchiseShare">Gross − InvoiceValue: the franchisee's share netted off before billing. 0 otherwise.</param>
    public readonly record struct GstFigures(
        decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal Refunded, decimal RefundedGst,
        decimal InvoiceValue, decimal Gst, decimal FranchiseShare);

    /// <summary>A missing billing state is treated as intra-state, matching the receipt and invoice renderers.</summary>
    public static bool IsIntraState(string? billingState) =>
        string.IsNullOrWhiteSpace(billingState)
        || string.Equals(billingState.Trim(), SellerState, StringComparison.OrdinalIgnoreCase);

    /// <param name="refundedFromLedger">Sum of succeeded refunds against this order; 0 when there are none.</param>
    /// <param name="invoicedValue">
    /// The invoice's own total, when one exists. On a FRANCHISE order the franchisee is billed the net
    /// of their share, so the supply we declare tax on is that net — not the order gross, which is the
    /// student-facing price nobody is invoiced. Pass null (or the order total) for everything else and
    /// the figures are unchanged.
    /// </param>
    public static GstFigures For(Order o, decimal refundedFromLedger, decimal? invoicedValue = null)
    {
        var intraState = IsIntraState(o.BillingState);

        // Which way the tax was split. Prefer what the order actually recorded so a state correction
        // made after the fact can't silently flip an already-filed row; fall back to the billing state
        // for admin-created and pre-split legacy orders that stored only a GST total.
        var useIgst = o.IgstAmount > 0 || (o.CgstAmount <= 0 && o.SgstAmount <= 0 && !intraState);

        var invoiced = invoicedValue is { } v && v > 0m ? Math.Round(v, 2) : o.TotalAmount;
        var share = Math.Max(0m, Math.Round(o.TotalAmount - invoiced, 2));

        // Scale the order's tax down to what was invoiced. Scaling the TOTAL proportionally — rather
        // than re-deriving it from a single headline rate — keeps an order with mixed GST rates at its
        // correct blended tax. For a non-franchise order invoiced == gross, so this is a no-op.
        var gst = o.TotalAmount > 0m
            ? Math.Round(o.GstAmount * invoiced / o.TotalAmount, 2)
            : 0m;

        decimal cgst = 0m, sgst = 0m, igst = 0m;
        if (invoiced == o.TotalAmount)
        {
            // Nothing was netted off — keep byte-for-byte what the order recorded, so no row that has
            // already been filed shifts because this method learned to scale.
            cgst = o.CgstAmount; sgst = o.SgstAmount; igst = o.IgstAmount;
            if (cgst == 0m && sgst == 0m && igst == 0m && gst > 0m)
            {
                // Admin-created and pre-split legacy orders store only the GST total — derive the
                // components so CGST+SGST+IGST always ties back to Total GST.
                if (useIgst) igst = gst;
                else { cgst = Math.Round(gst / 2m, 2); sgst = gst - cgst; }
            }
        }
        else
        {
            // Scaled down to the invoiced net. The stored components describe the gross, so they
            // cannot be reused — re-split the scaled total. Rounding is absorbed into SGST.
            if (useIgst) igst = gst;
            else { cgst = Math.Round(gst / 2m, 2); sgst = gst - cgst; }
        }

        // Prices are GST-inclusive, so the taxable value is the invoiced amount less the tax inside it.
        var taxable = Math.Round(invoiced - gst, 2);

        // A Refunded order with no refund row (offline or legacy) still counts as fully reversed.
        var refunded = refundedFromLedger;
        if (refunded == 0m && o.Status == OrderStatus.Refunded) refunded = o.TotalAmount;
        refunded = Math.Clamp(refunded, 0m, o.TotalAmount);

        // Refunds are recorded against the ORDER gross, so reverse the same proportion of the tax we
        // actually declared. A full refund reverses exactly `gst`, never the un-invoiced gross tax.
        var refundedGst = o.TotalAmount > 0m
            ? Math.Round(gst * refunded / o.TotalAmount, 2)
            : 0m;

        return new GstFigures(taxable, cgst, sgst, igst, refunded, refundedGst, invoiced, gst, share);
    }
}
