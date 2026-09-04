using System.Text.Json;
using RioCommerce.Core.DTOs.Attributes;
using RioCommerce.Core.DTOs.Checkout;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Shipping;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

public class CheckoutService : ICheckoutService
{
    // Place of supply for tax split: intra-state (CGST+SGST) vs inter-state (IGST).
    private const string SellerState = "Maharashtra";

    private readonly RioCommerceDbContext _db;
    private readonly IPaymentGatewayFactory _gateways;
    private readonly INotificationSender _notify;
    private readonly INotificationCenterService _center;
    private readonly IRealtimeBus _bus;
    private readonly ISerialKeyService _serialKeys;
    private readonly IInvoiceService _invoices;
    private readonly IFacultySharingService _facultyShares;
    private readonly ILogger<CheckoutService> _log;

    public CheckoutService(RioCommerceDbContext db, IPaymentGatewayFactory gateways, INotificationSender notify,
        INotificationCenterService center, IRealtimeBus bus, ISerialKeyService serialKeys,
        IInvoiceService invoices, IFacultySharingService facultyShares, ILogger<CheckoutService> log)
    {
        _db = db;
        _gateways = gateways;
        _notify = notify;
        _center = center;
        _bus = bus;
        _serialKeys = serialKeys;
        _invoices = invoices;
        _facultyShares = facultyShares;
        _log = log;
    }

    // Maps the PaymentMode enum the customer selected to the matching gateway name.
    private static string GatewayNameFor(PaymentMode mode) => mode switch
    {
        PaymentMode.Razorpay => "Razorpay",
        PaymentMode.Easebuzz => "Easebuzz",
        _ => "Razorpay" // fallback for any future online-mode value
    };

    // Convenience for paths that need the live gateway against the order's selected mode.
    private IPaymentGateway _gateway => _gateways.Get("Razorpay");

    // Buyer tier for pricing: student on a school roll OR active staff (Principal / Coordinator).
    // Matches CartService.IsSchoolStudentAsync so the checkout total lines up with the cart total
    // the buyer just saw. Server-authoritative; never trust a client flag.
    private async Task<bool> IsSchoolStudentAsync(Guid userId)
    {
        if (await _db.SchoolStudents.AnyAsync(s => s.UserId == userId && s.IsActive)) return true;
        return await _db.SchoolUsers.AnyAsync(su => su.UserId == userId && su.IsActive);
    }

    public async Task<CheckoutSummary> GetSummaryAsync(Guid userId, string? couponCode = null, string? affiliateCode = null,
        IReadOnlyList<CheckoutAttributeSelection>? checkoutSelections = null)
    {
        var rows = await LoadCartAsync(userId);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);

        var isSchoolStudent = await IsSchoolStudentAsync(userId);
        var summary = new CheckoutSummary();
        foreach (var c in rows)
        {
            // Base price picks the buyer's tier (school student vs regular); mode / options stack.
            var basePrice = c.Product.EffectivePriceFor(isSchoolStudent);
            var (optAddOn, _) = ResolveOptionAddOns(c);
            var unit = basePrice + (c.ProductMode?.Price ?? 0m) + optAddOn + c.AttributePriceAdjustment;
            summary.Items.Add(new CheckoutItemLine(
                c.ProductId, c.Product.Slug, c.Product.Title,
                c.Product.PrimaryFaculty?.DisplayName, c.ProductMode?.ModeName,
                c.Quantity, unit, c.Product.Mrp, CartService.AttributeSummary(c.SelectedAttributesJson),
                c.Product.BatchStatus,
                c.Product.LectureAccessTiming, c.Product.NotesDispatchTimeline));
        }

        summary.Subtotal = summary.Items.Sum(i => i.UnitPrice * i.Quantity);
        var productSavings = summary.Items.Sum(i => Math.Max(0, i.Mrp - i.UnitPrice) * i.Quantity);

        var affiliateId = await ResolveAffiliateIdAsync(affiliateCode, userId);
        var (discount, code, message) = await ResolveCouponAsync(couponCode, summary.Subtotal, affiliateId);
        summary.Discount = discount;
        summary.CouponCode = code;
        summary.CouponMessage = message;

        var afterDiscount = summary.Subtotal - summary.Discount;
        summary.CheckoutAttributes = await LoadCheckoutAttributesAsync();
        var (checkoutAmount, _) = await ResolveCheckoutAttributesAsync(afterDiscount, checkoutSelections);
        summary.CheckoutAttributesAmount = checkoutAmount;

        summary.Total = afterDiscount + checkoutAmount;
        summary.GstIncluded = Math.Round(summary.Total * 18m / 118m, 2);   // prices are GST-inclusive
        summary.Savings = productSavings + summary.Discount;

        if (user != null)
            summary.Billing = new BillingInfo
            {
                Name = user.FullName,
                Phone = user.Phone ?? string.Empty,
                Email = user.Email,
                City = user.City,
                State = string.IsNullOrWhiteSpace(user.State) ? SellerState : user.State!
            };

        return summary;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(Guid userId, CheckoutRequest req)
    {
        // Franchise accounts must order via the Franchise Portal, not the customer checkout.
        if (await _db.Franchises.AnyAsync(f => f.AdminUserId == userId && f.IsActive))
            throw new InvalidOperationException(
                "Franchise accounts place orders through the Franchise Portal. Please use Franchise → Place Order.");

        var rows = await LoadCartAsync(userId);
        if (rows.Count == 0) throw new InvalidOperationException("Your cart is empty.");

        // Re-check purchase availability at order time: the flag may have been switched off after
        // these lines were added, and no order may be created for a withdrawn product.
        var blocked = rows.Where(r => !r.Product.AllowCustomerPurchase).Select(r => r.Product.Title).ToList();
        if (blocked.Count > 0)
            throw new InvalidOperationException(
                $"{string.Join(", ", blocked)} is currently not available for purchase. Please remove it from your cart to continue.");

        var user = await _db.Users.FirstAsync(u => u.Id == userId);

        // Recompute everything server-side — never trust client amounts.
        // Buyer tier picks the base price; mode / options stack additively on top.
        var isSchoolStudent = await IsSchoolStudentAsync(userId);
        var subtotal = rows.Sum(c =>
            ((c.Product.EffectivePriceFor(isSchoolStudent)
              + (c.ProductMode?.Price ?? 0m) + ResolveOptionAddOns(c).addOn + c.AttributePriceAdjustment) * c.Quantity));
        var affiliateId = await ResolveAffiliateIdAsync(req.AffiliateCode, userId);
        var (discount, couponCode, _) = await ResolveCouponAsync(req.CouponCode, subtotal, affiliateId);
        var coupon = couponCode == null
            ? null
            : await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == couponCode.ToUpper() && c.IsActive);

        var afterDiscount = subtotal - discount;
        var (checkoutAmount, checkoutJson) = await ResolveCheckoutAttributesAsync(afterDiscount, req.CheckoutSelections);
        var total = afterDiscount + checkoutAmount;

        var b = req.Billing;
        var (cgst, sgst, igst) = SplitGst(total, b.State);
        var gst = cgst + sgst + igst;

        // Resolve the referral source (snapshot Name + Type onto the Order so admin edits later don't rewrite history).
        Guid? refId = null; string? refName = null; RioCommerce.Core.Enums.ReferralSourceType? refType = null; string? refCustom = null;
        if (req.ReferralSourceId is { } rsid && rsid != Guid.Empty)
        {
            var rs = await _db.ReferralSources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rsid && r.IsActive);
            if (rs != null)
            {
                refId = rs.Id; refName = rs.Name; refType = rs.Type;
                if (rs.Type == RioCommerce.Core.Enums.ReferralSourceType.Other && !string.IsNullOrWhiteSpace(req.ReferralCustomText))
                {
                    var t = req.ReferralCustomText.Trim();
                    refCustom = t.Length > 100 ? t[..100] : t;
                }
            }
        }

        var order = new Order
        {
            OrderNumber = await GenerateOrderNumberAsync(),
            UserId = userId,
            StudentName = string.IsNullOrWhiteSpace(b.Name) ? user.FullName : b.Name.Trim(),
            StudentPhone = string.IsNullOrWhiteSpace(b.Phone) ? (user.Phone ?? string.Empty) : b.Phone.Trim(),
            StudentEmail = string.IsNullOrWhiteSpace(b.Email) ? user.Email : b.Email!.Trim(),
            StudentCity = b.City?.Trim(),
            Source = OrderSource.Website,
            AffiliateId = affiliateId,
            ReferralSourceId = refId,
            ReferralSourceName = refName,
            ReferralType = refType,
            ReferralCustomText = refCustom,
            Subtotal = subtotal,
            DiscountAmount = discount,
            CouponId = coupon?.Id,
            CouponCode = couponCode,
            GstAmount = gst,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            TotalAmount = total,
            CheckoutAttributesJson = checkoutJson,
            CheckoutAttributesAmount = checkoutAmount,
            BillingName = string.IsNullOrWhiteSpace(b.Name) ? null : b.Name.Trim(),
            BillingAddress = string.IsNullOrWhiteSpace(b.Address) ? null : b.Address.Trim(),
            BillingCity = b.City?.Trim(),
            BillingState = b.State?.Trim(),
            BillingPincode = b.Pincode?.Trim(),
            GstNumber = string.IsNullOrWhiteSpace(b.GstNumber) ? null : b.GstNumber.Trim().ToUpper(),
            GstClassification = string.IsNullOrWhiteSpace(b.GstNumber) ? "B2C" : "B2B",
            PaymentMode = req.PaymentMode,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        foreach (var c in rows)
        {
            // Same tier resolution as the subtotal above — the price frozen onto the order line
            // must match what the buyer paid. Mode / options stack on top of the buyer base.
            var basePrice = c.Product.EffectivePriceFor(isSchoolStudent);
            var (optAddOn, optSnapshot) = ResolveOptionAddOns(c);
            var unit = basePrice + (c.ProductMode?.Price ?? 0m) + optAddOn + c.AttributePriceAdjustment;
            order.Items.Add(new OrderItem
            {
                ProductId = c.ProductId,
                ProductModeId = c.ProductModeId,
                ProductTitle = c.Product.Title,
                ModeName = c.ProductMode?.ModeName,
                SelectedAttributesJson = c.SelectedAttributesJson,
                SelectedOptionIdsJson = c.SelectedOptionIdsJson,
                SelectedOptionsJson = optSnapshot,
                Quantity = c.Quantity,
                UnitPrice = unit,
                GstRate = c.Product.GstRate,
                LineTotal = unit * c.Quantity
            });
        }

        GatewayOrder? gw = null;
        if (req.PayOnline)
        {
            var gateway = _gateways.Get(GatewayNameFor(req.PaymentMode));
            var productInfo = string.Join(", ", order.Items.Select(i => i.ProductTitle).Where(t => !string.IsNullOrWhiteSpace(t)).Take(3));
            if (string.IsNullOrEmpty(productInfo)) productInfo = $"Order {order.OrderNumber}";

            var returnUrl = BuildCallbackUrl(req.OriginBaseUrl);

            var context = new GatewayCreateContext(
                CustomerName: req.Billing.Name ?? string.Empty,
                CustomerEmail: req.Billing.Email ?? string.Empty,
                CustomerPhone: req.Billing.Phone ?? string.Empty,
                ProductInfo: productInfo,
                ReturnUrl: returnUrl);

            gw = await gateway.CreateOrderAsync(order.OrderNumber, total, "INR", context);
            order.Payments.Add(new Payment
            {
                Amount = total,
                PaymentMode = req.PaymentMode,
                Status = PaymentStatus.Pending,
                GatewayName = gw.GatewayName,
                GatewayOrderId = gw.GatewayOrderId
            });
        }

        _db.Orders.Add(order);

        // ── Save the checkout address to the customer's address book (issue #3) ──
        // So it pre-fills on their next order. Upsert: update the most-recent saved address if one
        // exists, else add a new one. Only when there's real address content. Best-effort.
        if (!string.IsNullOrWhiteSpace(b.Address) || !string.IsNullOrWhiteSpace(b.City)
            || !string.IsNullOrWhiteSpace(b.Pincode))
        {
            var nameParts = (b.Name ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var existingAddr = await _db.CustomerAddresses
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.UpdatedAt)
                .FirstOrDefaultAsync();

            if (existingAddr == null)
            {
                _db.CustomerAddresses.Add(new CustomerAddress
                {
                    UserId = userId,
                    FirstName = nameParts.ElementAtOrDefault(0),
                    LastName = nameParts.ElementAtOrDefault(1),
                    Email = b.Email,
                    Phone = b.Phone,
                    Address1 = b.Address?.Trim(),
                    City = b.City?.Trim(),
                    State = string.IsNullOrWhiteSpace(b.State) ? "Maharashtra" : b.State.Trim(),
                    Pincode = b.Pincode?.Trim(),
                    Country = "India"
                });
            }
            else
            {
                existingAddr.FirstName = nameParts.ElementAtOrDefault(0) ?? existingAddr.FirstName;
                existingAddr.LastName = nameParts.ElementAtOrDefault(1) ?? existingAddr.LastName;
                existingAddr.Email = b.Email ?? existingAddr.Email;
                existingAddr.Phone = b.Phone ?? existingAddr.Phone;
                if (!string.IsNullOrWhiteSpace(b.Address)) existingAddr.Address1 = b.Address.Trim();
                if (!string.IsNullOrWhiteSpace(b.City)) existingAddr.City = b.City.Trim();
                if (!string.IsNullOrWhiteSpace(b.State)) existingAddr.State = b.State.Trim();
                if (!string.IsNullOrWhiteSpace(b.Pincode)) existingAddr.Pincode = b.Pincode.Trim();
                existingAddr.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Cart-clearing policy:
        //   • Offline orders (no online payment) are a completed checkout intent → clear the cart now.
        //   • Online orders clear the cart ONLY after the payment is verified successful
        //     (CompleteOrderAsync). This keeps the cart intact if the gateway payment fails or is
        //     abandoned, so the customer can simply re-purchase. Issue #2.
        if (!req.PayOnline)
            _db.CartItems.RemoveRange(rows);
        await _db.SaveChangesAsync();

        await _center.NotifyAsync(AdminNotificationType.NewOrder, NotificationSeverity.Success,
            "New website order", $"Order #{order.OrderNumber} · ₹{total:N0} · {order.StudentName}", $"/admin/orders/{order.Id}", order.Id.ToString());
        _bus.PublishDataChanged(new RealtimeEvent("orders"));
        _bus.PublishDataChanged(new RealtimeEvent("dashboard"));

        _log.LogInformation("Order placed (Pending) UserId={UserId} OrderNumber={OrderNumber} Amount={Amount} PayOnline={Online} Gateway={Gw} GwOrderId={GwOrderId} Redirect={Redirect}",
            userId, order.OrderNumber, total, req.PayOnline, gw?.GatewayName, gw?.GatewayOrderId, gw?.RedirectUrl);

        return new PlaceOrderResult(order.Id, order.OrderNumber, total,
            gw?.GatewayName, gw?.GatewayOrderId, gw?.GatewayKey, req.PayOnline, gw?.RedirectUrl);
    }

    public async Task<OrderReceipt?> ConfirmPaymentAsync(PaymentCallback cb, GatewayPaymentInstrument? instrument = null)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Invoice)
            .FirstOrDefaultAsync(o => o.OrderNumber == cb.OrderNumber);
        if (order == null) return null;

        // Idempotent — a second callback for an already-paid order just returns the receipt.
        // Deliberately does NOT backfill the payment mode: we reach here BEFORE the signature check,
        // and `cb` is model-bound from a request body on POST /api/checkout/confirm with no ownership
        // check, so honouring it would let any caller rewrite the mode on a paid order's invoice.
        // Late-arriving modes are backfilled only on gateway-authenticated channels — the Razorpay
        // webhook (HMAC-verified), the Easebuzz callback/webhook (hash-verified) and the sync task.
        if (order.PaymentStatus == PaymentStatus.Success) return Build(order);

        var payment = order.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        var intentId = cb.GatewayOrderId ?? payment?.GatewayOrderId ?? string.Empty;
        var verified = cb.Success && _gateway.VerifyPayment(intentId, cb.GatewayPaymentId, cb.GatewaySignature);

        if (!verified)
        {
            if (payment != null) payment.Status = PaymentStatus.Failed;
            order.PaymentStatus = PaymentStatus.Failed;   // order stays Pending → payment can be retried
            await _db.SaveChangesAsync();
            _log.LogWarning("Payment verification FAILED OrderNumber={OrderNumber} GwOrderId={GwOrderId} GwPaymentId={GwPaymentId} ClientSuccess={Success}",
                order.OrderNumber, intentId, cb.GatewayPaymentId, cb.Success);
            return Build(order);
        }

        // Past the signature check — safe to settle. The instrument is resolved from the gateway
        // inside CompleteOrderAsync when the caller didn't supply a trusted one.
        await CompleteOrderAsync(order, payment, cb.GatewayPaymentId, cb.GatewaySignature, source: "popup", instrument: instrument);
        return Build(order);
    }

    public async Task<OrderReceipt?> ConfirmFromWebhookAsync(string gatewayOrderId, string? gatewayPaymentId,
        GatewayPaymentInstrument? instrument = null)
    {
        if (string.IsNullOrWhiteSpace(gatewayOrderId)) return null;

        // Locate the local order via the Payment row that holds the matching gateway order id.
        // The webhook itself has already been signature-verified upstream against the Webhook
        // Secret, so we don't re-run the popup-style HMAC here.
        var payment = await _db.Payments
            .Include(p => p.Order).ThenInclude(o => o.Items)
            .Include(p => p.Order).ThenInclude(o => o.Payments)
            .Include(p => p.Order).ThenInclude(o => o.Invoice)
            .FirstOrDefaultAsync(p => p.GatewayOrderId == gatewayOrderId);

        if (payment == null)
        {
            _log.LogWarning("Webhook reconcile NO MATCH GwOrderId={GwOrderId} GwPaymentId={GwPaymentId}", gatewayOrderId, gatewayPaymentId);
            return null;
        }

        var order = payment.Order;

        // Idempotent — popup verify path may have completed this already. The webhook still carries
        // the payment mode, so backfill it if the popup path settled without one.
        if (order.PaymentStatus == PaymentStatus.Success)
        {
            _log.LogInformation("Webhook reconcile no-op (already paid) OrderNumber={OrderNumber}", order.OrderNumber);
            await BackfillPaymentModeAsync(order, instrument, gatewayPaymentId);
            return Build(order);
        }

        await CompleteOrderAsync(order, payment, gatewayPaymentId, gatewaySignature: null, source: "webhook", instrument: instrument);
        return Build(order);
    }

    // Single source of truth for the "payment is good, settle the order" side effects.
    // Both the popup verify path (signature checked) and the webhook path (X-Razorpay-Signature
    // checked upstream) share this — so we can't get half-confirmed orders from one path that
    // wouldn't have happened on the other.
    private async Task CompleteOrderAsync(Order order, Payment? payment, string? gatewayPaymentId, string? gatewaySignature, string source,
        GatewayPaymentInstrument? instrument = null)
    {
        // What the customer actually paid with (UPI / Credit Card / Net Banking / …). Callers that
        // already have it from the gateway payload pass it in; the rest resolve it here. Resolved
        // BEFORE the save below so the invoice raised further down snapshots the same value.
        instrument ??= await ResolveInstrumentAsync(order, gatewayPaymentId);

        if (payment == null)
        {
            payment = new Payment { OrderId = order.Id, Amount = order.TotalAmount, PaymentMode = order.PaymentMode ?? PaymentMode.Razorpay };
            order.Payments.Add(payment);
        }
        payment.Status = PaymentStatus.Success;
        payment.GatewayName ??= _gateway.Name;
        payment.GatewayPaymentId = gatewayPaymentId ?? payment.GatewayPaymentId ?? $"pay_{Guid.NewGuid():N}";
        payment.GatewaySignature = gatewaySignature ?? payment.GatewaySignature;
        payment.PaidAt = DateTime.UtcNow;
        if (instrument != null)
        {
            payment.GatewayPaymentMode = ClipTo(instrument.Mode, 40);
            payment.GatewayPaymentModeDetail = ClipTo(instrument.Detail, 120);
            order.GatewayPaymentMode = payment.GatewayPaymentMode;
        }

        order.PaymentStatus = PaymentStatus.Success;
        order.Status = OrderStatus.Confirmed;
        order.ConfirmedAt = DateTime.UtcNow;
        order.ActivatedAt = DateTime.UtcNow;

        if (order.CouponId.HasValue)
        {
            var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == order.CouponId.Value);
            if (coupon != null) coupon.TotalUsed++;
        }

        // NOTE: the invoice is NOT created here. It is raised further down by
        // IInvoiceService.EnsureForOrderAsync once the paid order is saved, so website orders get the
        // same RIO-INV-yyyyMM-#### number and the same full customer/company/line snapshot as every
        // other source. Creating a thin one here used to win the race and leave that snapshot empty.

        // Grant course access. One enrollment per order item, idempotent.
        if (order.UserId.HasValue)
        {
            var modeIds = order.Items.Where(i => i.ProductModeId.HasValue).Select(i => i.ProductModeId!.Value).ToList();
            var modeTypes = await _db.ProductModes.Where(m => modeIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.ModeType);

            foreach (var item in order.Items)
            {
                item.IsActivated = true;
                item.ActivatedAt = DateTime.UtcNow;

                var exists = await _db.Enrollments.AnyAsync(e => e.UserId == order.UserId.Value && e.OrderItemId == item.Id);
                if (exists) continue;

                LectureMode? mode = item.ProductModeId.HasValue && modeTypes.TryGetValue(item.ProductModeId.Value, out var mt)
                    ? mt : null;
                _db.Enrollments.Add(new Enrollment
                {
                    UserId = order.UserId.Value,
                    ProductId = item.ProductId,
                    OrderItemId = item.Id,
                    Mode = mode,
                    IsActive = true
                });

                var prod = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (prod != null) prod.TotalOrders++;
            }
        }

        // Record affiliate commission once per order (the unique index on OrderId backs this up).
        if (order.AffiliateId.HasValue && !await _db.AffiliateReferrals.AnyAsync(r => r.OrderId == order.Id))
        {
            var affiliate = await _db.Affiliates.FirstOrDefaultAsync(a => a.Id == order.AffiliateId.Value);
            if (affiliate != null)
            {
                var commission = affiliate.CommissionType == SharingType.Percentage
                    ? Math.Round(order.TotalAmount * affiliate.CommissionValue / 100m, 2)
                    : affiliate.CommissionValue;
                _db.AffiliateReferrals.Add(new AffiliateReferral
                {
                    AffiliateId = affiliate.Id,
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    OrderAmount = order.TotalAmount,
                    Commission = commission,
                    IsPaid = false
                });
                affiliate.TotalReferrals++;
                affiliate.TotalEarned += commission;
            }
        }

        await _db.SaveChangesAsync();
        await _notify.SendOrderConfirmationAsync(order);

        // ── Serial-key generation ────────────────────────────────────────────────
        // For each order item whose product has serial-key config attached, insert a
        // SerialKeyRecord in Pending state. The SerialKeyRetry scheduled task (≤60s)
        // calls the provider, persists the result, and notifies the customer.
        // Idempotent — safe if CompleteOrderAsync runs twice (popup-callback + webhook race).
        try
        {
            var enqueued = await _serialKeys.EnqueueForOrderAsync(order.Id);
            if (enqueued > 0)
                _log.LogInformation("SerialKey enqueued count={Count} orderNumber={OrderNumber}",
                    enqueued, order.OrderNumber);
        }
        catch (Exception ex)
        {
            // Never block the payment-success flow on a serial-key issue.
            _log.LogError(ex, "SerialKey enqueue threw on order completion orderNumber={OrderNumber}", order.OrderNumber);
        }

        // ── Invoice generation ──────────────────────────────────────────────────
        // Idempotent: re-running on the same paid order returns the existing invoice.
        try
        {
            var (newId, existingId, err) = await _invoices.EnsureForOrderAsync(order.Id, actorName: "checkout");
            if (newId.HasValue)
                _log.LogInformation("Invoice generated id={Id} orderNumber={OrderNumber}", newId, order.OrderNumber);
            else if (!string.IsNullOrEmpty(err) && existingId == null)
                _log.LogWarning("Invoice not generated orderNumber={OrderNumber} reason={Reason}", order.OrderNumber, err);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Invoice generation threw on order completion orderNumber={OrderNumber}", order.OrderNumber);
        }

        // ── Faculty share ledger ────────────────────────────────────────────────
        // Website orders are the main revenue path, so this is where most faculty earnings originate.
        // Snapshotting now (rather than recomputing at payout time) is what stops a later rate change
        // from restating what a faculty already earned. Idempotent per order; best-effort — a share
        // row can be replayed, a confirmed payment cannot be un-confirmed.
        try
        {
            await _facultyShares.RecordOrderFacultyShareAsync(order.Id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Faculty share ledger write threw on order completion orderNumber={OrderNumber}", order.OrderNumber);
        }

        // ── Cart cleanup on confirmed payment ──────────────────────────────────
        // For online orders the cart was intentionally NOT cleared at placement (so a failed or
        // abandoned payment leaves it intact for re-purchase — issue #2). Now that payment is
        // verified, remove the purchased cart so it isn't bought twice. Idempotent + best-effort.
        if (order.UserId.HasValue)
        {
            try
            {
                var cartRows = await _db.CartItems.Where(c => c.UserId == order.UserId.Value).ToListAsync();
                if (cartRows.Count > 0)
                {
                    _db.CartItems.RemoveRange(cartRows);
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Cart cleanup on payment success threw orderNumber={OrderNumber}", order.OrderNumber);
            }
        }

        await _center.NotifyAsync(AdminNotificationType.PaymentSuccess, NotificationSeverity.Success,
            "Payment received", $"₹{order.TotalAmount:N0} paid for order #{order.OrderNumber}.", $"/admin/orders/{order.Id}", order.Id.ToString());
        _bus.PublishDataChanged(new RealtimeEvent("orders"));
        _bus.PublishDataChanged(new RealtimeEvent("dashboard"));

        _log.LogInformation("Payment SUCCESS Source={Source} OrderNumber={OrderNumber} UserId={UserId} Amount={Amount} GwPaymentId={GwPaymentId} PaymentMode={PaymentMode} Invoice={Invoice}",
            source, order.OrderNumber, order.UserId, order.TotalAmount, payment.GatewayPaymentId, order.GatewayPaymentMode ?? "unknown", order.Invoice?.InvoiceNumber);
    }

    // ── Gateway payment-mode capture ─────────────────────────────────────────
    // The order's PaymentMode says which gateway we sent the customer to; GatewayPaymentMode says
    // what they actually used there. Only the gateway knows the latter, so we either take it from
    // the callback payload (Easebuzz form, Razorpay webhook entity) or ask the gateway for it.

    /// <summary>Asks the order's gateway what instrument a payment was made with. Best-effort:
    /// any failure returns null and the order simply keeps an unknown payment mode — a settled
    /// payment is never held up or rolled back over a display label.</summary>
    private async Task<GatewayPaymentInstrument?> ResolveInstrumentAsync(Order order, string? gatewayPaymentId)
    {
        if (string.IsNullOrWhiteSpace(gatewayPaymentId)) return null;

        try
        {
            var gateway = _gateways.TryGet(GatewayNameFor(order.PaymentMode ?? PaymentMode.Razorpay));
            if (gateway == null) return null;
            return await gateway.GetPaymentInstrumentAsync(gatewayPaymentId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Payment mode lookup failed OrderNumber={OrderNumber} GwPaymentId={GwPaymentId}",
                order.OrderNumber, gatewayPaymentId);
            return null;
        }
    }

    /// <summary>Fills in the payment mode on an order that settled without one — the popup path can
    /// miss it (lookup timeout, gateway hiccup) and the webhook or the sync task arrives later with
    /// the answer. Never overwrites a mode we already captured; no-ops when there's nothing to add.
    ///
    /// <para>Callers are reconciliation paths whose already-paid branch was previously a pure read, so
    /// this stays entirely best-effort: it must never turn a verified webhook into a 500 (which would
    /// make the gateway retry) over a display label. Every failure is swallowed and logged.</para>
    /// </summary>
    private async Task BackfillPaymentModeAsync(Order order, GatewayPaymentInstrument? instrument, string? gatewayPaymentId)
    {
        if (!string.IsNullOrWhiteSpace(order.GatewayPaymentMode)) return;

        try
        {
            instrument ??= await ResolveInstrumentAsync(order, gatewayPaymentId);
            if (instrument == null) return;

            order.GatewayPaymentMode = ClipTo(instrument.Mode, 40);

            var payment = order.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            if (payment != null && string.IsNullOrWhiteSpace(payment.GatewayPaymentMode))
            {
                payment.GatewayPaymentMode = order.GatewayPaymentMode;
                payment.GatewayPaymentModeDetail = ClipTo(instrument.Detail, 120);
            }

            // The invoice snapshot was frozen before we knew the mode — fill that gap too, so the
            // printed invoice and the order agree.
            var invoice = order.Invoice ?? await _db.Set<Invoice>()
                .FirstOrDefaultAsync(i => i.OrderId == order.Id && i.Status == InvoiceStatus.Active);
            if (invoice != null && string.IsNullOrWhiteSpace(invoice.GatewayPaymentMode))
                invoice.GatewayPaymentMode = order.GatewayPaymentMode;

            await _db.SaveChangesAsync();
            _log.LogInformation("Payment mode backfilled OrderNumber={OrderNumber} Mode={Mode}", order.OrderNumber, order.GatewayPaymentMode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Payment mode backfill failed OrderNumber={OrderNumber}", order.OrderNumber);
        }
    }

    /// <summary>Guards the DB column widths — gateway free-text (wallet names, VPAs) is unbounded.</summary>
    private static string? ClipTo(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    public async Task<PlaceOrderResult?> RetryPaymentAsync(Guid userId, string orderNumber, string? originBaseUrl = null)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == userId);
        if (order == null) return null;
        return await InitiateGatewayAsync(order, originBaseUrl);
    }

    /// <summary>
    /// Owner-agnostic gateway initiation for an existing Pending order — used by admin Counter /
    /// franchisee order creation, where the caller is staff acting on behalf of the customer (so the
    /// order's UserId is NOT the caller's). MUST be role-gated at the controller. Mints a fresh
    /// gateway intent and returns the redirect/popup parameters.
    /// </summary>
    public async Task<PlaceOrderResult?> InitiateGatewayForOrderAsync(string orderNumber, string? originBaseUrl = null)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        if (order == null) return null;
        return await InitiateGatewayAsync(order, originBaseUrl);
    }

    /// <summary>
    /// The Easebuzz surl/furl. Absolute or nothing: Easebuzz validates these and rejects a bare
    /// path, so an origin-less caller gets an empty string, which fails loudly at the gateway
    /// instead of producing a link that silently cannot call us back.
    /// </summary>
    private static string BuildCallbackUrl(string? originBaseUrl)
    {
        var origin = (originBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        return origin.Length == 0 ? string.Empty : $"{origin}/api/payments/easebuzz/callback";
    }

    /// <summary>Shared gateway-intent logic for retry + admin-initiate. Returns null if already paid.</summary>
    private async Task<PlaceOrderResult?> InitiateGatewayAsync(Order order, string? originBaseUrl = null)
    {
        if (order.PaymentStatus == PaymentStatus.Success) return null; // already paid — nothing to do

        // Pick the gateway the order was placed with (so a Razorpay order retries on Razorpay,
        // an Easebuzz order retries on Easebuzz).
        var mode = order.PaymentMode ?? PaymentMode.Razorpay;
        var gateway = _gateways.Get(GatewayNameFor(mode));

        var productInfo = string.Join(", ", order.Items.Select(i => i.ProductTitle).Where(t => !string.IsNullOrWhiteSpace(t)).Take(3));
        if (string.IsNullOrEmpty(productInfo)) productInfo = $"Order {order.OrderNumber}";

        var context = new GatewayCreateContext(
            CustomerName: order.StudentName ?? string.Empty,
            CustomerEmail: order.StudentEmail ?? string.Empty,
            CustomerPhone: order.StudentPhone ?? string.Empty,
            ProductInfo: productInfo,
            // Retry happens via the same /callback route the original placement used — but it must
            // be sent ABSOLUTE. This used to pass the bare path, and Easebuzz rejects a relative
            // surl/furl outright ("Invalid value for surl.Invalid value for furl."), so retry and
            // admin-initiate could never succeed. The caller supplies the request's own origin, the
            // same way the placement path does.
            ReturnUrl: BuildCallbackUrl(originBaseUrl));

        // Fresh gateway order — the previous one may have expired or been consumed.
        var gw = await gateway.CreateOrderAsync(order.OrderNumber, order.TotalAmount, "INR", context);

        var payment = order.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (payment == null)
        {
            payment = new Payment
            {
                Amount = order.TotalAmount,
                PaymentMode = mode,
                Status = PaymentStatus.Pending,
                GatewayName = gw.GatewayName,
                GatewayOrderId = gw.GatewayOrderId
            };
            order.Payments.Add(payment);
        }
        else
        {
            // Reset the existing payment row onto the new gateway intent so retries don't
            // pile up orphaned Payment records.
            payment.GatewayName = gw.GatewayName;
            payment.GatewayOrderId = gw.GatewayOrderId;
            payment.GatewayPaymentId = null;
            payment.GatewaySignature = null;
            payment.Status = PaymentStatus.Pending;
        }

        order.PaymentStatus = PaymentStatus.Pending;   // clears any prior Failed mark
        await _db.SaveChangesAsync();

        _log.LogInformation("Order payment intent initiated OrderNumber={OrderNumber} Gateway={Gateway} NewGwOrderId={GwOrderId} Amount={Amount} Redirect={Redirect}",
            order.OrderNumber, gw.GatewayName, gw.GatewayOrderId, order.TotalAmount, gw.RedirectUrl);

        return new PlaceOrderResult(order.Id, order.OrderNumber, order.TotalAmount,
            gw.GatewayName, gw.GatewayOrderId, gw.GatewayKey, true, gw.RedirectUrl);
    }

    public async Task<OrderReceipt?> ConfirmByOrderNumberAsync(string orderNumber, string? gatewayPaymentId, string source,
        GatewayPaymentInstrument? instrument = null)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return null;

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .Include(o => o.Invoice)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        if (order == null)
        {
            _log.LogWarning("{Source} confirm NO MATCH OrderNumber={OrderNumber} GwPaymentId={GwPaymentId}", source, orderNumber, gatewayPaymentId);
            return null;
        }

        // Idempotent — popup verify path or webhook may have completed this already. A later
        // callback/webhook can still be the first one carrying the payment mode.
        if (order.PaymentStatus == PaymentStatus.Success)
        {
            _log.LogInformation("{Source} confirm no-op (already paid) OrderNumber={OrderNumber}", source, order.OrderNumber);
            await BackfillPaymentModeAsync(order, instrument, gatewayPaymentId);
            return Build(order);
        }

        var payment = order.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        await CompleteOrderAsync(order, payment, gatewayPaymentId, gatewaySignature: null, source: source, instrument: instrument);
        return Build(order);
    }

    public async Task<OrderReceipt?> GetReceiptAsync(Guid userId, string orderNumber)
    {
        // The UserId predicate is the ownership guard: a customer passing someone else's order number
        // gets null, not a receipt. Shipment details are loaded through this same query for exactly
        // that reason — they can never be reached without passing the ownership test.
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Invoice)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == userId);
        if (order == null) return null;

        var receipt = Build(order);
        receipt.Shipment = await LoadShipmentAsync(order.Id);
        return receipt;
    }

    /// <summary>
    /// Despatch details for the customer, with the tracking URL resolved centrally from the courier
    /// — never composed here, so admin and customer always agree on where a courier is tracked.
    /// Returns null when no courier has been assigned yet.
    /// </summary>
    private async Task<OrderShipmentInfo?> LoadShipmentAsync(Guid orderId)
    {
        var sh = await _db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == orderId)
            .OrderByDescending(s => s.DispatchedAt ?? s.CreatedAt)
            .FirstOrDefaultAsync();
        if (sh == null || string.IsNullOrWhiteSpace(sh.Courier)) return null;

        // A courier with no tracking page yields nulls for both fields, which is what collapses the
        // tracking block in the UI. Legacy free-text couriers (e.g. "DTDC") resolve to no URL and
        // still display their number — readable, just not linkable.
        return new OrderShipmentInfo
        {
            Courier = sh.Courier!,
            TrackingNumber = Couriers.RequiresTracking(sh.Courier) ? sh.TrackingNumber : null,
            TrackingUrl = Couriers.TrackingUrl(sh.Courier),
            Status = sh.Status.ToString(),
            DispatchedAt = sh.DispatchedAt,
            DeliveredAt = sh.DeliveredAt,
        };
    }

    // ── helpers ──

    private Task<List<CartItem>> LoadCartAsync(Guid userId) =>
        _db.CartItems.Where(c => c.UserId == userId)
            .Include(c => c.Product).ThenInclude(p => p.PrimaryFaculty)
            .Include(c => c.Product).ThenInclude(p => p.OptionGroups).ThenInclude(g => g.Items)
            .Include(c => c.ProductMode)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

    // Resolve a cart item's selected purchase-option IDs into (add-on sum, snapshot JSON).
    // SERVER-SIDE lookup only — never trust any client-sent amount. Validates each id belongs to
    // an active option group of THIS product; silently ignores ids that don't (tamper-safe). If two
    // selected ids belong to the same group, only the first is honoured (one pick per group).
    private static (decimal addOn, string? snapshot) ResolveOptionAddOns(CartItem c)
    {
        if (string.IsNullOrWhiteSpace(c.SelectedOptionIdsJson)) return (0m, null);
        List<Guid> ids;
        try { ids = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(c.SelectedOptionIdsJson!) ?? new(); }
        catch (System.Text.Json.JsonException) { return (0m, null); }
        if (ids.Count == 0) return (0m, null);

        decimal sum = 0m;
        var seenGroups = new HashSet<Guid>();
        var snap = new List<object>();
        foreach (var g in c.Product.OptionGroups.Where(g => g.IsActive))
        {
            var picked = g.Items.FirstOrDefault(i => i.IsActive && ids.Contains(i.Id));
            if (picked == null) continue;
            if (!seenGroups.Add(g.Id)) continue;  // one per group
            sum += picked.PriceAddOn;
            snap.Add(new { groupName = g.Name, optionName = picked.Name, addOn = picked.PriceAddOn });
        }
        var json = snap.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(snap);
        return (sum, json);
    }

    private Task<List<CheckoutAttributeView>> LoadCheckoutAttributesAsync() =>
        _db.CheckoutAttributes.Where(a => a.IsActive).OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .Select(a => new CheckoutAttributeView(
                a.Id, a.Name, a.TextPrompt, a.ControlType.ToString(), a.IsRequired,
                a.Values.OrderBy(v => v.DisplayOrder).Select(v => new CheckoutAttributeValueView(
                    v.Id, v.Name, v.PriceAdjustment, v.PriceAdjustmentUsePercentage, v.IsPreSelected)).ToList()))
            .ToListAsync();

    // Resolves selected checkout attributes into a price impact + snapshot JSON (server-side, percentage applies to baseAmount).
    private async Task<(decimal amount, string? json)> ResolveCheckoutAttributesAsync(
        decimal baseAmount, IReadOnlyList<CheckoutAttributeSelection>? selections)
    {
        if (selections == null || selections.Count == 0) return (0m, null);
        var ids = selections.Select(s => s.AttributeId).Distinct().ToList();
        var attrs = await _db.CheckoutAttributes.Where(a => a.IsActive && ids.Contains(a.Id))
            .Include(a => a.Values).ToListAsync();

        var resolved = new List<SelectedAttribute>();
        decimal total = 0m;
        foreach (var sel in selections)
        {
            var a = attrs.FirstOrDefault(x => x.Id == sel.AttributeId);
            if (a == null) continue;
            if (sel.ValueId is Guid vid)
            {
                var v = a.Values.FirstOrDefault(x => x.Id == vid);
                if (v == null) continue;
                var adj = v.PriceAdjustmentUsePercentage ? Math.Round(baseAmount * v.PriceAdjustment / 100m, 2) : v.PriceAdjustment;
                total += adj;
                resolved.Add(new SelectedAttribute(a.Name, v.Name, null, adj));
            }
            else if (!string.IsNullOrWhiteSpace(sel.TextValue))
            {
                resolved.Add(new SelectedAttribute(a.Name, null, sel.TextValue.Trim(), 0m));
            }
        }
        if (resolved.Count == 0) return (0m, null);
        return (total, JsonSerializer.Serialize(resolved));
    }

    /// <summary>Resolves the referring affiliate id from a code (active, never the buyer).</summary>
    private async Task<Guid?> ResolveAffiliateIdAsync(string? code, Guid? buyerUserId)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpper();
        var aff = await _db.Affiliates.FirstOrDefaultAsync(a => a.Code.ToUpper() == c && a.IsActive);
        return aff == null || aff.UserId == buyerUserId ? null : aff.Id;
    }

    private async Task<(decimal discount, string? code, string? message)> ResolveCouponAsync(string? couponCode, decimal subtotal, Guid? affiliateId)
    {
        // Franchise-only codes are invisible here — they're redeemed on /franchise/order, where the
        // discount stacks on top of the franchisee's share rather than a customer's cart.
        // 1) Affiliate-linked coupon auto-applies when the buyer arrived via that affiliate's link.
        if (affiliateId.HasValue)
        {
            var linked = await _db.Coupons.FirstOrDefaultAsync(c => c.AffiliateId == affiliateId && c.IsActive && c.IsCustomerApplicable);
            if (linked != null)
            {
                var (ok, discount, _) = ValidateCoupon(linked, subtotal);
                if (ok) return (discount, linked.Code, $"Referral offer \"{linked.Code}\" applied 🎉");
                // linked-but-invalid (e.g. min order not met) → fall through to a typed code
            }
        }

        // 2) A manually-entered coupon.
        if (string.IsNullOrWhiteSpace(couponCode)) return (0, null, null);
        var code = couponCode.Trim().ToUpper();
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == code && c.IsActive && c.IsCustomerApplicable);
        if (coupon == null) return (0, null, "Invalid coupon code.");
        if (coupon.AffiliateId.HasValue && coupon.AffiliateId != affiliateId)
            return (0, null, "This code only works through its referral link.");   // affiliate-exclusive

        var res = ValidateCoupon(coupon, subtotal);
        return res.ok ? (res.discount, coupon.Code, $"Coupon \"{coupon.Code}\" applied 🎉") : (0, null, res.message);
    }

    private static (bool ok, decimal discount, string? message) ValidateCoupon(Coupon coupon, decimal subtotal)
    {
        var now = DateTime.UtcNow;
        if ((coupon.StartsAt.HasValue && coupon.StartsAt > now) || (coupon.ExpiresAt.HasValue && coupon.ExpiresAt < now))
            return (false, 0, "This coupon has expired.");
        if (subtotal < coupon.MinOrder)
            return (false, 0, $"Minimum order of ₹{coupon.MinOrder:N0} required for this coupon.");

        var discount = coupon.CouponType == SharingType.Percentage ? subtotal * coupon.Value / 100m : coupon.Value;
        if (coupon.MaxDiscount.HasValue && discount > coupon.MaxDiscount.Value) discount = coupon.MaxDiscount.Value;
        discount = Math.Round(Math.Min(discount, subtotal), 2);
        return (true, discount, null);
    }

    /// <summary>Splits the GST-inclusive total into CGST+SGST (intra-state) or IGST (inter-state).</summary>
    private static (decimal cgst, decimal sgst, decimal igst) SplitGst(decimal grossTotal, string? buyerState)
    {
        var gst = Math.Round(grossTotal * 18m / 118m, 2);
        var intraState = string.Equals(buyerState?.Trim(), SellerState, StringComparison.OrdinalIgnoreCase);
        if (intraState)
        {
            var half = Math.Round(gst / 2m, 2);
            return (half, gst - half, 0m);   // absorb rounding into SGST so the parts always sum to gst
        }
        return (0m, 0m, gst);
    }

    private async Task<string> GenerateOrderNumberAsync()
    {
        var last = await _db.Orders.Where(o => o.OrderNumber.StartsWith("RIO"))
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
        var next = 1043;
        if (last != null && int.TryParse(last.OrderNumber.Split('-').Last(), out var n)) next = n + 1;
        return $"RIO-{next}";
    }

    private static OrderReceipt Build(Order o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        IsFranchiseOrder = o.Source == OrderSource.Franchisee || o.FranchiseId != null,
        CreatedAt = o.CreatedAt,
        StudentName = o.StudentName,
        StudentEmail = o.StudentEmail,
        StudentPhone = o.StudentPhone,
        Status = o.Status,
        PaymentStatus = o.PaymentStatus,
        PaymentMode = o.PaymentMode,
        GatewayPaymentMode = o.GatewayPaymentMode,
        Subtotal = o.Subtotal,
        Discount = o.DiscountAmount,
        CgstAmount = o.CgstAmount,
        SgstAmount = o.SgstAmount,
        IgstAmount = o.IgstAmount,
        GstAmount = o.GstAmount,
        Total = o.TotalAmount,
        CouponCode = o.CouponCode,
        InvoiceNumber = o.Invoice?.InvoiceNumber,
        BillingName = o.BillingName,
        BillingAddress = o.BillingAddress,
        BillingCity = o.BillingCity,
        BillingState = o.BillingState,
        BillingPincode = o.BillingPincode,
        GstNumber = o.GstNumber,
        CheckoutAttributesAmount = o.CheckoutAttributesAmount,
        CheckoutAttributes = CartService.AttributeSummary(o.CheckoutAttributesJson),
        Items = o.Items.Select(i => new OrderReceiptLine(i.ProductTitle, i.ModeName, i.Quantity, i.UnitPrice, i.LineTotal,
            CartService.AttributeSummary(i.SelectedAttributesJson))).ToList()
    };
}
