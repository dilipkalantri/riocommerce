namespace RioCommerce.Core.Enums;

/// <summary>
/// Lifecycle of a franchisee re-invoice. Mirrors <see cref="InvoiceStatus"/> deliberately — a
/// re-invoice is a tax document in its own right, so it can only be cancelled, never edited.
/// </summary>
public enum ReInvoiceStatus
{
    Draft,
    Issued,
    Cancelled
}

/// <summary>
/// Settlement lifecycle for a faculty/teacher period. Kept separate from
/// <see cref="SettlementStatus"/> (the payout-batch lifecycle) because teacher settlements are
/// tracked independently of the payout system and only ever move through these four states.
/// </summary>
public enum TeacherSettlementStatus
{
    Pending,
    PartiallyPaid,
    Paid,
    Cancelled
}

/// <summary>
/// Dispatch state of a shipment. Replaces the free-text <c>Shipment.Status</c> string so the
/// shipping report can filter on it. The four names match the strings the existing dispatch screen
/// already writes ("Pending" / "Dispatched" / "Delivered"), with Cancelled added for returns.
/// </summary>
public enum ShipmentStatus
{
    Pending,
    Dispatched,
    Delivered,
    Cancelled
}
