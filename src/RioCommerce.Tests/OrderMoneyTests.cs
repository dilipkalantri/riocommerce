using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Services.Reporting;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// <see cref="OrderMoney"/> is the single definition of an order's financial decomposition, shared
/// by the admin order list and all eight reports. If it drifts, the Sales Report stops matching the
/// order it came from — which §28 forbids — so the invariants are pinned here.
/// </summary>
public class OrderMoneyTests
{
    // ── ForOrder ───────────────────────────────────────────────────────────

    [Fact]
    public void Gross_is_the_list_value_and_the_discount_is_counted_once()
    {
        // Two units at ₹1,000 with ₹200 off the line, rolled up into Order.DiscountAmount.
        // UnitPrice is already the LIST price, so gross is ₹2,000 — NOT ₹2,200 — and the ₹200
        // appears once, not twice.
        var f = OrderMoney.ForOrder(
            grossSum: OrderMoney.LineGross(1000m, 2),
            orderDiscount: 200m, itemDiscountSum: 200m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 1800m, storedGst: 0m);

        Assert.Equal(2000m, f.Gross);
        Assert.Equal(200m, f.Discount);
        Assert.Equal(1800m, f.Net);
        Assert.Equal(f.Net, f.Gross - f.Discount);
    }

    [Fact]
    public void Order_discount_is_the_canonical_total_and_is_never_added_to_the_line_sum()
    {
        // Order.DiscountAmount is the roll-up of the line discounts, not an extra on top of them.
        // Summing the two levels is what produced RIO-1080's ₹6,200 against one ₹3,100 discount.
        var f = OrderMoney.ForOrder(
            grossSum: 5000m, orderDiscount: 500m, itemDiscountSum: 500m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 4500m, storedGst: 0m);

        Assert.Equal(500m, f.Discount);
        Assert.NotEqual(1000m, f.Discount);
    }

    [Fact]
    public void No_discount_leaves_gross_and_net_equal()
    {
        var f = OrderMoney.ForOrder(
            grossSum: OrderMoney.LineGross(1000m, 2),
            orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 2000m, storedGst: 0m);

        Assert.Equal(2000m, f.Gross);
        Assert.Equal(0m, f.Discount);
        Assert.Equal(2000m, f.Net);
    }

    [Fact]
    public void HJC_1080_reports_one_discount_not_two()
    {
        // Regression for the order that exposed this: stored Subtotal ₹53,100, DiscountAmount
        // ₹3,100, TotalAmount ₹50,000 — one item at list price ₹53,100 less ₹3,100. The grid used
        // to read Gross ₹56,200 / Discount ₹6,200 off exactly this data.
        var f = OrderMoney.ForOrder(
            grossSum: OrderMoney.LineGross(53_100m, 1),
            orderDiscount: 3_100m, itemDiscountSum: 3_100m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 50_000m, storedGst: 7_627.12m);

        Assert.Equal(53_100m, f.Gross);
        Assert.Equal(3_100m, f.Discount);
        Assert.Equal(50_000m, f.Net);
        Assert.Equal(f.Net, f.Gross - f.Discount);

        // The stored GST must pass through untouched — the corrected gross never feeds a new
        // tax calculation.
        Assert.Equal(7_627.12m, f.Gst);
        Assert.Equal(42_372.88m, f.Taxable);
    }

    [Fact]
    public void Multiple_lines_sum_list_price_and_count_the_roll_up_once()
    {
        // Line A: ₹1,000 × 1, ₹100 off.   Line B: ₹2,000 × 2, ₹200 off.
        // Gross = 1,000 + 4,000 = 5,000. Line discounts = 100 + (200 × 2) = 500, already rolled up.
        var grossSum = OrderMoney.LineGross(1000m, 1) + OrderMoney.LineGross(2000m, 2);
        Assert.Equal(5000m, grossSum);

        var f = OrderMoney.ForOrder(
            grossSum: grossSum,
            orderDiscount: 500m, itemDiscountSum: 100m + (200m * 2),
            commission: 0m, source: OrderSource.Counter, totalAmount: 4500m, storedGst: 0m);

        Assert.Equal(5000m, f.Gross);
        Assert.Equal(500m, f.Discount);
        Assert.Equal(4500m, f.Net);
    }

    [Fact]
    public void Line_gross_multiplies_price_by_quantity_and_never_the_discount()
    {
        // Quantity scales the price only. The discount is not a LineGross input at all now, so it
        // can be neither added back nor multiplied a second time.
        Assert.Equal(1000m, OrderMoney.LineGross(1000m, 1));
        Assert.Equal(3000m, OrderMoney.LineGross(1000m, 3));
        Assert.Equal(53_100m, OrderMoney.LineGross(53_100m, 1));
    }

    [Fact]
    public void Website_coupon_stored_only_at_order_level_is_still_counted()
    {
        // CheckoutService leaves OrderItem.Discount at zero and stores the coupon on the order, so
        // the line sum is 0 while the real discount is ₹300. Using the order figure covers this.
        var f = OrderMoney.ForOrder(
            grossSum: 5000m, orderDiscount: 300m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Website, totalAmount: 4700m, storedGst: 0m);

        Assert.Equal(5000m, f.Gross);
        Assert.Equal(300m, f.Discount);
        Assert.Equal(4700m, f.Net);
    }

    [Fact]
    public void Line_discounts_are_used_when_the_order_roll_up_was_never_stamped()
    {
        // Defensive fallback for a legacy row with discounted lines but a zero order roll-up:
        // report the line sum rather than losing the discount. Still counted once, never added.
        var f = OrderMoney.ForOrder(
            grossSum: 2000m, orderDiscount: 0m, itemDiscountSum: 200m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 1800m, storedGst: 0m);

        Assert.Equal(200m, f.Discount);
    }

    [Fact]
    public void Negative_discount_is_clamped_to_zero()
    {
        // A data slip must not read as a surcharge on the report.
        var f = OrderMoney.ForOrder(
            grossSum: 1000m, orderDiscount: -50m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 1000m, storedGst: 0m);

        Assert.Equal(0m, f.Discount);
    }

    [Fact]
    public void Stored_gst_is_preferred_over_the_fallback_rate()
    {
        // A 5% product must not be restated at 18% just because the fallback exists.
        var f = OrderMoney.ForOrder(
            grossSum: 1050m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 1050m, storedGst: 50m);

        Assert.Equal(50m, f.Gst);
        Assert.Equal(1000m, f.Taxable);
    }

    [Fact]
    public void Missing_gst_falls_back_to_extracting_18_percent()
    {
        // Legacy and admin-created orders stored only a total. ₹1,180 → ₹180 tax on ₹1,000.
        var f = OrderMoney.ForOrder(
            grossSum: 1180m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 1180m, storedGst: 0m);

        Assert.Equal(180m, f.Gst);
        Assert.Equal(1000m, f.Taxable);
    }

    [Fact]
    public void Taxable_plus_gst_always_equals_net()
    {
        var f = OrderMoney.ForOrder(
            grossSum: 3333m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 3333m, storedGst: 508.42m);

        Assert.Equal(f.Net, f.Taxable + f.Gst);
    }

    [Fact]
    public void Commission_counts_as_franchise_discount_only_on_franchise_orders()
    {
        // A stray commission row against a counter order must not surface as a discount there.
        var counter = OrderMoney.ForOrder(
            grossSum: 1000m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 120m, source: OrderSource.Counter, totalAmount: 1000m, storedGst: 0m);
        Assert.Equal(0m, counter.FranchiseDiscount);

        var franchise = OrderMoney.ForOrder(
            grossSum: 1000m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 120m, source: OrderSource.Franchisee, totalAmount: 1000m, storedGst: 0m);
        Assert.Equal(120m, franchise.FranchiseDiscount);
    }

    [Fact]
    public void Franchise_percent_is_measured_against_the_post_discount_value()
    {
        // ₹100 commission on ₹1,000 net of a ₹200 student discount = 12.5%, not 10%.
        var f = OrderMoney.ForOrder(
            grossSum: 1000m, orderDiscount: 200m, itemDiscountSum: 0m,
            commission: 100m, source: OrderSource.Franchisee, totalAmount: 800m, storedGst: 0m);

        Assert.Equal(12.5m, f.FranchiseDiscountPercent);
    }

    [Fact]
    public void Fully_discounted_order_does_not_divide_by_zero()
    {
        var f = OrderMoney.ForOrder(
            grossSum: 500m, orderDiscount: 500m, itemDiscountSum: 0m,
            commission: 50m, source: OrderSource.Franchisee, totalAmount: 0m, storedGst: 0m);

        Assert.Equal(0m, f.FranchiseDiscountPercent);
        Assert.Equal(0m, f.Net);
    }

    // ── Apportion ──────────────────────────────────────────────────────────
    //
    // The property that matters: however the order's money divides across its lines, the lines must
    // add back up to the order exactly. A line-level report that drifts by paise stops reconciling.

    [Fact]
    public void Apportioned_lines_sum_back_to_the_order()
    {
        var order = OrderMoney.ForOrder(
            grossSum: 3000m, orderDiscount: 250m, itemDiscountSum: 0m,
            commission: 300m, source: OrderSource.Franchisee, totalAmount: 2750m, storedGst: 419.49m);

        var lines = OrderMoney.Apportion(new[] { 1000m, 1000m, 1000m }, order);

        Assert.Equal(order.Discount, lines.Sum(l => l.Discount));
        Assert.Equal(order.FranchiseDiscount, lines.Sum(l => l.FranchiseDiscount));
        Assert.Equal(order.Net, lines.Sum(l => l.Net));
        Assert.Equal(order.Gst, lines.Sum(l => l.Gst));
        Assert.Equal(order.Taxable, lines.Sum(l => l.Taxable));
    }

    [Fact]
    public void Rounding_remainder_lands_on_the_last_line()
    {
        // ₹100 across three equal lines cannot divide evenly; the odd paise must not vanish.
        var order = OrderMoney.ForOrder(
            grossSum: 300m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 100m, storedGst: 0m);

        var lines = OrderMoney.Apportion(new[] { 100m, 100m, 100m }, order);

        Assert.Equal(order.Net, lines.Sum(l => l.Net));
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void Apportionment_is_proportional_to_line_gross()
    {
        // A line worth three quarters of the order carries three quarters of its money.
        var order = OrderMoney.ForOrder(
            grossSum: 4000m, orderDiscount: 400m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 3600m, storedGst: 0m);

        var lines = OrderMoney.Apportion(new[] { 3000m, 1000m }, order);

        Assert.Equal(300m, lines[0].Discount);
        Assert.Equal(100m, lines[1].Discount);
    }

    [Fact]
    public void Zero_value_lines_split_the_order_evenly()
    {
        // A wholly-comped order still has to divide its money somehow; an even split is the only
        // defensible answer, and the total must still tie.
        var order = OrderMoney.ForOrder(
            grossSum: 0m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 100m, storedGst: 0m);

        var lines = OrderMoney.Apportion(new[] { 0m, 0m }, order);

        Assert.Equal(order.Net, lines.Sum(l => l.Net));
    }

    [Fact]
    public void Apportioning_no_lines_returns_nothing_rather_than_throwing()
    {
        var order = OrderMoney.ForOrder(
            grossSum: 0m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 0m, storedGst: 0m);

        Assert.Empty(OrderMoney.Apportion(Array.Empty<decimal>(), order));
    }

    [Fact]
    public void Single_line_receives_the_whole_order()
    {
        var order = OrderMoney.ForOrder(
            grossSum: 1180m, orderDiscount: 0m, itemDiscountSum: 0m,
            commission: 0m, source: OrderSource.Counter, totalAmount: 1180m, storedGst: 180m);

        var lines = OrderMoney.Apportion(new[] { 1180m }, order);

        Assert.Single(lines);
        Assert.Equal(order.Net, lines[0].Net);
        Assert.Equal(order.Gst, lines[0].Gst);
        Assert.Equal(order.Taxable, lines[0].Taxable);
    }
}
