using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

/// <summary>
/// Earned faculty-share ledger: one row per (order item × faculty) that produced a share. The
/// faculty counterpart of <see cref="FranchiseCommissionEntry"/>.
///
/// <para>Snapshots the rule (<see cref="ShareType"/> + <see cref="ShareValue"/>) AND
/// <see cref="WasGstRegistered"/> at the moment of the order. Recording registration status is a
/// deliberate improvement on the franchise ledger, which omitted it — that omission is exactly why
/// the franchise commission split could not be backfilled for historical orders. Editing a sharing
/// rule now changes future earnings only; settled history is immutable.</para>
/// </summary>
public class FacultyShareEntry : BaseEntity
{
    public Guid FacultyId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;

    /// <summary>The pre-GST (taxable) value the share was computed against, multiplied out to the
    /// line quantity. ₹100 per unit for a ₹118 @ 18% product.</summary>
    public decimal BaseAmount { get; set; }

    public SharingType ShareType { get; set; }
    public decimal ShareValue { get; set; }

    /// <summary>The bare share — taxable value of the faculty's own supply, excluding any GST
    /// charged on it. Earnings totals sum this column, so it stays the earning figure.</summary>
    public decimal ShareAmount { get; set; }

    /// <summary>GST on that share; zero for faculty who are not GST-registered.</summary>
    public decimal GstOnShare { get; set; }

    /// <summary>What the faculty is actually paid = <see cref="ShareAmount"/> + <see cref="GstOnShare"/>.</summary>
    public decimal TotalPayout { get; set; }

    /// <summary>The faculty's GST registration status AT THE TIME OF THE ORDER. Read this rather
    /// than re-deriving from <c>Faculty.Gstin</c> — a faculty who registers later must not have
    /// their past payouts restated.</summary>
    public bool WasGstRegistered { get; set; }

    /// <summary>True when the product's combined faculty share exceeded the taxable base and this
    /// line was scaled down proportionally. Recorded so an unexpectedly small payout is
    /// explainable from the data instead of looking like an arithmetic bug.</summary>
    public bool WasCapped { get; set; }

    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

    public Faculty Faculty { get; set; } = null!;
}
