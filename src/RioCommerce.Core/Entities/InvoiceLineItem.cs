namespace RioCommerce.Core.Entities;

/// <summary>
/// One row per OrderItem snapshotted at invoice-generation time. Frozen alongside the parent
/// <see cref="Invoice"/> — the line text, prices and tax breakdown don't change if the
/// referenced product is renamed/repriced later.
/// </summary>
public class InvoiceLineItem : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    /// <summary>Reference back to the source OrderItem — kept for traceability; not used for any join logic.</summary>
    public Guid? OrderItemId { get; set; }
    public Guid? ProductId { get; set; }

    /// <summary>Display sequence on the PDF (1-based).</summary>
    public int LineNumber { get; set; }

    /// <summary>Snapshot of the product title at billing time.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Snapshot of the chosen ProductMode name (e.g. "Live Streaming") or null when N/A.</summary>
    public string? ModeName { get; set; }

    /// <summary>HSN/SAC code if the product carries one — required on B2B invoices over the GST threshold.</summary>
    public string? HsnCode { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal? GstRate { get; set; }
    public decimal GstAmount { get; set; }
    /// <summary>(UnitPrice × Quantity) − Discount + GstAmount, per the order's accounting.</summary>
    public decimal LineTotal { get; set; }
}
