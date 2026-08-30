using RioCommerce.Core.DTOs.Attributes;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Checkout;

/// <summary>One line in the checkout review (mirrors a cart line).</summary>
public record CheckoutItemLine(
    Guid ProductId, string Slug, string Title, string? FacultyName, string? ModeName,
    int Quantity, decimal UnitPrice, decimal Mrp, string? Attributes = null,
    // Live product BatchStatus — drives the badge under each course title on the Checkout review.
    BatchStatus BatchStatus = BatchStatus.Upcoming,
    // ── 📦 Estimated Delivery Information snapshot on the checkout line ──
    string LectureAccessTiming = "Within 24 Hours",
    string NotesDispatchTimeline = "Within 48 Hours")
{
    /// <summary>Display label — mirrors <c>ProductListItem.BatchStatusText</c>.</summary>
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

/// <summary>A checkout attribute definition presented to the buyer at checkout.</summary>
public record CheckoutAttributeView(
    Guid Id, string Name, string? TextPrompt, string ControlType, bool IsRequired,
    List<CheckoutAttributeValueView> Values);
public record CheckoutAttributeValueView(
    Guid Id, string Name, decimal PriceAdjustment, bool PriceAdjustmentUsePercentage, bool IsPreSelected);

/// <summary>Billing details captured at checkout (prefilled from the user where possible).</summary>
public class BillingInfo
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string State { get; set; } = "Maharashtra";
    public string? Pincode { get; set; }
    /// <summary>Optional GSTIN — when present the order is treated as B2B.</summary>
    public string? GstNumber { get; set; }
}

/// <summary>Server-computed preview of what the user is about to pay.</summary>
public class CheckoutSummary
{
    public List<CheckoutItemLine> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal GstIncluded { get; set; }
    public decimal Total { get; set; }
    public decimal Savings { get; set; }
    public string? CouponCode { get; set; }
    public string? CouponMessage { get; set; }
    public int ItemCount => Items.Sum(i => i.Quantity);
    public BillingInfo Billing { get; set; } = new();
    /// <summary>Active checkout attribute definitions to render at checkout.</summary>
    public List<CheckoutAttributeView> CheckoutAttributes { get; set; } = new();
    /// <summary>Price impact of the currently-selected checkout attributes (already folded into Total/GstIncluded).</summary>
    public decimal CheckoutAttributesAmount { get; set; }
}

/// <summary>Posted when the user places the order.</summary>
public class CheckoutRequest
{
    public BillingInfo Billing { get; set; } = new();
    public string? CouponCode { get; set; }
    public PaymentMode PaymentMode { get; set; } = PaymentMode.Razorpay;
    /// <summary>true = online gateway; false = pay at center (order stays Pending).</summary>
    public bool PayOnline { get; set; } = true;
    /// <summary>Referral code from the <c>rc_ref</c> cookie, attributes the order to an affiliate.</summary>
    public string? AffiliateCode { get; set; }
    /// <summary>Selected checkout attributes (resolved + priced server-side).</summary>
    public List<CheckoutAttributeSelection> CheckoutSelections { get; set; } = new();
    /// <summary>"Referred By" option chosen on the checkout page (from /api/referrals). Null = not provided.</summary>
    public Guid? ReferralSourceId { get; set; }
    /// <summary>Free-text "please specify" — only used when the picked source's Type == Other; capped at 100 chars server-side.</summary>
    public string? ReferralCustomText { get; set; }
    /// <summary>Base URL for the customer's browser session — used by redirect-style gateways
    /// (Easebuzz) to build the surl/furl callback. Set by the API controller from
    /// HttpContext.Request, never trusted from the client.</summary>
    public string? OriginBaseUrl { get; set; }
}

/// <summary>Returned after placing the order — carries the gateway handoff details.
/// <para>Popup gateways (Razorpay) populate <c>GatewayKey</c> + <c>GatewayOrderId</c>.
/// Redirect gateways (Easebuzz) populate <c>RedirectUrl</c> — the JS just navigates to it.</para></summary>
public record PlaceOrderResult(
    Guid OrderId, string OrderNumber, decimal Amount,
    string? GatewayName, string? GatewayOrderId, string? GatewayKey, bool RequiresPayment,
    string? RedirectUrl = null);

/// <summary>Gateway callback (success/failure) posted back after the payment attempt.
/// <para>NOTE: this type is model-bound straight from a request body by
/// <c>POST /api/checkout/confirm</c>, so every field on it is CLIENT-CONTROLLED and only safe
/// because nothing is acted on until the gateway signature verifies. Never add a field here that
/// is written to an order/invoice without verification — pass such values as explicit service
/// parameters instead (see <c>ConfirmPaymentAsync</c>'s <c>instrument</c>).</para></summary>
public class PaymentCallback
{
    public string OrderNumber { get; set; } = string.Empty;
    public bool Success { get; set; } = true;
    public string? GatewayPaymentId { get; set; }
    public string? GatewayOrderId { get; set; }
    public string? GatewaySignature { get; set; }
}

public record OrderReceiptLine(string Title, string? ModeName, int Quantity, decimal UnitPrice, decimal LineTotal, string? Attributes = null);

/// <summary>Everything the confirmation / receipt page needs.</summary>
public class OrderReceipt
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string? StudentEmail { get; set; }
    public string StudentPhone { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    /// <summary>Gateway-reported instrument — "UPI", "Credit Card", … Null when unknown/offline.</summary>
    public string? GatewayPaymentMode { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal Total { get; set; }
    public string? CouponCode { get; set; }
    public string? InvoiceNumber { get; set; }
    /// <summary>True when this order was placed by a franchisee. Per policy the customer/student
    /// gets only a RECEIPT for these — the tax invoice belongs to the franchisee.</summary>
    public bool IsFranchiseOrder { get; set; }
    public string? BillingName { get; set; }
    public string? BillingAddress { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPincode { get; set; }
    public string? GstNumber { get; set; }
    public decimal CheckoutAttributesAmount { get; set; }
    public string? CheckoutAttributes { get; set; }
    public List<OrderReceiptLine> Items { get; set; } = new();

    /// <summary>Despatch details, or null when nothing has shipped for this order yet.</summary>
    public OrderShipmentInfo? Shipment { get; set; }
}

/// <summary>
/// What the customer is told about their consignment.
///
/// <para><see cref="TrackingNumber"/> and <see cref="TrackingUrl"/> are both null for a courier that
/// does not track (PCMC same-day), so the UI can hide the whole tracking block on a single null check
/// rather than testing for a courier by name.</para>
/// </summary>
public class OrderShipmentInfo
{
    public string Courier { get; set; } = string.Empty;
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? DispatchedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    /// <summary>True when there is something for the customer to track — drives both the
    /// "Tracking Number" row and the Track Shipment button.</summary>
    public bool HasTracking => !string.IsNullOrWhiteSpace(TrackingUrl) && !string.IsNullOrWhiteSpace(TrackingNumber);
}
