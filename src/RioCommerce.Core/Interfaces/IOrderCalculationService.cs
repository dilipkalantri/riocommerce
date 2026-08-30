namespace RioCommerce.Core.Interfaces;

// A single order line for calculation (prices are GST-inclusive, matching the storefront).
public record OrderLineInput(decimal UnitPrice, int Quantity, decimal Discount);

public record OrderTotals(
    decimal Subtotal, decimal Discount, decimal Gst, decimal Cgst, decimal Sgst, decimal Igst, decimal Total);

// Centralised, reusable order math (subtotal, item discounts, GST CGST/SGST/IGST split, grand total).
public interface IOrderCalculationService
{
    OrderTotals Compute(IEnumerable<OrderLineInput> lines, string? buyerState);

    /// <summary>Loads an order + its items, recomputes line totals + order totals, and persists.</summary>
    Task RecalculateAsync(Guid orderId);
}
