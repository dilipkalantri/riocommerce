using RioCommerce.Core.DTOs.Franchise;
namespace RioCommerce.Core.Interfaces;

public interface IFranchisePortalService
{
    /// <summary>The active franchise owned by this user (Franchise.AdminUserId), or null.</summary>
    Task<Guid?> ResolveFranchiseIdAsync(Guid userId);
    Task<FranchisePortalDashboard?> GetDashboardAsync(Guid franchiseId);
    Task<List<FranchiseCatalogItem>> CatalogAsync();
    /// <summary>
    /// The franchisee-facing product listing: name, regular/effective price, special-price flag,
    /// their resolved share (type, value, source) and the calculated per-unit earning. Override
    /// priority and special-price recalculation are handled by IFranchiseShareCalculator, so this
    /// always reflects the live state of admin Bulk Assign changes and the current date.
    /// </summary>
    Task<List<FranchiseProductShareRow>> ProductSharesAsync(Guid franchiseId, bool onlyWithShare = false);
    /// <summary>Checks a discount code for this franchise and returns its rules so the order page can
    /// price it live. Only coupons flagged <c>IsFranchiseApplicable</c> qualify. The discount is
    /// re-resolved from scratch when the order is placed — this is a preview, never the source of
    /// truth. The franchisee's share is unaffected: a coupon reduces the total on top of it.</summary>
    Task<FranchiseCouponPreview> PreviewCouponAsync(Guid franchiseId, string? code);
    /// <summary>Places a franchise order at franchise pricing, debiting the wallet (within credit limit).</summary>
    /// <summary>True when the given order belongs to this franchise (ownership guard for downloads).</summary>
    Task<bool> OwnsOrderAsync(Guid franchiseId, Guid orderId);
    Task<(bool ok, string? error, string? orderNumber)> PlaceOrderAsync(Guid franchiseId, FranchiseOrderRequest request);
    /// <summary>
    /// Creates a Pending franchise order and a payment intent on WHICHEVER gateway the admin has
    /// enabled. The handoff tells the browser how to proceed: navigate to
    /// <c>RedirectUrl</c> when set (Easebuzz), otherwise open the popup and call
    /// <see cref="ConfirmGatewayOrderAsync"/> with the signed result (Razorpay).
    /// Fails with a clear message when no gateway is enabled — it never picks a default.
    /// </summary>
    Task<(bool ok, string? error, FranchiseGatewayHandoff? handoff)> CreateGatewayOrderAsync(Guid franchiseId, FranchiseOrderRequest request);

    /// <summary>Verifies a POPUP gateway's signed result and finalises the order (no wallet debit).
    /// The gateway is resolved from the mode recorded on the order, so an order created on one gateway
    /// still verifies on it after the admin switches.</summary>
    Task<(bool ok, string? error, string? orderNumber)> ConfirmGatewayOrderAsync(
        Guid franchiseId, string orderNumber, string gatewayOrderId, string gatewayPaymentId, string signature);

    /// <summary>
    /// Settles a franchise order from a REDIRECT gateway's server-to-server callback (Easebuzz).
    /// The caller must already have verified the gateway's hash. Validates the paid amount against
    /// FranchiseNetPayable and finalises through the franchise path — franchise commission and the
    /// wallet-vs-invoice rule live there and the customer checkout path does not implement them.
    /// Idempotent.
    /// </summary>
    Task<(bool ok, string? error, string? orderNumber)> SettleGatewayCallbackAsync(
        string orderNumber, string? gatewayPaymentId, decimal? paidAmount, GatewayPaymentInstrument? instrument);

    /// <summary>True when this order number belongs to a franchise order. Lets a shared gateway
    /// callback pick the right settlement path without knowing franchise internals.</summary>
    Task<bool> IsFranchiseOrderAsync(string orderNumber);
    Task<List<FranchiseOrderRow>> MyOrdersAsync(Guid franchiseId);
    Task<List<FranchiseLedgerItem>> LedgerAsync(Guid franchiseId);
}
