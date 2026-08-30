namespace RioCommerce.Core.Enums;

/// <summary>
/// Lifecycle states for an invoice. Distinct from <see cref="OrderStatus"/> because invoices
/// have their own immutability rules (a generated invoice is a legal document; it doesn't
/// change once issued — only voided + reissued).
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Live, valid invoice. Default.</summary>
    Active = 0,
    /// <summary>Voided by admin — kept in the table for audit, marked clearly on PDFs.</summary>
    Cancelled = 1,
}
