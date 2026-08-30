using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// Dispatch record for an order's physical goods (printed books and other shippable items).
/// One row per order — the unique index on <see cref="OrderId"/> enforces it.
///
/// <para><see cref="Status"/> was a free-text string until the reporting module needed to filter
/// on it; the enum values are byte-identical to the strings the dispatch screen already wrote
/// ("Pending" / "Dispatched" / "Delivered"), so the migration is a straight cast.</para>
/// </summary>
public class Shipment : BaseEntity
{
    public Guid OrderId { get; set; }
    public string? Courier { get; set; }

    /// <summary>AWB / consignment number from the courier partner.</summary>
    public string? TrackingNumber { get; set; }

    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public DateTime? DispatchedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? Notes { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>The goods actually in this dispatch. Empty on shipments created before line-item
    /// tracking existed — readers should fall back to the order's shippable items.</summary>
    public ICollection<ShipmentItem> Items { get; set; } = new List<ShipmentItem>();
}

/// <summary>
/// One product line within a dispatch. Without this the shipping report can only say "an order
/// shipped", not what was in the box or how many — and a partial dispatch is unrepresentable.
/// </summary>
public class ShipmentItem : BaseEntity
{
    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    /// <summary>The order line being dispatched. Nullable so a manually-added line (a replacement
    /// copy, say) does not have to correspond to an original sale line.</summary>
    public Guid? OrderItemId { get; set; }

    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>Units in this dispatch — may be fewer than the order line's quantity when the
    /// consignment is split.</summary>
    public int Quantity { get; set; } = 1;
}
