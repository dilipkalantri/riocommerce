using System.ComponentModel.DataAnnotations.Schema;
using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// A legal invoice issued against a paid order. Once generated it's immutable — totals and
/// numbers don't shift if the underlying order is later edited. All money + customer fields
/// are SNAPSHOTS taken at the moment of generation so the invoice always reflects what was
/// actually billed, not what the order looks like today.
///
/// One invoice per order. Idempotent generation — calling EnsureForOrderAsync multiple times
/// for the same paid order returns the existing row, doesn't create a duplicate.
///
/// Backward-compat note: this entity is also written by the legacy <c>OrderAdminService.
/// GetOrCreateInvoiceAsync</c> (which only fills <see cref="InvoiceNumber"/>, <see cref="InvoiceDate"/>,
/// <see cref="TaxableAmount"/>, and the GST totals). Customer / company snapshot fields stay null
/// in that case. Newer <c>IInvoiceService.EnsureForOrderAsync</c> fills everything.
/// </summary>
public class Invoice : BaseEntity
{
    /// <summary>Human-readable invoice number — format <c>RIO-INV-YYYYMM-NNNN</c>.
    /// Sequence resets monthly. Unique-indexed.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>Calendar date the invoice was issued. Kept as <see cref="DateOnly"/> to stay
    /// compatible with the original entity shape — printable invoices only show the date.</summary>
    public DateOnly InvoiceDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    // ── Order linkage ──
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    /// <summary>Snapshot of the order number at issuance — held separately so legacy code that
    /// reads <c>Invoice.OrderNumber</c> doesn't have to traverse the navigation.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    // ── Customer snapshot (populated by IInvoiceService; null when written by legacy flow) ──
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }
    public string? CustomerGstin { get; set; }
    /// <summary>"B2C" or "B2B" — drives GST classification on the PDF.</summary>
    public string GstClassification { get; set; } = "B2C";

    /// <summary>True when the supply is under reverse charge (RCM) — printed on the invoice.</summary>
    public bool ReverseCharge { get; set; }

    /// <summary>Headline GST rate applied (e.g. 18). Display-only; the split lives in the amount fields.</summary>
    public decimal GstRate { get; set; }

    // ── Money snapshot ──
    /// <summary>Subtotal before tax/discount. Newly snapshotted by IInvoiceService.</summary>
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    /// <summary>Pre-existing legacy column — kept for compatibility with
    /// <c>OrderAdminService.GetOrCreateInvoiceAsync</c>. New code sets this equal to Subtotal
    /// (pre-tax total). Old code sets it to <c>TotalAmount − GstAmount</c>.</summary>
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal ShippingCharges { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>For franchise-raised invoices: the franchisee's share deducted from the gross
    /// (shown as a "Franchise Share" line). Zero for normal customer invoices. When &gt; 0,
    /// <see cref="TotalAmount"/> is the NET the franchisee pays (gross − share).</summary>
    public decimal FranchiseShareAmount { get; set; }
    public string Currency { get; set; } = "INR";

    // ── Payment context ──
    public PaymentMode? PaymentMode { get; set; }

    /// <summary>Snapshot of the gateway-reported instrument at issuance — "UPI", "Credit Card",
    /// "Net Banking", … Frozen with the rest of the invoice; see
    /// <see cref="Order.GatewayPaymentMode"/>. Null for offline orders.</summary>
    public string? GatewayPaymentMode { get; set; }

    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }

    // ── Lifecycle ──
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Active;
    public DateTime? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }

    /// <summary>Backward-compat alias over <see cref="Status"/>. The legacy entity exposed an
    /// <c>IsCancelled</c> bool; new code uses the enum. NOT mapped — DB stores Status only.</summary>
    [NotMapped]
    public bool IsCancelled
    {
        get => Status == InvoiceStatus.Cancelled;
        set { if (value) Status = InvoiceStatus.Cancelled; else if (Status == InvoiceStatus.Cancelled) Status = InvoiceStatus.Active; }
    }

    /// <summary>Pre-existing optional column from the original minimal Invoice entity.
    /// <see cref="NotMapped"/> because no current code reads or writes a PDF URL — PDFs are
    /// rendered on-demand by the InvoiceService. Kept on the entity so any legacy code that
    /// might still set this property compiles, but EF will NOT include it in queries — so
    /// databases that never had the column work fine.</summary>
    [NotMapped]
    public string? PdfUrl { get; set; }

    public Guid? GeneratedByUserId { get; set; }
    public string? GeneratedByName { get; set; }
    public string? Notes { get; set; }

    // ── Company snapshot — frozen at issuance (populated by IInvoiceService; null on legacy flow) ──
    public string? CompanyName { get; set; }
    public string? CompanyGstin { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyEmail { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}
