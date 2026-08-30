using RioCommerce.Core.DTOs.Attributes;
using RioCommerce.Core.DTOs.Checkout;
namespace RioCommerce.Core.Interfaces;

public interface ICheckoutService
{
    /// <summary>Server-computed preview of the cart (totals, GST, coupon, checkout attributes, billing prefill).</summary>
    Task<CheckoutSummary> GetSummaryAsync(Guid userId, string? couponCode = null, string? affiliateCode = null,
        IReadOnlyList<CheckoutAttributeSelection>? checkoutSelections = null);

    /// <summary>Creates a Pending order from the cart, clears the cart, and (for online) opens a gateway intent.</summary>
    Task<PlaceOrderResult> PlaceOrderAsync(Guid userId, CheckoutRequest request);

    /// <summary>Verifies the gateway callback; on success confirms the order, raises the invoice and enrollments.
    /// <para><paramref name="instrument"/> is the gateway-reported payment mode. It is a separate
    /// parameter rather than a field on <see cref="PaymentCallback"/> precisely because that type is
    /// model-bound from a request body — only trusted, server-side callers may supply it.</para></summary>
    Task<OrderReceipt?> ConfirmPaymentAsync(PaymentCallback callback, GatewayPaymentInstrument? instrument = null);

    /// <summary>Loads a receipt for the given user's order (Pending or Confirmed).</summary>
    Task<OrderReceipt?> GetReceiptAsync(Guid userId, string orderNumber);

    /// <summary>Re-issues a fresh gateway order against an existing Pending order so the
    /// customer can re-open the popup after a failed/cancelled attempt. Returns null if
    /// the order isn't theirs, doesn't exist, or is already Confirmed.</summary>
    /// <param name="originBaseUrl">Scheme+host of the request, e.g. "https://shop.example.com".
    /// Redirect gateways need an ABSOLUTE callback URL — Easebuzz rejects a relative surl/furl.
    /// Optional: popup gateways (Razorpay) ignore it.</param>
    Task<PlaceOrderResult?> RetryPaymentAsync(Guid userId, string orderNumber, string? originBaseUrl = null);
    /// <summary>Owner-agnostic gateway initiation for admin/franchise-created orders. Role-gate at the controller.</summary>
    /// <param name="originBaseUrl">See <see cref="RetryPaymentAsync"/> — absolute callback origin
    /// for redirect gateways; ignored by popup gateways.</param>
    Task<PlaceOrderResult?> InitiateGatewayForOrderAsync(string orderNumber, string? originBaseUrl = null);

    /// <summary>Reconciles a payment from a verified Razorpay webhook (X-Razorpay-Signature
    /// already checked upstream with the WebhookSecret). Idempotent: if the order is
    /// already Success this just returns the receipt. Returns null if no local order
    /// matches the given gateway order id.
    /// <para><paramref name="instrument"/> is the gateway-reported payment mode when the caller
    /// already has it from the payload; null asks the service to resolve it.</para></summary>
    Task<OrderReceipt?> ConfirmFromWebhookAsync(string gatewayOrderId, string? gatewayPaymentId,
        GatewayPaymentInstrument? instrument = null);

    /// <summary>Reconciles a payment looked up by our local <c>OrderNumber</c> — used by the
    /// Easebuzz callback path (which already verified the SHA-512 hash with the merchant
    /// Salt). Idempotent. Returns null if the order doesn't exist.
    /// <para><paramref name="instrument"/> is the gateway-reported payment mode when the caller
    /// already has it from the callback form; null asks the service to resolve it.</para></summary>
    Task<OrderReceipt?> ConfirmByOrderNumberAsync(string orderNumber, string? gatewayPaymentId, string source,
        GatewayPaymentInstrument? instrument = null);
}
