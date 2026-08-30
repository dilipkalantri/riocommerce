using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Orders;

/// <summary>
/// The spec's order lifecycle as shown to users. Derived from the stored OrderStatus +
/// PaymentStatus by <see cref="OrderLifecycleMap.Resolve"/> so we don't have to migrate the
/// native DB enum.
/// </summary>
public enum OrderLifecycle
{
    Pending,
    Processing,
    PaymentPending,
    Paid,
    Completed,
    Cancelled,
    Refunded
}

public static class OrderLifecycleMap
{
    public static OrderLifecycle Resolve(OrderStatus status, PaymentStatus payment)
    {
        return status switch
        {
            OrderStatus.Cancelled => OrderLifecycle.Cancelled,
            OrderStatus.Refunded => OrderLifecycle.Refunded,
            OrderStatus.Delivered => OrderLifecycle.Completed,
            OrderStatus.Activated => OrderLifecycle.Completed,
            OrderStatus.Processing => OrderLifecycle.Processing,
            _ when payment == PaymentStatus.Success => OrderLifecycle.Paid,
            _ when payment == PaymentStatus.Pending && status == OrderStatus.Pending => OrderLifecycle.PaymentPending,
            OrderStatus.Confirmed => OrderLifecycle.Paid,
            _ => OrderLifecycle.Pending
        };
    }

    public static string Label(OrderLifecycle l) => l switch
    {
        OrderLifecycle.PaymentPending => "Payment Pending",
        _ => l.ToString()
    };
}

/// <summary>Per-line franchise bifurcation: how a line splits between the franchisee and the company.</summary>
public sealed record FranchiseBifurcationLine
{
    public string ProductTitle { get; init; } = "";
    public int Quantity { get; init; }
    public decimal LineAmount { get; init; }            // net line value (after line discount)
    public string ShareLabel { get; init; } = "—";      // e.g. "15%" or "₹375/unit"
    public bool SpecialPriceApplied { get; init; }       // special pricing influenced the share base
    public decimal FranchiseShare { get; init; }         // total franchisee payout on this line (commission + GST on it)
    public decimal CompanyShare { get; init; }           // LineAmount − FranchiseShare
    public decimal CommissionAmount { get; init; }       // bare commission, excl. GST on it
    public decimal GstOnCommission { get; init; }        // 0 when the franchisee has no GSTIN
}

/// <summary>Order-level franchise bifurcation, shown on receipts/invoices and the franchisee view.</summary>
public sealed record FranchiseBifurcation
{
    public bool IsFranchiseOrder { get; init; }
    public string? FranchiseName { get; init; }
    public string? FranchiseCode { get; init; }
    public List<FranchiseBifurcationLine> Lines { get; init; } = new();
    public decimal TotalFranchiseShare { get; init; }    // = order.FranchiseShareAmount (commission + GST on it)
    public decimal TotalCompanyShare { get; init; }      // = order total − franchise share
    public decimal NetPayableByFranchisee { get; init; } // = order.FranchiseNetPayable
    public decimal OrderTotal { get; init; }

    // ── Commission split (Franchisee Share Calculation Logic, Step 3) ────────────────────────
    /// <summary>True when the franchisee holds a GSTIN, so their commission is a taxable supply
    /// (Scenario A) and they invoice us for it. False → bill of supply, no tax (Scenario B).</summary>
    public bool FranchiseeIsGstRegistered { get; init; }
    /// <summary>Bare commission = <see cref="TotalFranchiseShare"/> − <see cref="TotalGstOnCommission"/>.</summary>
    public decimal TotalCommissionAmount { get; init; }
    /// <summary>GST on the commission — the input tax credit claimable once the franchisee files it.
    /// Zero for unregistered franchisees and for orders placed before the split was recorded.</summary>
    public decimal TotalGstOnCommission { get; init; }
    /// <summary>False for orders written before the commission split existed, whose share is a
    /// single undecomposed figure. Lets renderers hide the breakdown rather than print zeros.</summary>
    public bool HasCommissionSplit { get; init; }
}

/// <summary>Everything needed to render an order RECEIPT (available for all orders, any status).</summary>
public sealed record ReceiptDetail
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    /// <summary>Display-only receipt reference, derived deterministically from the order number.
    /// Receipts are not separately numbered in the DB (unlike invoices, which have their own series).</summary>
    public string? ReceiptNumber { get; init; }
    public DateTime OrderDateUtc { get; init; }

    public string CustomerName { get; init; } = "";
    public string? CustomerPhone { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerCity { get; init; }
    // Billing address snapshot. Rendered as a block; the renderer falls back to CustomerCity
    // when the order carries no separate billing city.
    public string? CustomerAddress { get; init; }
    public string? CustomerBillingCity { get; init; }
    public string? CustomerState { get; init; }
    public string? CustomerPincode { get; init; }

    public string PaymentStatus { get; init; } = "";
    public string OrderStatus { get; init; } = "";       // lifecycle label
    public string? PaymentMode { get; init; }
    /// <summary>Gateway-reported instrument — "UPI", "Credit Card", … Null when unknown/offline.</summary>
    public string? GatewayPaymentMode { get; init; }
    public DateTime? PaidAtUtc { get; init; }
    /// <summary>Gateway transaction reference for the successful capture, when one exists.</summary>
    public string? TransactionId { get; init; }
    /// <summary>"Referred By" snapshot taken at checkout; null when the order has no referral source.</summary>
    public string? ReferredBy { get; init; }

    public string GstClassification { get; init; } = "B2C";
    public bool ReverseCharge { get; init; }
    public string? CustomerGstin { get; init; }
    public bool IntraState { get; init; }                 // true → CGST+SGST, false → IGST

    public decimal Subtotal { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstRate { get; init; }
    public decimal CgstAmount { get; init; }
    public decimal SgstAmount { get; init; }
    public decimal IgstAmount { get; init; }
    public decimal GstAmount { get; init; }
    public decimal ShippingCharges { get; init; }
    public decimal TotalAmount { get; init; }

    /// <summary>Franchise order paid out of the wallet — no tax invoice exists for it, because the
    /// wallet top-up that funded it was invoiced instead.</summary>
    public bool PaidFromWallet { get; init; }

    public List<ReceiptLine> Lines { get; init; } = new();

    public FranchiseBifurcation? Franchise { get; init; }

    // Company snapshot
    public string CompanyName { get; init; } = "";
    public string? CompanyGstin { get; init; }
    public string? CompanyAddress { get; init; }
    public string? CompanyPhone { get; init; }
    public string? CompanyEmail { get; init; }
    public string? CompanyWebsite { get; init; }
}

public sealed record ReceiptLine
{
    public int LineNumber { get; init; }
    public string Description { get; init; } = "";
    public string? ModeName { get; init; }
    /// <summary>Product SKU snapshot, resolved from the catalogue at render time.</summary>
    public string? Sku { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Discount { get; init; }
    public decimal GstRate { get; init; }
    public decimal LineTotal { get; init; }
}
