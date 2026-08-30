using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Franchise;

public record FranchisePortalDashboard(
    string Name, string Code, string City,
    decimal Wallet, decimal CreditLimit, decimal Available,
    int TotalOrders, int PendingOrders, int OrdersMtd, decimal RevenueMtd);

public record FranchiseCatalogItem(
    Guid ProductId, string Title, string Slug, CourseLevel Level, string? FacultyName,
    decimal Mrp, decimal RetailPrice, decimal FranchisePrice,
    // Lecture modes + configurable purchase-option groups the franchisee can pick while ordering.
    // Each mode Price and each option PriceAddOn is ADDED to FranchisePrice (same model as the
    // public checkout / admin order-create). Empty lists when the product has none configured.
    IReadOnlyList<FranchiseCatalogMode> Modes,
    IReadOnlyList<FranchiseCatalogOptionGroup> OptionGroups,
    // Product code, so the franchisee can find a course by SKU as well as by name.
    string? Sku = null);

/// <summary>A selectable lecture mode on a franchise-catalog product. <see cref="Price"/> is the
/// add-on to the base franchise price (0 = no extra).</summary>
public record FranchiseCatalogMode(Guid Id, string Name, decimal Price);

/// <summary>A purchase-option group (e.g. "Additional Test Series"). The franchisee picks exactly
/// one option; the chosen option's add-on stacks onto the base price.</summary>
public record FranchiseCatalogOptionGroup(Guid Id, string Name, IReadOnlyList<FranchiseCatalogOption> Options);

/// <summary>A single option within a group. <see cref="PriceAddOn"/> is added when selected.</summary>
public record FranchiseCatalogOption(Guid Id, string Name, decimal PriceAddOn);

// Captured by the franchisee on /franchise/order. Handles both Individual and Organization customers,
// quantity/discount/shipping, and an optional separate shipping address.
public class FranchiseOrderRequest
{
    // ── Order line items ──────────────────────────────────────────────────────
    // A franchise order can contain multiple products. New clients populate Lines.
    // The single-product fields below remain for backwards compatibility: if Lines is
    // empty but ProductId is set, the service treats it as a one-line order.
    public List<FranchiseOrderLine> Lines { get; set; } = new();

    // ── Legacy single-product fields (back-compat) ────────────────────────────
    public Guid ProductId { get; set; }
    public string? ModeName { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal Discount { get; set; }

    public decimal ShippingCharges { get; set; }
    public string? CustomerNotes { get; set; }

    /// <summary>Optional discount code the franchisee typed on /franchise/order. Only coupons flagged
    /// <c>IsFranchiseApplicable</c> are accepted, and the discount is always re-resolved server-side —
    /// the client never sends an amount. It reduces the order total <b>on top of</b> the franchisee's
    /// share, which is calculated from product pricing and is never touched by a coupon.</summary>
    public string? CouponCode { get; set; }

    /// <summary>How the franchisee pays for this order: from their wallet (default) or via whichever
    /// online payment gateway the admin has enabled. When Gateway, the order is created Pending and is
    /// only confirmed after the payment is verified server-side; the wallet is not debited.</summary>
    public FranchisePaymentMethod PaymentMethod { get; set; } = FranchisePaymentMethod.Wallet;

    /// <summary>Scheme + host of the page the franchisee is on, e.g. "https://www.riocommerce.com".
    /// Redirect gateways need an ABSOLUTE return URL — Easebuzz rejects a relative one — and the
    /// server cannot infer it from a Blazor circuit, so the component supplies it.
    /// Ignored by popup gateways.</summary>
    public string? OriginBaseUrl { get; set; }

    /// <summary>Normalises to the effective set of lines: <see cref="Lines"/> when present,
    /// otherwise a single line built from the legacy ProductId/Quantity/Discount fields.</summary>
    public IReadOnlyList<FranchiseOrderLine> EffectiveLines =>
        Lines is { Count: > 0 }
            ? Lines
            : (ProductId != Guid.Empty
                ? new List<FranchiseOrderLine> { new() { ProductId = ProductId, ModeName = ModeName, Quantity = Quantity, Discount = Discount } }
                : new List<FranchiseOrderLine>());

    public CustomerType CustomerType { get; set; } = CustomerType.Individual;

    // Primary contact: for Organization this is the contact-person name; for Individual it's the customer.
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? OrgName { get; set; }            // required when CustomerType=Organization
    public string? Gstin { get; set; }              // optional, typically for Organization

    // Billing address (mandatory for Org, optional for Individual).
    public string? AddressLine { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? PinCode { get; set; }

    // If false, the four Shipping* fields are persisted as a distinct shipping address.
    public bool ShipToBilling { get; set; } = true;
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingState { get; set; }
    public string? ShippingPinCode { get; set; }
}

/// <summary>One product line on a franchise order. Discount is a flat ₹ amount applied to this line.</summary>
public class FranchiseOrderLine
{
    public Guid ProductId { get; set; }
    public string? ModeName { get; set; }
    /// <summary>Chosen lecture mode (ProductMode). Its price is added to the base price server-side.</summary>
    public Guid? ProductModeId { get; set; }
    /// <summary>Chosen purchase-option item ids — at most one per option group. The server re-resolves
    /// each against the product (tamper-safe) and adds its PriceAddOn.</summary>
    public List<Guid> SelectedOptionIds { get; set; } = new();
    public int Quantity { get; set; } = 1;
    public decimal Discount { get; set; }
}

/// <summary>Result of checking a discount code on /franchise/order, before the order is placed.
/// Returns the coupon's <b>rules</b> rather than a fixed rupee amount so the page can keep the
/// discount in step as courses and quantities change — the server re-resolves the same rules when
/// the order is actually placed, so a tampered client can never dictate the discount.</summary>
/// <param name="Ok">True when the code is valid for this franchise right now.</param>
/// <param name="Code">The canonical (upper-case) code, when valid.</param>
/// <param name="CouponType">Percentage or fixed rupee amount.</param>
/// <param name="Value">Percent, or rupees, depending on <paramref name="CouponType"/>.</param>
/// <param name="MaxDiscount">Cap in rupees on a percentage discount (null = uncapped).</param>
/// <param name="MinOrder">Order value the discount needs to stay valid.</param>
/// <param name="Message">Franchisee-facing confirmation or the reason it was rejected.</param>
public record FranchiseCouponPreview(
    bool Ok, string? Code, SharingType CouponType, decimal Value,
    decimal? MaxDiscount, decimal MinOrder, string Message)
{
    public static FranchiseCouponPreview Rejected(string message)
        => new(false, null, SharingType.FixedAmount, 0m, null, 0m, message);
}

/// <summary>How a franchisee pays for an order.</summary>
public enum FranchisePaymentMethod
{
    /// <summary>Debit the franchise wallet/credit immediately (order is confirmed at once).</summary>
    Wallet = 0,
    /// <summary>Pay online via the gateway (Razorpay). Order is Pending until the payment is verified.</summary>
    Gateway = 1
}

/// <summary>
/// Everything the browser needs to hand the franchisee over to whichever gateway the admin has
/// enabled. Gateway-agnostic on purpose: the two live gateways hand over in different ways.
///
/// <para><b>Popup gateways (Razorpay):</b> <see cref="RedirectUrl"/> is null and the browser opens a
/// popup using <see cref="GatewayKey"/> + <see cref="GatewayOrderId"/>, then posts the signed result
/// back to the confirm endpoint.</para>
///
/// <para><b>Redirect gateways (Easebuzz):</b> <see cref="RedirectUrl"/> is set and the browser simply
/// navigates there. There is no signature to post back — the gateway POSTs its own verified callback
/// to the server, which settles the order.</para>
///
/// <para>Check <see cref="RedirectUrl"/> to decide which, not the gateway name: a future redirect
/// gateway then needs no change here or in the UI.</para>
/// </summary>
public record FranchiseGatewayHandoff(
    string OrderNumber,        // our local order number (FRN-… / RIO-…)
    string GatewayName,        // "Razorpay" | "Easebuzz" — informational / logging
    string? GatewayOrderId,    // popup gateways: razorpay order_xxx
    string? GatewayKey,        // popup gateways: public key id
    long AmountPaise,          // amount in paise for the popup
    string Currency = "INR",
    string? RedirectUrl = null); // redirect gateways: navigate the browser here

/// <summary>One wallet movement. <paramref name="OrderId"/> is set when the row came from an order
/// (deduction or refund) so admin views can link straight to it; the franchisee-facing page only
/// needs the number.</summary>
public record FranchiseLedgerItem(
    Guid Id, bool IsCredit, decimal Amount, decimal BalanceAfter, string? Description, string? OrderNumber, DateTime CreatedAt,
    Guid? OrderId = null);
