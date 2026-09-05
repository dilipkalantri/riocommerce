using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Invoices;

public class InvoiceListItem
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public InvoiceStatus Status { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    /// <summary>Gateway-reported instrument snapshotted on the invoice — "UPI", "Credit Card", …</summary>
    public string? GatewayPaymentMode { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InvoiceDetail : InvoiceListItem
{
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }
    public string? CustomerGstin { get; set; }
    public string GstClassification { get; set; } = "B2C";
    public bool ReverseCharge { get; set; }
    public decimal GstRatePct { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal ShippingCharges { get; set; }
    public decimal FranchiseShareAmount { get; set; }
    /// <summary>For franchise invoices: full per-product franchisee/company bifurcation, rendered
    /// as a breakdown section on the invoice PDF. Null for normal customer invoices.</summary>
    public RioCommerce.Core.DTOs.Orders.FranchiseBifurcation? Bifurcation { get; set; }

    public string? PaymentReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledReason { get; set; }
    public string? GeneratedByName { get; set; }
    public string? Notes { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyGstin { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhone { get; set; }
    public string? CompanyEmail { get; set; }

    // ── Remittance + statutory identity, printed in the invoice footer.
    //    These are NOT snapshotted onto the Invoice row like the fields above: they are
    //    standing company facts (where to pay us, our PAN), not a record of what was
    //    transacted. They are stamped from current settings when the PDF is rendered, so
    //    a bank change is reflected on a reprint instead of reprinting a closed account.
    //    Nothing here affects any amount.
    public string? CompanyPan { get; set; }
    public string? CompanyBankName { get; set; }
    public string? CompanyBankAccountName { get; set; }
    public string? CompanyBankAccountNumber { get; set; }
    public string? CompanyBankIfsc { get; set; }
    public string? CompanyBankBranch { get; set; }

    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
}

public class InvoiceLineItemDto
{
    public int LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ModeName { get; set; }
    public string? HsnCode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal? GstRate { get; set; }
    public decimal GstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class InvoiceListFilter
{
    public InvoiceStatus? Status { get; set; }
    /// <summary>Free-text match on InvoiceNumber, OrderNumber, CustomerName.</summary>
    public string? Query { get; set; }
    public Guid? OrderId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>Strongly-typed bag of company info read from AppSettings — used by both the
/// invoice service (to snapshot at generation time) and the PDF generator.</summary>
public class CompanyProfile
{
    public string Name { get; set; } = "RioCommerce";
    public string? Gstin { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? BankBranch { get; set; }
    /// <summary>Company PAN, printed on the invoice footer.</summary>
    public string? Pan { get; set; }

    /// <summary>
    /// Invoice number series / prefix, e.g. "VP/26-27/". The numeric part is appended and
    /// auto-increments per series — VP/26-27/1, VP/26-27/2, … An admin sets only this prefix;
    /// the full number is never entered by hand. Changing it starts a fresh sequence at 1 under
    /// the new prefix and leaves every already-issued number untouched.
    /// </summary>
    public string? InvoiceSeries { get; set; }

    /// <summary>
    /// Order number series / prefix, e.g. "VJP-" producing VJP-01, VJP-02 … Blank keeps the
    /// original RIO- numbering. Like the invoice series, changing it starts a fresh sequence and
    /// leaves every already-issued order number untouched.
    /// </summary>
    public string? OrderSeries { get; set; }
}
