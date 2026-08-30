using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class OrderCalculationService : IOrderCalculationService
{
    private const decimal GstRate = 18m;          // platform-wide GST on courses
    private const string SellerState = "Maharashtra";
    private readonly RioCommerceDbContext _db;
    public OrderCalculationService(RioCommerceDbContext db) => _db = db;

    public OrderTotals Compute(IEnumerable<OrderLineInput> lines, string? buyerState)
    {
        decimal subtotal = 0, discount = 0;
        foreach (var l in lines)
        {
            subtotal += l.UnitPrice * l.Quantity;
            discount += l.Discount;
        }
        var total = subtotal - discount;
        if (total < 0) total = 0;                 // never produce a negative order
        var (cgst, sgst, igst) = SplitGst(total, buyerState);
        return new OrderTotals(Math.Round(subtotal, 2), Math.Round(discount, 2), cgst + sgst + igst, cgst, sgst, igst, Math.Round(total, 2));
    }

    public async Task RecalculateAsync(Guid orderId)
    {
        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return;
        foreach (var it in o.Items) it.LineTotal = (it.UnitPrice * it.Quantity) - it.Discount;
        var t = Compute(o.Items.Select(it => new OrderLineInput(it.UnitPrice, it.Quantity, it.Discount)), o.BillingState);
        Apply(o, t);
        await _db.SaveChangesAsync();
    }

    // Applies computed totals onto an order entity (caller saves). Keeps the math in one place.
    public static void Apply(Core.Entities.Order o, OrderTotals t)
    {
        o.Subtotal = t.Subtotal;
        o.DiscountAmount = t.Discount;
        o.GstAmount = t.Gst;
        o.CgstAmount = t.Cgst;
        o.SgstAmount = t.Sgst;
        o.IgstAmount = t.Igst;
        o.TotalAmount = t.Total;
    }

    // Splits the GST-inclusive total into CGST+SGST (intra-state) or IGST (inter-state).
    private static (decimal cgst, decimal sgst, decimal igst) SplitGst(decimal grossTotal, string? buyerState)
    {
        var gst = Math.Round(grossTotal * GstRate / (100m + GstRate), 2);
        var intraState = string.Equals(buyerState?.Trim(), SellerState, StringComparison.OrdinalIgnoreCase);
        if (intraState)
        {
            var half = Math.Round(gst / 2m, 2);
            return (half, gst - half, 0m);
        }
        return (0m, 0m, gst);
    }
}
