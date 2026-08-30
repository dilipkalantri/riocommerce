using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// A tax invoice raised ON a franchisee against an invoice already issued to the end customer.
///
/// <para>The franchisee sells to the student under their own arrangement; the institute then
/// re-invoices the franchisee for the net-of-commission value of that same supply. This entity
/// records the second document and — crucially for the report — keeps a permanent link back to the
/// first, so an auditor can trace which original produced which re-invoice.</para>
///
/// <para>Every money and party field is a SNAPSHOT taken at issue, exactly like
/// <see cref="Invoice"/>. Once issued the row is immutable; a mistake is corrected by cancelling
/// and raising a new one, never by editing.</para>
/// </summary>
public class FranchiseReInvoice : BaseEntity
{
    /// <summary>Human-readable number — format <c>RIO-RINV-YYYYMM-NNNN</c>, sequence resets
    /// monthly. Unique-indexed. Deliberately distinct from the <c>RIO-INV-</c> customer series so
    /// the two documents can never be confused in a ledger.</summary>
    public string ReInvoiceNumber { get; set; } = string.Empty;

    public DateOnly ReInvoiceDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    // ── Link to the original customer invoice ──
    public Guid OriginalInvoiceId { get; set; }
    public Invoice? OriginalInvoice { get; set; }

    /// <summary>Snapshot of the original invoice's number, held here so the report can render the
    /// original/re-invoice pairing without joining — and so it survives even if the original is
    /// later cancelled.</summary>
    public string OriginalInvoiceNumber { get; set; } = string.Empty;
    public DateOnly OriginalInvoiceDate { get; set; }

    /// <summary>The order behind both documents. Snapshotted for the same reason.</summary>
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;

    // ── Franchisee being billed (snapshot) ──
    public Guid FranchiseId { get; set; }
    public Franchise? Franchise { get; set; }
    public string FranchiseName { get; set; } = string.Empty;
    public string? FranchiseCode { get; set; }
    public string? FranchiseGstin { get; set; }
    public string? PlaceOfSupply { get; set; }

    // ── Money snapshot ──
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }

    /// <summary>CGST + SGST + IGST. Stored rather than computed so the report totals cannot drift
    /// from the document.</summary>
    public decimal TotalGst { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>Headline rate applied (e.g. 18). Display-only; the split lives in the amounts.</summary>
    public decimal GstRate { get; set; }

    /// <summary>The commission the franchisee retained on the original sale, which is why the
    /// re-invoice value differs from the original. Shown on the report so the difference between
    /// the two documents is self-explaining.</summary>
    public decimal FranchiseShareAmount { get; set; }

    public string Currency { get; set; } = "INR";

    // ── Lifecycle ──
    public ReInvoiceStatus Status { get; set; } = ReInvoiceStatus.Issued;
    public DateTime? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }

    public Guid? IssuedById { get; set; }
    public string? IssuedByName { get; set; }
    public string? Notes { get; set; }

    public ICollection<FranchiseReInvoiceItem> Items { get; set; } = new List<FranchiseReInvoiceItem>();
}

/// <summary>One product line on a franchisee re-invoice. Carries its own subject snapshot so the
/// re-invoice report can be filtered by subject without joining back through the product, which
/// may have been re-categorised since.</summary>
public class FranchiseReInvoiceItem : BaseEntity
{
    public Guid ReInvoiceId { get; set; }
    public FranchiseReInvoice ReInvoice { get; set; } = null!;

    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;

    public Guid? SubjectId { get; set; }
    public string? SubjectName { get; set; }

    /// <summary>The originating order line, so a re-invoice line is traceable to the sale.</summary>
    public Guid? OrderItemId { get; set; }

    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }
}
