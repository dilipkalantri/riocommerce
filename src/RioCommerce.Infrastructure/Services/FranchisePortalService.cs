using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace RioCommerce.Infrastructure.Services;

public class FranchisePortalService : IFranchisePortalService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };
    private static DateTime MonthStart => new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly RioCommerceDbContext _db;
    private readonly IFranchiseService _franchise;
    private readonly INotificationService _notify;
    private readonly IFranchiseShareCalculator _shares;
    private readonly IInvoiceService _invoices;
    private readonly IPaymentGatewayFactory _gateways;
    private readonly ISerialKeyService _serialKeys;
    private readonly IPaymentModeRegistry _paymentModes;
    private readonly IStudentAccountProvisioner _students;
    private readonly IFacultySharingService _facultyShares;
    private readonly ILogger<FranchisePortalService> _log;
    public FranchisePortalService(RioCommerceDbContext db, IFranchiseService franchise, INotificationService notify, IFranchiseShareCalculator shares, IInvoiceService invoices, IPaymentGatewayFactory gateways, ISerialKeyService serialKeys, IPaymentModeRegistry paymentModes, IStudentAccountProvisioner students, IFacultySharingService facultyShares, ILogger<FranchisePortalService> log)
    {
        _db = db;
        _franchise = franchise;
        _notify = notify;
        _shares = shares;
        _invoices = invoices;
        _gateways = gateways;
        _serialKeys = serialKeys;
        _paymentModes = paymentModes;
        _students = students;
        _facultyShares = facultyShares;
        _log = log;
    }

    /// <summary>
    /// The customer account this order belongs to, resolved from the student's mobile number.
    ///
    /// <para>A franchise order is placed <i>for</i> a student who never signs in to place it, so unless
    /// something links the order to a real account the student has no login, no course access and no
    /// order history — everything downstream (enrollment, the customer dashboard, the LMS grant) keys
    /// off <c>Order.UserId</c>. An existing account is reused untouched; only a genuinely new mobile
    /// number gets a new one.</para>
    ///
    /// <para>Non-fatal by design: if provisioning fails the order still goes through with a null
    /// UserId and the failure is logged. A franchisee who has already paid must never lose the order
    /// because account creation had a bad day.</para>
    /// </summary>
    private async Task<Guid?> ResolveStudentUserIdAsync(string orderNumber, string name, string phone, string? email, string? city, string? state)
    {
        var result = await _students.ResolveOrCreateAsync(new StudentAccountRequest(
            FullName: name,
            Phone: phone,
            Email: email,
            City: city,
            State: state,
            SourceNote: $"Created from Franchise Order {orderNumber}"));

        if (!result.Linked)
            _log.LogError("Franchise order {OrderNumber} has no customer account ({Outcome}: {Note}). Student={Name} Phone={Phone} — the order is kept, but course access will not activate until it is linked.",
                orderNumber, result.Outcome, result.Note, name, phone);

        return result.UserId;
    }

    /// <summary>Links a settling order to its customer when it was created before it had one — the
    /// gateway paths can settle an order that was written by an older build, or whose provisioning
    /// failed at creation time. Idempotent: an order that already has a UserId is left alone.</summary>
    private async Task EnsureOrderUserAsync(Order order)
    {
        if (order.UserId.HasValue) return;
        order.UserId = await ResolveStudentUserIdAsync(
            order.OrderNumber, order.StudentName, order.StudentPhone, order.StudentEmail,
            order.StudentCity, order.ShippingState);
    }

    /// <summary>
    /// The gateway a NEW franchise payment starts on, from the admin settings. Fails loudly rather
    /// than substituting a default — a wrong guess here creates an order that can never be paid.
    /// </summary>
    private async Task<(IPaymentGateway? gateway, PaymentMode mode, string? error)> ResolveOnlineGatewayAsync()
    {
        var resolved = await _paymentModes.ResolveOnlineGatewayAsync();
        if (resolved == null)
        {
            _log.LogError("Franchise online payment requested but no payment gateway is enabled.");
            return (null, default, "Online payment is currently unavailable. Please contact administrator.");
        }

        var gateway = _gateways.TryGet(resolved.Name);
        if (gateway == null)
        {
            _log.LogError("Configured gateway {Gateway} is enabled but not registered.", resolved.Name);
            return (null, default, "The configured payment gateway is currently unavailable. Please contact administrator.");
        }

        return (gateway, resolved.Mode, null);
    }

    /// <summary>
    /// The gateway an EXISTING order must be verified against — taken from the mode persisted on the
    /// order, never from today's settings. An order started on Razorpay has to verify on Razorpay even
    /// after the admin switches to Easebuzz, or historical payments stop settling.
    /// </summary>
    private (IPaymentGateway? gateway, string? error) GatewayForOrder(Order order)
    {
        var mode = order.PaymentMode;
        if (mode == null) return (null, "This order has no payment gateway recorded.");

        var name = _paymentModes.GatewayNameFor(mode.Value);
        if (name == null) return (null, $"Order {order.OrderNumber} was not an online gateway payment ({mode}).");

        var gateway = _gateways.TryGet(name);
        return gateway == null
            ? (null, $"The {name} gateway is no longer registered, so this payment cannot be verified.")
            : (gateway, null);
    }

    public async Task<Guid?> ResolveFranchiseIdAsync(Guid userId)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.AdminUserId == userId && x.IsActive);
        return f?.Id;
    }

    public async Task<FranchisePortalDashboard?> GetDashboardAsync(Guid franchiseId)
    {
        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return null;
        var ms = MonthStart;
        // Wallet top-ups are orders too (so they can be invoiced) but they are money IN, not sales —
        // counting them here would double-count against the courses that money later buys.
        var orders = await _db.Orders.ExcludeWalletTopUps().Where(o => o.FranchiseId == franchiseId)
            .Select(o => new { o.TotalAmount, o.Status, o.CreatedAt }).ToListAsync();

        return new FranchisePortalDashboard(
            f.Name, f.Code, f.City, f.WalletBalance, f.CreditLimit, f.WalletBalance + f.CreditLimit,
            orders.Count,
            orders.Count(o => o.Status == OrderStatus.Pending),
            orders.Count(o => o.CreatedAt >= ms),
            orders.Where(o => o.CreatedAt >= ms && !NonRevenue.Contains(o.Status)).Sum(o => o.TotalAmount));
    }

    public async Task<List<FranchiseCatalogItem>> CatalogAsync()
    {
        var now = DateTime.UtcNow;
        return await _db.Products.Where(p => p.Status == ProductStatus.Active)
            .Include(p => p.PrimaryFaculty)
            .OrderBy(p => p.DisplayOrder)
            .Select(p => new FranchiseCatalogItem(
                p.Id, p.Title, p.Slug, p.Level, p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                p.Mrp, p.SellingPrice,
                // Franchisee is charged the effective selling price (special when active, else regular);
                // their earning is the commission, recorded separately at order time.
                (p.SpecialPrice != null && p.SpecialPrice > 0
                    && (p.SpecialPriceStartDateUtc == null || now >= p.SpecialPriceStartDateUtc)
                    && (p.SpecialPriceEndDateUtc == null || now <= p.SpecialPriceEndDateUtc))
                    ? p.SpecialPrice!.Value : p.SellingPrice,
                // Lecture modes + purchase-option groups so the franchisee can configure the line
                // exactly like a public checkout (add-ons stack onto FranchisePrice, priced server-side).
                p.Modes.Where(m => m.IsEnabled).OrderBy(m => m.DisplayOrder)
                    .Select(m => new FranchiseCatalogMode(m.Id, m.ModeName, m.Price)).ToList(),
                p.OptionGroups.Where(g => g.IsActive).OrderBy(g => g.SortOrder)
                    .Select(g => new FranchiseCatalogOptionGroup(g.Id, g.Name,
                        g.Items.Where(i => i.IsActive).OrderBy(i => i.SortOrder)
                            .Select(i => new FranchiseCatalogOption(i.Id, i.Name, i.PriceAddOn)).ToList()))
                    .ToList(),
                p.Sku))
            .ToListAsync();
    }

    public Task<bool> OwnsOrderAsync(Guid franchiseId, Guid orderId)
        => _db.Orders.IgnoreQueryFilters().AnyAsync(o => o.Id == orderId && o.FranchiseId == franchiseId);

    // ── Franchise discount codes ──────────────────────────────────────────────
    // A franchise coupon is an ORDER-LEVEL discount that stacks on top of the franchisee's share:
    // the share is derived from product pricing and commission rules and is never reduced by a
    // coupon, so the franchisee keeps their full earning AND pays less. Only coupons an admin has
    // flagged IsFranchiseApplicable qualify — customer-website codes are not redeemable here.

    public async Task<FranchiseCouponPreview> PreviewCouponAsync(Guid franchiseId, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return FranchiseCouponPreview.Rejected("Enter a discount code.");
        var (coupon, error) = await FindFranchiseCouponAsync(franchiseId, code);
        if (coupon == null) return FranchiseCouponPreview.Rejected(error!);

        var label = string.IsNullOrWhiteSpace(coupon.Name) ? coupon.Code : $"{coupon.Name} ({coupon.Code})";
        return new FranchiseCouponPreview(
            true, coupon.Code, coupon.CouponType, coupon.Value, coupon.MaxDiscount, coupon.MinOrder,
            $"Discount \"{label}\" applied — it comes off your order total on top of your share. 🎉");
    }

    /// <summary>Looks up a franchise-redeemable coupon and runs every eligibility rule that does not
    /// depend on the order value (audience, active window, usage limits). Returns the reason instead
    /// of the coupon when it can't be used.</summary>
    private async Task<(Coupon? coupon, string? error)> FindFranchiseCouponAsync(Guid franchiseId, string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == normalized && c.IsActive);
        if (coupon == null) return (null, "Invalid discount code.");
        if (!coupon.IsFranchiseApplicable)
            return (null, "This code isn't available for franchise orders.");

        var now = DateTime.UtcNow;
        if (coupon.StartsAt.HasValue && coupon.StartsAt > now)
            return (null, $"This code becomes active on {coupon.StartsAt.Value.ToLocalTime():dd MMM yyyy}.");
        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt < now)
            return (null, "This discount code has expired.");
        if (coupon.TotalLimit.HasValue && coupon.TotalUsed >= coupon.TotalLimit.Value)
            return (null, "This discount code has reached its usage limit.");

        // PerUserLimit is per FRANCHISE here — the franchise is the buying account. Only orders that
        // were actually PAID burn an allowance, matching when TotalUsed is incremented: an abandoned
        // online payment leaves a Pending order behind and must not cost the franchisee a redemption.
        if (coupon.PerUserLimit > 0)
        {
            var usedByFranchise = await _db.Orders.CountAsync(o =>
                o.FranchiseId == franchiseId && o.CouponId == coupon.Id
                && o.PaymentStatus == PaymentStatus.Success && !NonRevenue.Contains(o.Status));
            if (usedByFranchise >= coupon.PerUserLimit)
                return (null, coupon.PerUserLimit == 1
                    ? "You've already used this discount code."
                    : $"You've already used this discount code {coupon.PerUserLimit} times.");
        }

        return (coupon, null);
    }

    /// <summary>The rupee discount this coupon gives on <paramref name="baseAmount"/> (the order value
    /// after line discounts, before shipping). Mirrored client-side on /franchise/order so the summary
    /// stays live; this is the authoritative copy.</summary>
    private static decimal CouponDiscountOn(Coupon coupon, decimal baseAmount)
    {
        var discount = coupon.CouponType == SharingType.Percentage
            ? baseAmount * coupon.Value / 100m
            : coupon.Value;
        if (coupon.MaxDiscount.HasValue && discount > coupon.MaxDiscount.Value) discount = coupon.MaxDiscount.Value;
        return Math.Round(Math.Clamp(discount, 0m, baseAmount), 2);
    }

    public Task<List<FranchiseProductShareRow>> ProductSharesAsync(Guid franchiseId, bool onlyWithShare = false)
        => _shares.GetProductSharesAsync(franchiseId, onlyWithShare);

    public async Task<(bool ok, string? error, string? orderNumber)> PlaceOrderAsync(Guid franchiseId, FranchiseOrderRequest req)
    {
        var (ok, error, order, _) = await BuildOrderAsync(franchiseId, req);
        if (!ok || order == null) return (false, error, null);

        // Gateway orders are created Pending here and finalised in ConfirmGatewayOrderAsync.
        // This method only finalises wallet orders.
        if (req.PaymentMethod == FranchisePaymentMethod.Gateway)
            return (false, "Use CreateGatewayOrderAsync for online payments.", null);

        await FinalizeOrderAsync(order, debitWallet: true);
        return (true, null, order.OrderNumber);
    }

    public async Task<(bool ok, string? error, FranchiseGatewayHandoff? handoff)> CreateGatewayOrderAsync(Guid franchiseId, FranchiseOrderRequest req)
    {
        req.PaymentMethod = FranchisePaymentMethod.Gateway;

        // Resolve BEFORE the order is written. Nothing is worse than a saved Pending order with no
        // payable intent behind it — that is exactly how FRN-1005 got stranded.
        var (gateway, _, gwError) = await ResolveOnlineGatewayAsync();
        if (gateway == null) return (false, gwError, null);

        // Redirect gateways need customer details and an absolute return URL up front. Check that
        // here so a missing field is a clear message, not a gateway rejection after the order exists.
        var contextError = ValidateGatewayContext(req);
        if (contextError != null) return (false, contextError, null);

        var (ok, error, order, total) = await BuildOrderAsync(franchiseId, req);
        if (!ok || order == null) return (false, error, null);

        // Order is already saved Pending by BuildOrderAsync. The franchisee pays the NET
        // (total − their share), so the gateway intent is for FranchiseNetPayable.
        var payable = order.FranchiseNetPayable;

        GatewayOrder intent;
        try
        {
            intent = await gateway.CreateOrderAsync(order.OrderNumber, payable, "INR", BuildGatewayContext(req, order));
        }
        catch (Exception ex)
        {
            // The order stays Pending and retryable; it is NOT reported as started. The technical
            // detail goes to the log, never to the franchisee.
            _log.LogError(ex, "Franchise gateway intent failed OrderNumber={OrderNumber} Gateway={Gateway} Payable={Payable}",
                order.OrderNumber, gateway.Name, payable);
            return (false, $"Unable to initialize online payment. Order {order.OrderNumber} is saved as pending — "
                         + "you can retry payment from your orders list.", null);
        }

        return (true, null, new FranchiseGatewayHandoff(
            order.OrderNumber, intent.GatewayName, intent.GatewayOrderId, intent.GatewayKey,
            (long)Math.Round(payable * 100m), "INR", intent.RedirectUrl));
    }

    /// <summary>Fields a redirect gateway cannot do without. Returns null when the request is usable.</summary>
    private static string? ValidateGatewayContext(FranchiseOrderRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName))
            return "Unable to start online payment because the customer name is missing.";
        if (string.IsNullOrWhiteSpace(req.Phone))
            return "Unable to start online payment because the customer mobile number is missing.";
        if (string.IsNullOrWhiteSpace(req.OriginBaseUrl))
            return "Unable to start online payment — the return address could not be determined. Please reload the page and try again.";
        return null;
    }

    /// <summary>
    /// Customer + return-URL context for redirect gateways, built from the REAL order and request —
    /// never placeholder data. Popup gateways ignore it.
    /// </summary>
    private static GatewayCreateContext BuildGatewayContext(FranchiseOrderRequest req, Order order)
    {
        var productInfo = string.Join(", ", order.Items
            .Select(i => i.ProductTitle)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Take(3));
        if (string.IsNullOrWhiteSpace(productInfo)) productInfo = $"Order {order.OrderNumber}";

        // Absolute, and the same callback the storefront uses — the gateway POSTs its verified result
        // there and the server settles from it. Validated as present by ValidateGatewayContext.
        var origin = req.OriginBaseUrl!.Trim().TrimEnd('/');

        return new GatewayCreateContext(
            CustomerName: req.CustomerName.Trim(),
            CustomerEmail: (req.Email ?? string.Empty).Trim(),
            CustomerPhone: req.Phone.Trim(),
            ProductInfo: productInfo,
            ReturnUrl: $"{origin}/api/payments/easebuzz/callback");
    }

    public async Task<(bool ok, string? error, string? orderNumber)> ConfirmGatewayOrderAsync(
        Guid franchiseId, string orderNumber, string gatewayOrderId, string gatewayPaymentId, string signature)
    {
        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.FranchiseId == franchiseId);
        if (order == null) return (false, "Order not found.", null);
        if (order.PaymentStatus == PaymentStatus.Success) return (true, null, order.OrderNumber); // idempotent

        // Verify against the gateway this order was CREATED on, not whatever is enabled today.
        var (gateway, gwError) = GatewayForOrder(order);
        if (gateway == null)
        {
            _log.LogError("Franchise confirm cannot resolve gateway OrderNumber={OrderNumber} Mode={Mode}",
                order.OrderNumber, order.PaymentMode);
            return (false, gwError ?? "Payment verification failed.", null);
        }

        if (!gateway.VerifyPayment(gatewayOrderId, gatewayPaymentId, signature))
            return (false, "Payment verification failed.", null);

        order.Status = OrderStatus.Confirmed;
        order.PaymentStatus = PaymentStatus.Success;
        order.ConfirmedAt = DateTime.UtcNow;

        // What the franchisee actually paid with (UPI / Credit Card / …). Best-effort and set before
        // FinalizeOrderAsync so the invoice it raises snapshots the same value; never fails the
        // settlement — an unknown mode is fine, a rejected verified payment is not.
        try
        {
            var instrument = await gateway.GetPaymentInstrumentAsync(gatewayPaymentId);
            if (instrument != null)
                order.GatewayPaymentMode = instrument.Mode.Length <= 40 ? instrument.Mode : instrument.Mode[..40];
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Franchise order payment mode lookup failed OrderNumber={OrderNumber}", order.OrderNumber);
        }

        await FinalizeOrderAsync(order, debitWallet: false);
        return (true, null, order.OrderNumber);
    }

    public Task<bool> IsFranchiseOrderAsync(string orderNumber) =>
        string.IsNullOrWhiteSpace(orderNumber)
            ? Task.FromResult(false)
            : _db.Orders.AnyAsync(o => o.OrderNumber == orderNumber && o.FranchiseId != null);

    public async Task<(bool ok, string? error, string? orderNumber)> SettleGatewayCallbackAsync(
        string orderNumber, string? gatewayPaymentId, decimal? paidAmount, GatewayPaymentInstrument? instrument)
    {
        // Entry point for REDIRECT gateways (Easebuzz): the gateway POSTs its own verified result to
        // the server, so there is no signature for the browser to hand back. The caller MUST have
        // verified the gateway's hash before calling this — this method trusts nothing else about the
        // browser, but it cannot re-verify a hash it never saw.
        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.FranchiseId != null);
        if (order == null) return (false, "Franchise order not found.", null);
        if (order.PaymentStatus == PaymentStatus.Success) return (true, null, order.OrderNumber); // idempotent

        // Amount check: the franchisee pays the NET, so that is what must have arrived. A mismatch is
        // a security event, not a rounding nuisance — refuse and leave the order unpaid.
        if (paidAmount is { } paid)
        {
            var expected = order.FranchiseNetPayable;
            if (Math.Abs(paid - expected) > 0.99m)
            {
                _log.LogError("SECURITY: franchise callback amount mismatch OrderNumber={OrderNumber} Expected={Expected} Paid={Paid}",
                    order.OrderNumber, expected, paid);
                return (false, "Paid amount does not match the order amount.", null);
            }
        }

        order.Status = OrderStatus.Confirmed;
        order.PaymentStatus = PaymentStatus.Success;
        order.ConfirmedAt = DateTime.UtcNow;
        if (instrument != null)
            order.GatewayPaymentMode = instrument.Mode.Length <= 40 ? instrument.Mode : instrument.Mode[..40];

        // The FRANCHISE settlement path — franchise commission, the wallet-vs-invoice rule and the
        // franchise notifications all live here. The customer checkout path does not know about them,
        // which is why a franchise order must never be settled through it.
        await FinalizeOrderAsync(order, debitWallet: false);

        _log.LogInformation("Franchise gateway callback settled OrderNumber={OrderNumber} GwPaymentId={Pid} Mode={Mode}",
            order.OrderNumber, gatewayPaymentId, instrument?.Mode);
        return (true, null, order.OrderNumber);
    }

    // ── Shared: validate + price + build + save the order (Pending for gateway, Confirmed for wallet). ──
    private async Task<(bool ok, string? error, Order? order, decimal total)> BuildOrderAsync(Guid franchiseId, FranchiseOrderRequest req)
    {
        // ── Validate ─────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(req.CustomerName)) return (false, "Customer / contact name is required.", null, 0);
        if (string.IsNullOrWhiteSpace(req.Phone) || req.Phone.Trim().Length < 10) return (false, "A valid 10-digit mobile number is required.", null, 0);
        if (string.IsNullOrWhiteSpace(req.City)) return (false, "City is required.", null, 0);

        // ── Student contact + delivery address are mandatory ──
        // These feed the SHIPPING address (billing is the franchisee's own), so an order without them
        // cannot be despatched — and course access + order updates go to the email. They used to be
        // optional, which is how orders reached despatch with no PIN code at all.
        var email = (req.Email ?? string.Empty).Trim();
        if (email.Length == 0) return (false, "Student email is required — order updates and course access are sent there.", null, 0);
        if (!email.Contains('@') || !email.Contains('.')) return (false, "Please enter a valid student email address.", null, 0);

        if (string.IsNullOrWhiteSpace(req.State)) return (false, "State is required.", null, 0);

        // 6 digits, nothing else. A required-but-wrong PIN is no better than a blank one for a courier.
        var pin = (req.PinCode ?? string.Empty).Trim();
        if (pin.Length == 0) return (false, "PIN code is required for delivery.", null, 0);
        if (pin.Length != 6 || !pin.All(char.IsAsciiDigit)) return (false, "PIN code must be exactly 6 digits.", null, 0);

        if (string.IsNullOrWhiteSpace(req.AddressLine)) return (false, "Delivery address is required.", null, 0);

        if (req.ShippingCharges < 0) return (false, "Shipping charges can't be negative.", null, 0);

        var lines = req.EffectiveLines;
        if (lines.Count == 0) return (false, "Please add at least one course to the order.", null, 0);
        if (lines.Any(l => l.Quantity <= 0)) return (false, "Every line must have a quantity of at least 1.", null, 0);
        if (lines.Any(l => l.Discount < 0)) return (false, "Line discount can't be negative.", null, 0);

        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == franchiseId);
        if (f == null) return (false, "Franchise not found.", null, 0);

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Modes)
            .Include(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .ToDictionaryAsync(p => p.Id);
        if (productIds.Any(id => !products.ContainsKey(id)))
            return (false, "One or more selected courses are not available.", null, 0);

        // ── Pricing (per line, then summed) ──────────────────────────────────
        var settings = await _db.FranchiseSettings.AsNoTracking().FirstOrDefaultAsync() ?? new FranchiseSettings();
        var overrideRules = await _db.FranchiseCommissions
            .Where(c => c.FranchiseId == franchiseId && productIds.Contains(c.ProductId))
            .ToDictionaryAsync(c => c.ProductId);

        // Whether this franchisee charges GST on their commission. Same test as the B2B/B2C
        // classification below, so a franchisee is never registered for one and not the other.
        var franchiseeIsGstRegistered = !string.IsNullOrWhiteSpace(f.Gstin);

        // Pass 1 — price every line and resolve the franchisee's share. The share is deliberately
        // computed BEFORE any discount code: a coupon reduces what the franchisee PAYS, never what
        // they EARN. Order items are built in pass 2, once the order-level discount is known.
        decimal subtotal = 0, discount = 0;
        var shareSplit = FranchiseCommissionMath.CommissionSplit.Zero;
        var priced = new List<PricedLine>(lines.Count);
        foreach (var line in lines)
        {
            var product = products[line.ProductId];

            // ── Lecture mode + purchase options (ADD-ONS, re-resolved server-side so a tampered
            //    client can't inject a price). Mirrors OrderAdminService/CheckoutService pricing. ──
            var mode = line.ProductModeId.HasValue
                ? product.Modes.FirstOrDefault(m => m.Id == line.ProductModeId.Value && m.IsEnabled)
                : null;
            // Every active option group requires exactly one pick (enforced client- and server-side).
            decimal optAddOn = 0m;
            var optSnap = new List<object>();
            foreach (var g in product.OptionGroups.Where(g => g.IsActive))
            {
                var picked = g.Items.FirstOrDefault(i => i.IsActive && line.SelectedOptionIds.Contains(i.Id));
                if (picked == null)
                    return (false, $"Please choose an option for \"{g.Name}\" on {product.Title}.", null, 0);
                optAddOn += picked.PriceAddOn;
                optSnap.Add(new { groupName = g.Name, optionName = picked.Name, addOn = picked.PriceAddOn });
            }

            // Base = franchisee's effective selling price (special when active) + mode + option add-ons.
            var unit = product.EffectiveSellingPrice + (mode?.Price ?? 0m) + optAddOn;
            var lineSubtotal = unit * line.Quantity;
            var lineDiscount = Math.Min(line.Discount, lineSubtotal);
            var lineNet = lineSubtotal - lineDiscount;

            subtotal += lineSubtotal;
            discount += lineDiscount;

            // Franchisee's share on this line (per-unit × qty) — same calculator used everywhere.
            overrideRules.TryGetValue(product.Id, out var rule);
            var calc = _shares.Calculate(product, rule, settings, franchiseeIsGstRegistered);
            if (calc.HasShare)
            {
                // Commission basis INCLUDES the mode + option add-ons: a % share also applies to the
                // add-on amount; a fixed ₹ share stays per-unit regardless of add-ons. The add-on is
                // priced GST-inclusive like everything else, so it goes through the same reverse
                // calculation rather than having the percentage applied to the gross.
                var perUnit = new FranchiseCommissionMath.CommissionSplit(
                    calc.CommissionAmount, calc.GstOnCommission, calc.CalculatedFranchiseAmount);

                var addOnTotal = (mode?.Price ?? 0m) + optAddOn;
                if (calc.ShareType == CommissionType.Percent && addOnTotal > 0)
                    perUnit = perUnit.Add(FranchiseCommissionMath.FromPercent(
                        addOnTotal, product.GstRate, calc.ShareValue, franchiseeIsGstRegistered));

                shareSplit = shareSplit.Add(perUnit.Times(line.Quantity));
            }

            priced.Add(new PricedLine(product, mode, line, optSnap, unit, lineDiscount, lineNet));
        }

        // ── Discount code (order level) ──────────────────────────────────────
        // Re-resolved from the database every time: the client sends a code, never an amount. This
        // is stacked ON TOP OF the share computed above — the franchisee keeps their full earning
        // and the coupon comes off the balance they pay.
        Coupon? coupon = null;
        decimal couponDiscount = 0m;
        if (!string.IsNullOrWhiteSpace(req.CouponCode))
        {
            var (found, couponError) = await FindFranchiseCouponAsync(franchiseId, req.CouponCode);
            if (found == null) return (false, couponError, null, 0);

            var couponBase = subtotal - discount;
            if (couponBase < found.MinOrder)
                return (false, $"Discount \"{found.Code}\" needs a minimum order of ₹{found.MinOrder:N0} — this order is ₹{couponBase:N0}.", null, 0);

            coupon = found;
            couponDiscount = CouponDiscountOn(found, couponBase);
        }

        // Spread the order-level discount across the lines in proportion to their value, so each
        // line's GST is charged on what was actually paid for it and the item rows still sum to the
        // order total.
        var couponSlices = SpreadDiscount(couponDiscount, priced.Select(p => p.LineNet).ToList());

        // Pass 2 — build the order items on the post-discount amounts.
        decimal gst = 0;
        var orderItems = new List<OrderItem>(priced.Count);
        for (var i = 0; i < priced.Count; i++)
        {
            var p = priced[i];
            var lineNet = p.LineNet - couponSlices[i];
            var lineGst = Math.Round(lineNet * p.Product.GstRate / (100m + p.Product.GstRate), 2);
            gst += lineGst;

            orderItems.Add(new OrderItem
            {
                ProductId = p.Product.Id,
                ProductTitle = p.Product.Title,
                ProductModeId = p.Mode?.Id,
                ModeName = p.Mode?.ModeName ?? p.Line.ModeName,
                SelectedOptionIdsJson = (p.Line.SelectedOptionIds is { Count: > 0 }) ? JsonSerializer.Serialize(p.Line.SelectedOptionIds) : null,
                SelectedOptionsJson = p.OptionSnapshot.Count == 0 ? null : JsonSerializer.Serialize(p.OptionSnapshot),
                Quantity = p.Line.Quantity,
                UnitPrice = p.UnitPrice,
                // The line's own discount plus its slice of the coupon — what the invoice shows off this line.
                Discount = p.LineDiscount + couponSlices[i],
                GstRate = p.Product.GstRate,
                GstAmount = lineGst,
                LineTotal = lineNet
            });
        }

        // Order.DiscountAmount is every rupee off the order: per-line discounts + the coupon.
        discount += couponDiscount;

        var total = subtotal - discount + req.ShippingCharges;
        if (total < 0) total = 0;

        // The franchisee keeps their share by paying less: net payable = total − share. The coupon
        // has already reduced the total, so the two reductions stack. The share is still capped at
        // the (discounted) total — we can't pay out more than the order is worth — so a discount
        // deep enough to drive the total below the share trims the share to what's left. Capping
        // scales the commission and its GST together so the two still sum to what's paid out.
        var cappedSplit = FranchiseCommissionMath.CapTo(shareSplit, Math.Min(shareSplit.TotalPayout, total));
        var shareAmount = cappedSplit.TotalPayout;
        var netPayable = total - shareAmount;

        const string SellerState = "Maharashtra";
        // Place of supply follows the BILLED party — the franchisee, in whose name the tax invoice is
        // raised — not the student receiving the material. Using the student's state here would
        // print CGST/SGST on an invoice addressed to an out-of-state franchisee (or vice versa).
        var intraState = string.Equals(f.State?.Trim(), SellerState, StringComparison.OrdinalIgnoreCase);
        decimal cgst = 0, sgst = 0, igst = 0;
        if (intraState) { cgst = Math.Round(gst / 2m, 2); sgst = gst - cgst; }
        else { igst = gst; }

        var payByGateway = req.PaymentMethod == FranchisePaymentMethod.Gateway;

        // The mode stamped on the order must be the gateway the payment will actually run on, because
        // verification later resolves the gateway FROM this value. It used to be hardcoded Razorpay,
        // so an Easebuzz-only setup produced razorpay orders that no gateway would ever settle.
        var gatewayMode = PaymentMode.BankTransfer;
        if (payByGateway)
        {
            var resolved = await _paymentModes.ResolveOnlineGatewayAsync();
            if (resolved == null)
                return (false, "Online payment is currently unavailable. Please contact administrator.", null, 0);
            gatewayMode = resolved.Mode;
        }

        // The franchisee pays the NET (total − their share). Wallet payment is prepaid from
        // balance+credit so the NET must be affordable up front. Gateway settles externally.
        if (!payByGateway)
        {
            var available = f.WalletBalance + f.CreditLimit;
            if (netPayable > available)
                return (false, $"Insufficient balance. Available ₹{available:N0}, you need to pay ₹{netPayable:N0} (after your ₹{shareAmount:N0} share). Top up your wallet or choose Pay Online.", null, 0);
        }

        // ── Build order ─────────────────────────────────────────────────────
        // The customer/learner is the recipient (Student*). The INVOICE party is the FRANCHISEE,
        // so all billing fields are taken from the franchise — the invoice is raised in their name.
        var franchiseLegalName = !string.IsNullOrWhiteSpace(f.BusinessName) ? f.BusinessName!.Trim() : f.Name.Trim();
        // Same flag that drove the commission split above — a franchisee cannot be registered for
        // billing classification and unregistered for their commission.
        var franchiseIsB2B = franchiseeIsGstRegistered;

        // Resolve the STUDENT's customer account before the order is written, so the order is linked
        // from the moment it exists — every path that reads Order.UserId (enrollment activation, the
        // customer's order history, course access) then works without a second pass.
        var orderNumber = await GenerateFranchiseOrderNumberAsync();
        var studentUserId = await ResolveStudentUserIdAsync(
            orderNumber, req.CustomerName.Trim(), req.Phone.Trim(), req.Email,
            string.IsNullOrWhiteSpace(req.City) ? f.City : req.City.Trim(), req.State);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = orderNumber,
            UserId = studentUserId,
            StudentName = req.CustomerName.Trim(),
            StudentPhone = req.Phone.Trim(),
            StudentEmail = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            StudentCity = string.IsNullOrWhiteSpace(req.City) ? f.City : req.City.Trim(),
            Source = OrderSource.Franchisee,
            FranchiseId = franchiseId,

            // Invoice billing party = the franchisee.
            CustomerType = franchiseIsB2B ? CustomerType.Organization : CustomerType.Individual,
            OrgName = franchiseIsB2B ? franchiseLegalName : null,
            BillingName = franchiseLegalName,
            BillingAddress = string.IsNullOrWhiteSpace(f.AddressLine) ? null : f.AddressLine!.Trim(),
            BillingCity = string.IsNullOrWhiteSpace(f.City) ? null : f.City.Trim(),
            BillingState = string.IsNullOrWhiteSpace(f.State) ? null : f.State!.Trim(),
            BillingPincode = string.IsNullOrWhiteSpace(f.PinCode) ? null : f.PinCode!.Trim(),
            GstNumber = string.IsNullOrWhiteSpace(f.Gstin) ? null : f.Gstin!.Trim().ToUpperInvariant(),
            GstClassification = franchiseIsB2B ? "B2B" : "B2C",

            // Shipping goes to the end customer's address (the learner receiving the material).
            ShippingAddress = NullIfEmpty(req.ShipToBilling ? req.AddressLine : req.ShippingAddress),
            ShippingCity = NullIfEmpty(req.ShipToBilling ? req.City : req.ShippingCity),
            ShippingState = NullIfEmpty(req.ShipToBilling ? req.State : req.ShippingState),
            ShippingPincode = NullIfEmpty(req.ShipToBilling ? req.PinCode : req.ShippingPinCode),
            ShippingCharges = req.ShippingCharges,

            CustomerNotes = string.IsNullOrWhiteSpace(req.CustomerNotes) ? null : req.CustomerNotes.Trim(),
            Subtotal = subtotal,
            DiscountAmount = discount,
            CouponId = coupon?.Id,
            CouponCode = coupon?.Code,
            GstAmount = gst,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            TotalAmount = total,
            FranchiseShareAmount = shareAmount,
            FranchiseCommissionBase = cappedSplit.Commission,
            FranchiseCommissionGst = cappedSplit.GstOnCommission,
            FranchiseNetPayable = netPayable,
            Status = payByGateway ? OrderStatus.Pending : OrderStatus.Confirmed,
            PaymentStatus = payByGateway ? PaymentStatus.Pending : PaymentStatus.Success,
            PaymentMode = gatewayMode,   // resolved above: the enabled gateway, or BankTransfer for wallet
            // Wallet-funded orders aren't invoiced — the top-up that funded them already was.
            // Gateway orders are invoiced exactly as before.
            PaidFromWallet = !payByGateway,
            ConfirmedAt = payByGateway ? null : DateTime.UtcNow
        };
        foreach (var item in orderItems) order.Items.Add(item);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return (true, null, order, total);
    }

    // ── Shared: wallet debit (optional) + commission + invoice + notification. ──
    private async Task FinalizeOrderAsync(Order order, bool debitWallet)
    {
        // THE choke point. Every way a franchise order can become paid — wallet, popup gateway
        // confirmation, redirect gateway callback — funnels through here, so this is the one place
        // that can guarantee no franchise order is ever finalised as paid while its student has no
        // customer account. BuildOrderAsync already resolved one at creation; this is the retry for
        // the case where that attempt failed, and a no-op the rest of the time.
        //
        // It does NOT grant course access. Identity and access stay separate concerns: enrollment
        // still depends on the existing activation flow, exactly as before.
        await EnsureOrderUserAsync(order);

        var f = await _db.Franchises.FirstOrDefaultAsync(x => x.Id == order.FranchiseId);
        if (f == null) return;

        // The franchisee pays the NET (total − their share). The share is realised at payment
        // (they pay less), so it is NOT also written to the commission ledger — that would
        // double-count. The ledger records exactly what the franchisee paid.
        var payable = order.FranchiseNetPayable;
        var label = $"{f.Name} — order {order.OrderNumber} (student: {order.StudentName})";

        if (debitWallet)
        {
            f.WalletBalance -= payable;
            _db.FranchiseLedger.Add(new FranchiseLedgerEntry
            {
                FranchiseId = order.FranchiseId!.Value, IsCredit = false, Amount = payable,
                BalanceAfter = f.WalletBalance,
                Description = $"{label} · paid ₹{payable:N0} (₹{order.FranchiseShareAmount:N0} share retained)",
                OrderId = order.Id
            });
        }
        else
        {
            // Gateway-paid: informational ledger entry (no wallet balance change).
            _db.FranchiseLedger.Add(new FranchiseLedgerEntry
            {
                FranchiseId = order.FranchiseId!.Value, IsCredit = false, Amount = 0m,
                BalanceAfter = f.WalletBalance,
                Description = $"{label} · paid ₹{payable:N0} online (₹{order.FranchiseShareAmount:N0} share retained)",
                OrderId = order.Id
            });
        }

        // Count the redemption once the order is actually paid — same point the website checkout
        // does it, so TotalLimit can't be burned by orders that were never completed.
        if (order.CouponId.HasValue)
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == order.CouponId.Value);
            if (coupon != null) coupon.TotalUsed++;
        }

        // Bump per-product order counts.
        var ids = order.Items.Select(i => i.ProductId).ToList();
        var prods = await _db.Products.Where(p => ids.Contains(p.Id)).ToListAsync();
        foreach (var item in order.Items)
        {
            var p = prods.FirstOrDefault(x => x.Id == item.ProductId);
            if (p != null) p.TotalOrders += item.Quantity;
        }

        await _db.SaveChangesAsync();

        // NOTE: commission is intentionally NOT recorded here — the franchisee already keeps their
        // share by paying the net amount, so recording a commission entry would double-count it.

        // GST invoice (idempotent) — raised in the franchisee's name, shows the share as a
        // deduction and the net as the payable total.
        try { await _invoices.EnsureForOrderAsync(order.Id, actorName: "franchise-portal"); } catch { /* non-fatal */ }

        // Provision LMS access / serial keys (Superclass, RioPlay, Valence) for this now-paid order.
        // Same idempotent, PaymentStatus.Success-gated enqueue the website + admin/counter paths use,
        // so franchisee-portal orders (wallet AND gateway — both reach here) now grant access too.
        // Non-fatal: a provisioning hiccup must never fail the order the franchisee just paid for.
        try
        {
            var enqueued = await _serialKeys.EnqueueForOrderAsync(order.Id);
            if (enqueued > 0)
                _log.LogInformation("SerialKey enqueued count={Count} orderNumber={OrderNumber} (franchise portal)", enqueued, order.OrderNumber);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SerialKey enqueue threw orderNumber={OrderNumber} (franchise portal)", order.OrderNumber);
        }

        // Faculty share ledger — the franchise portal's missing half. The website and admin/counter
        // paths have always recorded it on payment; this path never did, so a faculty earned nothing
        // from a franchisee's sale and the Faculty-Wise report could not show the order at all.
        //
        // Placed here for the same reason the serial-key enqueue is: reaching this method means the
        // order is paid, whichever of the three routes it came by. The share is computed by the one
        // existing calculator — nothing about the rules, the split or the GST is re-implemented — and
        // the write is idempotent per order, so a repeated gateway callback adds nothing.
        //
        // Non-fatal, exactly like the two blocks above: a ledger row can be replayed, a settled
        // payment cannot be un-settled.
        try
        {
            await _facultyShares.RecordOrderFacultyShareAsync(order.Id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Faculty share ledger write threw orderNumber={OrderNumber} (franchise portal)", order.OrderNumber);
        }

        await _notify.SendAsync("order_placed",
            new NotificationRecipient(order.StudentEmail, order.StudentPhone),
            new Dictionary<string, string>
            {
                ["name"] = order.StudentName,
                ["order_number"] = order.OrderNumber,
                ["total"] = order.FranchiseNetPayable.ToString("N0"),
                ["items"] = order.Items.Sum(i => i.Quantity).ToString(),
                ["franchise"] = f.Name
            });
    }

    public async Task<List<FranchiseOrderRow>> MyOrdersAsync(Guid franchiseId) =>
        // Top-ups belong on the Wallet page, not in the franchisee's order history.
        await _db.Orders.ExcludeWalletTopUps().Include(o => o.Items).Where(o => o.FranchiseId == franchiseId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new FranchiseOrderRow
            {
                Id = o.Id, OrderNumber = o.OrderNumber, CreatedAt = o.CreatedAt, FranchiseName = "",
                StudentName = o.StudentName, StudentPhone = o.StudentPhone,
                ProductSummary = o.Items.Count == 0 ? "—" : o.Items.First().ProductTitle + (o.Items.Count > 1 ? $" +{o.Items.Count - 1}" : ""),
                TotalAmount = o.TotalAmount, Status = o.Status, PaidFromWallet = o.PaidFromWallet
            }).ToListAsync();

    public async Task<List<FranchiseLedgerItem>> LedgerAsync(Guid franchiseId)
    {
        var rows = await _db.FranchiseLedger.Where(e => e.FranchiseId == franchiseId)
            .OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync();
        var orderNos = await _db.Orders.Where(o => o.FranchiseId == franchiseId)
            .Select(o => new { o.Id, o.OrderNumber }).ToDictionaryAsync(o => o.Id, o => o.OrderNumber);
        return rows.Select(e => new FranchiseLedgerItem(
            e.Id, e.IsCredit, e.Amount, e.BalanceAfter, e.Description,
            e.OrderId.HasValue && orderNos.TryGetValue(e.OrderId.Value, out var n) ? n : null, e.CreatedAt,
            e.OrderId)).ToList();
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

    /// <summary>A line after pricing but before the order-level discount is spread over it. Carries
    /// everything pass 2 needs to build the OrderItem.</summary>
    private sealed record PricedLine(
        Product Product, ProductMode? Mode, FranchiseOrderLine Line, List<object> OptionSnapshot,
        decimal UnitPrice, decimal LineDiscount, decimal LineNet);

    /// <summary>Splits an order-level discount across lines in proportion to their value. Slices are
    /// rounded to paise and the last contributing line absorbs the remainder, so the parts always sum
    /// to exactly <paramref name="amount"/> and the item rows reconcile with the order total.</summary>
    private static List<decimal> SpreadDiscount(decimal amount, IReadOnlyList<decimal> lineValues)
    {
        var slices = new decimal[lineValues.Count];
        var pool = lineValues.Sum();
        if (amount <= 0 || pool <= 0) return slices.ToList();

        decimal allocated = 0m;
        var last = -1;
        for (var i = 0; i < lineValues.Count; i++)
        {
            if (lineValues[i] <= 0) continue;
            slices[i] = Math.Round(amount * lineValues[i] / pool, 2);
            allocated += slices[i];
            last = i;
        }
        if (last >= 0) slices[last] += amount - allocated;
        return slices.ToList();
    }

    private async Task<string> GenerateFranchiseOrderNumberAsync()
    {
        // Max numeric suffix (CreatedAt is unreliable — seeded FRN orders share a timestamp).
        var nums = await _db.Orders.Where(o => o.OrderNumber.StartsWith("FRN-"))
            .Select(o => o.OrderNumber).ToListAsync();
        var max = 1003;
        foreach (var n in nums)
            if (int.TryParse(n.Split('-').Last(), out var v) && v > max) max = v;
        return $"FRN-{max + 1}";
    }
}
