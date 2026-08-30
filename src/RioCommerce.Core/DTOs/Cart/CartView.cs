using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Cart;

public record CartItemView(
    Guid Id, Guid ProductId, string Slug, string Title, CourseLevel Level,
    string? FacultyName, string? ModeName, decimal UnitPrice, decimal Mrp, int Quantity,
    string? Attributes = null,
    // Live product BatchStatus — single source of truth. The Cart line badge AND the Checkout summary
    // both read this. Default Upcoming is just a record-positional fallback for old call sites.
    BatchStatus BatchStatus = BatchStatus.Upcoming,
    // ── 📦 Estimated Delivery Information snapshot on the cart line ──
    // Lecture access timing + notes dispatch timeline propagate from Product.* so every cart row
    // shows the same per-product delivery information rendered on the Detail page.
    string LectureAccessTiming = "Within 24 Hours",
    string NotesDispatchTimeline = "Within 48 Hours")
{
    /// <summary>Display label — same logic as <c>ProductListItem.BatchStatusText</c>.</summary>
    public string BatchStatusText => BatchStatus switch
    {
        BatchStatus.Upcoming    => "Upcoming Batch",
        BatchStatus.Ongoing     => "Ongoing Batch",
        BatchStatus.PreRecorded => "Pre-Recorded",
        BatchStatus.ComingSoon  => "Coming Soon",
        BatchStatus.OutOfStock  => "Out Of Stock",
        _                       => BatchStatus.ToString()
    };
}

public class CartView
{
    public List<CartItemView> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal GstIncluded { get; set; }
    public decimal Total { get; set; }
    public decimal Savings { get; set; }
    public string? CouponCode { get; set; }
    public string? CouponMessage { get; set; }
    public int ItemCount => Items.Sum(i => i.Quantity);
}

public record AddToCartRequest(Guid ProductId, Guid? ModeId);
