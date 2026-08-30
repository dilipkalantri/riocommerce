namespace RioCommerce.Core.Interfaces;

/// <summary>A payment intent created at the gateway, handed to the client to complete payment.
/// <para>For popup gateways (Razorpay) <c>RedirectUrl</c> is null and the browser opens the popup
/// using <c>GatewayKey</c> + <c>GatewayOrderId</c>. For redirect gateways (Easebuzz)
/// <c>RedirectUrl</c> is the URL the browser should navigate to.</para></summary>
public record GatewayOrder(
    string GatewayName,
    string GatewayOrderId,
    string GatewayKey,
    decimal Amount,
    string? RedirectUrl = null);

/// <summary>Customer/order context that redirect-style gateways need at order-creation time.
/// Popup-style gateways (Razorpay) can ignore it.</summary>
public record GatewayCreateContext(
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string ProductInfo,
    string ReturnUrl);

/// <summary>
/// The instrument the customer ACTUALLY paid with, as reported by the gateway
/// (Razorpay <c>method</c> + <c>card.type</c>, Easebuzz <c>mode</c> + <c>card_type</c>).
///
/// <para><c>Mode</c> is the normalised display label — "UPI", "Credit Card", "Debit Card",
/// "Net Banking", "Wallet", "EMI", … — and is what shows on order details and invoices.
/// <c>Detail</c> is optional extra context (issuing bank, wallet name, masked VPA/card).
/// <c>RawMode</c> keeps the gateway's own token ("upi", "CC") for support and debugging.</para>
/// </summary>
public record GatewayPaymentInstrument(string Mode, string? Detail = null, string? RawMode = null);

/// <summary>Result of a server-to-gateway poll for the current state of a payment intent.
/// Used by the Payment Status Sync scheduled task to reconcile orders where the customer's
/// browser dropped between gateway success and our callback.</summary>
public enum GatewayPaymentState { Unknown, Pending, Success, Failed, Cancelled }
public record GatewayPaymentStatus(GatewayPaymentState State, string? GatewayPaymentId, decimal? AmountPaise, string? RawStatus, string? Notes,
    GatewayPaymentInstrument? Instrument = null);

/// <summary>Result of a gateway refund attempt.</summary>
public record GatewayRefundResult(bool Success, string? RefundId, string? Message);

/// <summary>
/// Abstraction over a payment provider. Implementations register themselves by <c>Name</c>
/// (e.g. "Razorpay", "Easebuzz") and are looked up via <see cref="IPaymentGatewayFactory"/>.
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    /// <summary>Creates a gateway-side order/intent for the given amount (INR).
    /// <paramref name="context"/> is optional and only used by redirect-style gateways
    /// that need customer/return-URL details up front.</summary>
    Task<GatewayOrder> CreateOrderAsync(string orderNumber, decimal amount, string currency = "INR", GatewayCreateContext? context = null);

    /// <summary>Verifies the signature/ids returned by the gateway after a payment attempt.</summary>
    bool VerifyPayment(string gatewayOrderId, string? gatewayPaymentId, string? signature);

    /// <summary>Issues a refund against a captured payment. Real providers call their refund API here.</summary>
    Task<GatewayRefundResult> RefundAsync(string? gatewayPaymentId, decimal amount);

    /// <summary>
    /// Resolves the instrument a completed payment was made with (UPI / Credit Card / Net Banking / …).
    ///
    /// <para>Best-effort and purely informational: callers MUST treat null — and a throw — as
    /// "unknown" and never fail or delay a settled payment because of it. Gateways that hand the
    /// instrument back in their own callback payload (Easebuzz) leave this default in place; the
    /// controller parses the form instead.</para>
    /// </summary>
    Task<GatewayPaymentInstrument?> GetPaymentInstrumentAsync(string? gatewayPaymentId)
        => Task.FromResult<GatewayPaymentInstrument?>(null);
}

/// <summary>
/// Resolves a registered <see cref="IPaymentGateway"/> by name. Used by CheckoutService to pick
/// the right gateway based on the customer's payment-method selection on the Review page.
/// </summary>
public interface IPaymentGatewayFactory
{
    /// <summary>Returns the gateway whose <see cref="IPaymentGateway.Name"/> matches, case-insensitive.</summary>
    IPaymentGateway Get(string name);

    /// <summary>Same as <see cref="Get"/> but returns null instead of throwing.</summary>
    IPaymentGateway? TryGet(string name);

    /// <summary>All registered gateways, in registration order. Useful for diagnostics.</summary>
    IEnumerable<IPaymentGateway> All { get; }
}
