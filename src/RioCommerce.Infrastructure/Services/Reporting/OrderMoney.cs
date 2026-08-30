using RioCommerce.Core.Enums;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// The single definition of an order's financial decomposition — Gross, Discount, Franchisee
/// Discount, Taxable, GST and Net.
///
/// <para>These formulas were previously written inline inside <c>OrderAdminService.ListAsync</c>.
/// They are lifted here unchanged so the order list and all eight reports compute the same figures
/// from the same code, which is what §26 and §36.13/14 require. Pure arithmetic, no database, so
/// it is testable on its own.</para>
/// </summary>
public static class OrderMoney
{
    /// <summary>Fallback rate used only when an order stored no GST at all — legacy and
    /// admin-created rows. Matches the order list's existing behaviour.</summary>
    public const decimal FallbackGstRate = 18m;

    /// <param name="Gross">List value before any discount: Σ UnitPrice × Qty.</param>
    /// <param name="Discount">Student-facing discount: <c>Order.DiscountAmount</c>, which is already
    /// the canonical total for the order.</param>
    /// <param name="FranchiseDiscount">Franchisee's commission share. Zero for non-franchise orders.</param>
    /// <param name="FranchiseDiscountPercent">That share as a percentage of the post-discount value.</param>
    /// <param name="Net">Final amount payable, GST inclusive.</param>
    /// <param name="Taxable">Pre-GST value inside <paramref name="Net"/>.</param>
    /// <param name="Gst">GST inside <paramref name="Net"/>.</param>
    public readonly record struct Figures(
        decimal Gross, decimal Discount, decimal FranchiseDiscount, decimal FranchiseDiscountPercent,
        decimal Net, decimal Taxable, decimal Gst);

    /// <summary>
    /// Order-level decomposition. Mirrors the order list exactly, including its guards: a negative
    /// discount is clamped to zero, the franchisee share only counts on franchise-sourced orders,
    /// and a missing stored GST falls back to the 18% extraction.
    /// </summary>
    public static Figures ForOrder(
        decimal grossSum, decimal orderDiscount, decimal itemDiscountSum,
        decimal commission, OrderSource source, decimal totalAmount, decimal storedGst)
    {
        var gross = grossSum;

        // Order.DiscountAmount is the CANONICAL TOTAL discount — not an order-level extra sitting on
        // top of the lines. Every creation path rolls the line discounts up into it: the website
        // checkout stores the coupon there and leaves OrderItem.Discount at zero, the franchise
        // portal stores per-line discounts PLUS the coupon, and the admin/counter paths run through
        // OrderCalculationService.Compute, which sets it to Σ item.Discount. Adding the two levels
        // together therefore counted the same rupees twice — RIO-1080 reported ₹6,200 of discount
        // against a single ₹3,100 one. The line sum survives only as a fallback for a row that never
        // had the roll-up stamped; it is never ADDED, so no rupee can be counted twice.
        var discount = orderDiscount > 0 ? orderDiscount : itemDiscountSum;
        if (discount < 0) discount = 0;

        var afterDiscount = gross - discount;

        var franchiseDiscount = source == OrderSource.Franchisee ? Math.Max(0m, commission) : 0m;
        var franchisePct = afterDiscount > 0 && franchiseDiscount > 0
            ? Math.Round(franchiseDiscount / afterDiscount * 100m, 2)
            : 0m;

        var net = totalAmount;
        var gst = storedGst > 0
            ? storedGst
            : Math.Round(net * FallbackGstRate / (100m + FallbackGstRate), 2);
        var taxable = net - gst;
        if (taxable < 0) taxable = Math.Round(net * 100m / (100m + FallbackGstRate), 2);

        return new Figures(
            Math.Round(gross, 2), Math.Round(discount, 2),
            Math.Round(franchiseDiscount, 2), franchisePct,
            Math.Round(net, 2), Math.Round(taxable, 2), Math.Round(gst, 2));
    }

    /// <summary>
    /// Gross for one order line: <c>UnitPrice × Quantity</c>.
    ///
    /// <para><c>OrderItem.UnitPrice</c> is stored as the LIST price, with the line's discount kept
    /// separately and <c>LineTotal = (UnitPrice × Qty) − Discount</c> — see
    /// <c>OrderCalculationService.Compute</c>, which every creation path routes through. Adding the
    /// discount back re-inflated an already-gross figure, so a ₹53,100 line read ₹56,200. The
    /// discount is no longer a parameter, so it cannot be added back by mistake again.</para>
    /// </summary>
    public static decimal LineGross(decimal unitPrice, int quantity)
        => unitPrice * quantity;

    /// <summary>
    /// Splits an order's figures across its lines, pro-rata by line gross.
    ///
    /// <para>Line-level reports (Sales, Franchisee-Wise) must sum back to the order they came from
    /// — §28 says the Sales Report amount has to match the order. Order-level money that has no
    /// natural line (a coupon, shipping, checkout add-ons, the franchisee's commission) therefore
    /// cannot simply be dropped or repeated per line; it is apportioned.</para>
    ///
    /// <para>Rounding remainder from the apportionment lands on the LAST line, so the column totals
    /// tie to the paise rather than drifting by a few paise per order.</para>
    /// </summary>
    /// <param name="lineGross">Gross per line, in row order.</param>
    /// <param name="order">The order-level figures being split.</param>
    /// <returns>Per-line figures in the same order as <paramref name="lineGross"/>.</returns>
    public static IReadOnlyList<Figures> Apportion(IReadOnlyList<decimal> lineGross, Figures order)
    {
        var n = lineGross.Count;
        if (n == 0) return Array.Empty<Figures>();

        var totalGross = lineGross.Sum();
        var result = new Figures[n];

        // Running sums, so the final line can absorb whatever rounding left behind.
        decimal accDiscount = 0, accFranchise = 0, accNet = 0, accGst = 0;

        for (var i = 0; i < n; i++)
        {
            var isLast = i == n - 1;
            // An order whose lines are all zero-value still has to divide its order-level money
            // somehow; an even split is the only defensible option.
            var share = totalGross > 0 ? lineGross[i] / totalGross : 1m / n;

            decimal discount, franchise, net, gst;
            if (isLast)
            {
                discount = order.Discount - accDiscount;
                franchise = order.FranchiseDiscount - accFranchise;
                net = order.Net - accNet;
                gst = order.Gst - accGst;
            }
            else
            {
                discount = Math.Round(order.Discount * share, 2);
                franchise = Math.Round(order.FranchiseDiscount * share, 2);
                net = Math.Round(order.Net * share, 2);
                gst = Math.Round(order.Gst * share, 2);
                accDiscount += discount;
                accFranchise += franchise;
                accNet += net;
                accGst += gst;
            }

            result[i] = new Figures(
                Math.Round(lineGross[i], 2),
                discount,
                franchise,
                order.FranchiseDiscountPercent,
                net,
                Math.Round(net - gst, 2),
                gst);
        }

        return result;
    }
}
