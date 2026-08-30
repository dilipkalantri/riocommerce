using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.Checkout;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

/// <summary>
/// REST surface that drives the entire Razorpay payment lifecycle from the browser.
///
/// Why REST (not Blazor JSInvokable):
///   • The verification step can complete even if the Blazor SignalR circuit dies.
///   • The popup's handler can POST → wait for a JSON {success, redirectUrl} → run
///     window.location.replace(redirectUrl). No spinners, no polling, no "awaiting
///     payment" screens.
///   • Reproducible via curl/Postman for debugging.
///
/// Cookie auth is the default scheme on this app; admin endpoints opt into JWT — the
/// public checkout pages this controller serves use cookies, so [Authorize] without a
/// scheme is correct here.
/// </summary>
[ApiController]
[Authorize]
[Route("api/payments/razorpay")]
public class RazorpayPaymentController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IIntegrationSettingsService _settings;
    private readonly ILogger<RazorpayPaymentController> _log;

    public RazorpayPaymentController(ICheckoutService checkout, IIntegrationSettingsService settings, ILogger<RazorpayPaymentController> log)
    {
        _checkout = checkout;
        _settings = settings;
        _log = log;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class CreateOrderResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? OrderNumber { get; set; }     // our local order id (RIO-1053)
        public string? OrderId { get; set; }         // razorpay order id (order_xxx)
        public long Amount { get; set; }             // paise
        public string Currency { get; set; } = "INR";
        public string? Key { get; set; }             // razorpay public KeyId
    }

    public class RetryRequest
    {
        public string LocalOrderId { get; set; } = string.Empty; // our RIO-… order number
    }

    public class VerifyRequest
    {
        // Names match what Razorpay's checkout.js hands back in resp.razorpay_*.
        public string? razorpay_order_id { get; set; }
        public string? razorpay_payment_id { get; set; }
        public string? razorpay_signature { get; set; }
        public string LocalOrderId { get; set; } = string.Empty;
    }

    public class VerifyResponse
    {
        public bool Success { get; set; }
        public string RedirectUrl { get; set; } = string.Empty;
        public string? Reason { get; set; }          // diagnostic only — UI shouldn't read this
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────

    /// <summary>
    /// New-order path: creates a Pending order from the cart, mints a real Razorpay
    /// order, returns the public handoff so the browser can open the popup.
    /// </summary>
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CheckoutRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new CreateOrderResponse { Success = false, Error = "Not signed in." });
        if (req == null) return BadRequest(new CreateOrderResponse { Success = false, Error = "Missing checkout body." });

        // The new flow is always online-paid via Razorpay; force the mode regardless of
        // what the browser sent so a tampered request can't slip cash through here.
        req.PayOnline = true;
        req.PaymentMode = PaymentMode.Razorpay;

        _log.LogInformation("Razorpay create-order BEGIN UserId={UserId} IP={IP} UA={UA} Amount-hint={Amount}",
            userId, ClientIp, UserAgent, req.CouponCode);

        try
        {
            var result = await _checkout.PlaceOrderAsync(userId, req);
            if (result == null || !result.RequiresPayment || string.IsNullOrEmpty(result.GatewayOrderId))
            {
                _log.LogWarning("Razorpay create-order produced no gateway intent UserId={UserId} OrderNumber={OrderNumber}", userId, result?.OrderNumber);
                return BadRequest(new CreateOrderResponse { Success = false, Error = "Could not create the payment order." });
            }

            _log.LogInformation("Razorpay create-order OK UserId={UserId} OrderNumber={OrderNumber} GwOrderId={GwId} Amount={Amount} IP={IP}",
                userId, result.OrderNumber, result.GatewayOrderId, result.Amount, ClientIp);

            return Ok(new CreateOrderResponse
            {
                Success = true,
                OrderNumber = result.OrderNumber,
                OrderId = result.GatewayOrderId,
                Amount = (long)Math.Round(result.Amount * 100m, MidpointRounding.AwayFromZero),
                Currency = "INR",
                Key = result.GatewayKey
            });
        }
        catch (InvalidOperationException ex)
        {
            // Business rejections (empty cart, missing referral source, Razorpay 4xx, etc.) —
            // surface the actual reason to the browser so the failure page can show it.
            _log.LogWarning(ex, "Razorpay create-order REJECTED UserId={UserId} IP={IP}", userId, ClientIp);
            return BadRequest(new CreateOrderResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Razorpay create-order UNEXPECTED UserId={UserId} IP={IP}", userId, ClientIp);
            return StatusCode(500, new CreateOrderResponse { Success = false, Error = "Unexpected error while creating the payment order." });
        }
    }

    /// <summary>
    /// Retry path: the local order already exists (Pending). Mint a fresh Razorpay
    /// order against it so a previously-attempted/expired Razorpay intent doesn't
    /// block the user.
    /// </summary>
    [HttpPost("retry-order")]
    public async Task<IActionResult> RetryOrder([FromBody] RetryRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new CreateOrderResponse { Success = false, Error = "Not signed in." });
        if (string.IsNullOrWhiteSpace(req?.LocalOrderId)) return BadRequest(new CreateOrderResponse { Success = false, Error = "Missing localOrderId." });

        _log.LogInformation("Razorpay retry-order BEGIN UserId={UserId} OrderNumber={OrderNumber} IP={IP} UA={UA}",
            userId, req.LocalOrderId, ClientIp, UserAgent);

        try
        {
            var result = await _checkout.RetryPaymentAsync(userId, req.LocalOrderId);
            if (result == null)
            {
                _log.LogWarning("Razorpay retry-order not allowed UserId={UserId} OrderNumber={OrderNumber}", userId, req.LocalOrderId);
                return NotFound(new CreateOrderResponse { Success = false, Error = "Order not found or already paid." });
            }

            _log.LogInformation("Razorpay retry-order OK UserId={UserId} OrderNumber={OrderNumber} NewGwOrderId={GwId} Amount={Amount}",
                userId, result.OrderNumber, result.GatewayOrderId, result.Amount);

            return Ok(new CreateOrderResponse
            {
                Success = true,
                OrderNumber = result.OrderNumber,
                OrderId = result.GatewayOrderId,
                Amount = (long)Math.Round(result.Amount * 100m, MidpointRounding.AwayFromZero),
                Currency = "INR",
                Key = result.GatewayKey
            });
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "Razorpay retry-order REJECTED UserId={UserId} OrderNumber={OrderNumber}", userId, req.LocalOrderId);
            return BadRequest(new CreateOrderResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Razorpay retry-order UNEXPECTED UserId={UserId} OrderNumber={OrderNumber}", userId, req.LocalOrderId);
            return StatusCode(500, new CreateOrderResponse { Success = false, Error = "Unexpected error while retrying the payment." });
        }
    }

    /// <summary>
    /// Admin/franchise path: initiate a Razorpay intent for an EXISTING Pending order created at the
    /// counter on a customer's behalf (so the order isn't the caller's own). Role-gated to staff.
    /// </summary>
    [HttpPost("admin-initiate")]
    [Authorize(Roles = "super_admin,admin,operations,franchise_admin,backend_user")]
    public async Task<IActionResult> AdminInitiate([FromBody] RetryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.LocalOrderId))
            return BadRequest(new CreateOrderResponse { Success = false, Error = "Missing localOrderId." });
        try
        {
            var result = await _checkout.InitiateGatewayForOrderAsync(req.LocalOrderId);
            if (result == null || string.IsNullOrEmpty(result.GatewayOrderId))
                return NotFound(new CreateOrderResponse { Success = false, Error = "Order not found or already paid." });

            return Ok(new CreateOrderResponse
            {
                Success = true,
                OrderNumber = result.OrderNumber,
                OrderId = result.GatewayOrderId,
                Amount = (long)Math.Round(result.Amount * 100m, MidpointRounding.AwayFromZero),
                Currency = "INR",
                Key = result.GatewayKey
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CreateOrderResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Razorpay admin-initiate UNEXPECTED OrderNumber={OrderNumber}", req.LocalOrderId);
            return StatusCode(500, new CreateOrderResponse { Success = false, Error = "Unexpected error initiating payment." });
        }
    }

    /// <summary>
    /// Verification: ALL state mutation (PaymentStatus=Success, OrderStatus=Confirmed,
    /// invoice, enrollment) lives inside ConfirmPaymentAsync gated on signature verify.
    /// Frontend is never trusted — the JS just forwards the three razorpay_* fields and
    /// follows whatever redirectUrl we return.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new VerifyResponse { Success = false, RedirectUrl = "/login?returnUrl=/cart", Reason = "Not signed in." });
        if (req == null || string.IsNullOrWhiteSpace(req.LocalOrderId))
            return BadRequest(new VerifyResponse { Success = false, RedirectUrl = "/cart", Reason = "Missing order id." });

        var startedAt = DateTime.UtcNow;
        _log.LogInformation("Razorpay verify BEGIN UserId={UserId} LocalOrderId={Local} GwOrderId={GwOrder} GwPaymentId={GwPayment} HasSig={HasSig} IP={IP} UA={UA}",
            userId, req.LocalOrderId, req.razorpay_order_id, req.razorpay_payment_id, !string.IsNullOrWhiteSpace(req.razorpay_signature), ClientIp, UserAgent);

        try
        {
            // Ownership guard: only the order's owner can hand us a verification for it.
            var owned = await _checkout.GetReceiptAsync(userId, req.LocalOrderId);
            if (owned == null)
            {
                _log.LogWarning("Razorpay verify ownership FAIL UserId={UserId} LocalOrderId={Local}", userId, req.LocalOrderId);
                return Ok(new VerifyResponse { Success = false, RedirectUrl = $"/checkout/payment-failed?o={req.LocalOrderId}&r={Uri.EscapeDataString("Order not found")}", Reason = "not found" });
            }

            var receipt = await _checkout.ConfirmPaymentAsync(new PaymentCallback
            {
                OrderNumber = req.LocalOrderId,
                Success = true,
                GatewayOrderId = req.razorpay_order_id,
                GatewayPaymentId = req.razorpay_payment_id,
                GatewaySignature = req.razorpay_signature
            });

            var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;

            if (receipt == null || receipt.PaymentStatus != PaymentStatus.Success)
            {
                _log.LogWarning("Razorpay verify FAILED UserId={UserId} LocalOrderId={Local} GwOrderId={GwOrder} GwPaymentId={GwPayment} ElapsedMs={Elapsed} ClientIp={IP}",
                    userId, req.LocalOrderId, req.razorpay_order_id, req.razorpay_payment_id, elapsedMs, ClientIp);

                return Ok(new VerifyResponse
                {
                    Success = false,
                    RedirectUrl = $"/checkout/payment-failed?o={req.LocalOrderId}&r={Uri.EscapeDataString("Signature verification failed")}",
                    Reason = "signature-mismatch"
                });
            }

            _log.LogInformation("Razorpay verify SUCCESS UserId={UserId} LocalOrderId={Local} GwOrderId={GwOrder} GwPaymentId={GwPayment} Amount={Amount} Invoice={Invoice} ElapsedMs={Elapsed} ClientIp={IP}",
                userId, req.LocalOrderId, req.razorpay_order_id, req.razorpay_payment_id, receipt.Total, receipt.InvoiceNumber, elapsedMs, ClientIp);

            return Ok(new VerifyResponse
            {
                Success = true,
                RedirectUrl = $"/checkout/payment-success?order={Uri.EscapeDataString(receipt.OrderNumber)}",
                Reason = null
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Razorpay verify UNEXPECTED UserId={UserId} LocalOrderId={Local}", userId, req.LocalOrderId);
            return Ok(new VerifyResponse
            {
                Success = false,
                RedirectUrl = $"/checkout/payment-failed?o={req.LocalOrderId}&r={Uri.EscapeDataString("Verification error")}",
                Reason = "exception"
            });
        }
    }

    /// <summary>
    /// Razorpay webhook receiver. Reconciliation fallback for the moments where the
    /// customer's browser dies between the popup's success handler and our /verify call.
    /// Razorpay POSTs payment.captured / payment.failed / order.paid events here with an
    /// X-Razorpay-Signature header = HMAC-SHA256(rawBody, WebhookSecret) in hex. We
    /// recompute that and constant-time compare BEFORE doing anything with the body.
    ///
    /// To wire this up in production:
    ///   Razorpay Dashboard → Settings → Webhooks → Add New Webhook
    ///   URL    : https://&lt;your-domain&gt;/api/payments/razorpay/webhook
    ///   Secret : (paste the same value as Admin → Payment Gateways → Razorpay → Webhook Secret)
    ///   Events : payment.captured, payment.failed (minimum)
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Webhook()
    {
        // 1) Read the raw body — model binding would consume it and break the HMAC.
        string raw;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            raw = await reader.ReadToEndAsync();

        var headerSig = Request.Headers["X-Razorpay-Signature"].FirstOrDefault()?.Trim() ?? string.Empty;

        // 2) Load the webhook secret from DB (same source as the admin form).
        var settings = await _settings.GetRazorpayAsync();
        if (string.IsNullOrEmpty(settings.WebhookSecret))
        {
            _log.LogWarning("Razorpay webhook received but WebhookSecret is not configured RemoteIp={IP}", ClientIp);
            return Ok(); // 200 so Razorpay doesn't retry-storm; the admin needs to configure it
        }

        // 3) HMAC-SHA256(rawBody, WebhookSecret), constant-time compare against the header.
        string computed;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.WebhookSecret)))
            computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        if (!FixedTimeEquals(computed, headerSig.ToLowerInvariant()))
        {
            _log.LogWarning("Razorpay webhook signature MISMATCH RemoteIp={IP} UA={UA} BodyLen={Len}", ClientIp, UserAgent, raw.Length);
            return Unauthorized(); // 401 — Razorpay will retry with exponential backoff
        }

        // 4) Parse the event envelope. Razorpay's standard shape:
        //    { "event": "payment.captured",
        //      "payload": { "payment": { "entity": { "id": "pay_...", "order_id": "order_...", ... } } } }
        string? eventType = null;
        string? gwOrderId = null;
        string? gwPaymentId = null;
        GatewayPaymentInstrument? instrument = null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            eventType = root.TryGetProperty("event", out var e) ? e.GetString() : null;

            if (root.TryGetProperty("payload", out var payload)
             && payload.TryGetProperty("payment", out var paySection)
             && paySection.TryGetProperty("entity", out var paymentEntity))
            {
                gwOrderId = paymentEntity.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
                gwPaymentId = paymentEntity.TryGetProperty("id", out var pid) ? pid.GetString() : null;
                // The webhook body already IS the payment entity, so the payment mode (UPI / Credit
                // Card / …) comes free here — no follow-up GET /v1/payments/{id} needed.
                instrument = RazorpayGateway.ParseInstrument(paymentEntity);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Razorpay webhook body parse failed RemoteIp={IP} BodyLen={Len}", ClientIp, raw.Length);
            return Ok(); // sig was good; the body shape isn't ours to fix — don't retry-storm
        }

        _log.LogInformation("Razorpay webhook VERIFIED Event={Event} GwOrderId={GwOrderId} GwPaymentId={GwPaymentId} PaymentMode={Mode} RemoteIp={IP} UA={UA}",
            eventType, gwOrderId, gwPaymentId, instrument?.Mode, ClientIp, UserAgent);

        // 5) Route the event. payment.captured is the one that reconciles a Pending order.
        if (eventType == "payment.captured" && !string.IsNullOrEmpty(gwOrderId))
        {
            try
            {
                var receipt = await _checkout.ConfirmFromWebhookAsync(gwOrderId!, gwPaymentId, instrument);
                _log.LogInformation("Razorpay webhook RECONCILED OrderNumber={OrderNumber} PaymentStatus={Status} Invoice={Invoice}",
                    receipt?.OrderNumber, receipt?.PaymentStatus, receipt?.InvoiceNumber);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Razorpay webhook reconcile threw GwOrderId={GwOrderId}", gwOrderId);
                return StatusCode(500); // Razorpay will retry
            }
        }
        else if (eventType == "payment.failed")
        {
            // Informational — the popup's payment.failed handler has already redirected the
            // user to the failure page. Logging it here gives a paper trail when the popup
            // path didn't get to fire (e.g., browser closed mid-flight).
            _log.LogInformation("Razorpay webhook payment.failed (informational) GwOrderId={GwOrderId} GwPaymentId={GwPaymentId}", gwOrderId, gwPaymentId);
        }

        return Ok();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private bool TryGetUserId(out Guid userId)
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private string ClientIp =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "-";

    private string UserAgent =>
        HttpContext.Request.Headers.UserAgent.ToString();

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
