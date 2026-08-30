using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using RioCommerce.Core.DTOs.Checkout;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

/// <summary>
/// Easebuzz payment surface — mirrors the architecture of <see cref="RazorpayPaymentController"/>
/// but follows Easebuzz's redirect-flow conventions (no popup):
///
///   POST  /api/payments/easebuzz/create-order   → places a Pending order, mints an Easebuzz
///                                                 access_key, returns the hosted-page URL
///   POST  /api/payments/easebuzz/retry-order    → re-issues a fresh access_key for an
///                                                 existing Pending order
///   POST  /api/payments/easebuzz/callback       → surl/furl that Easebuzz posts the customer
///                                                 back to; verifies the SHA-512 reverse-hash
///                                                 and HTTP-redirects to /checkout/payment-*
///   POST  /api/payments/easebuzz/webhook        → server-to-server reconciliation channel
///                                                 (same hash convention as callback)
///
/// Hash format and field sequence match the reference plugin verbatim — see
/// <see cref="EasebuzzGateway"/> for the exact algorithm.
/// </summary>
[ApiController]
[Route("api/payments/easebuzz")]
public class EasebuzzPaymentController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IIntegrationSettingsService _settings;
    private readonly IPaymentGatewayFactory _gateways;
    private readonly IFranchisePortalService _franchisePortal;
    private readonly ILogger<EasebuzzPaymentController> _log;

    public EasebuzzPaymentController(ICheckoutService checkout, IIntegrationSettingsService settings,
        IPaymentGatewayFactory gateways, IFranchisePortalService franchisePortal,
        ILogger<EasebuzzPaymentController> log)
    {
        _checkout = checkout;
        _settings = settings;
        _gateways = gateways;
        _franchisePortal = franchisePortal;
        _log = log;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public class CreateOrderResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? OrderNumber { get; set; }
        public string? RedirectUrl { get; set; }   // {Endpoint}/pay/{access_key}
        public decimal Amount { get; set; }
    }

    public class RetryRequest { public string LocalOrderId { get; set; } = string.Empty; }

    // ── Endpoints ─────────────────────────────────────────────────────────────

    [HttpPost("create-order")]
    [Authorize]
    public async Task<IActionResult> CreateOrder([FromBody] CheckoutRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new CreateOrderResponse { Success = false, Error = "Not signed in." });
        if (req == null) return BadRequest(new CreateOrderResponse { Success = false, Error = "Missing checkout body." });

        // Force Easebuzz selection regardless of what the client sent — a tampered request
        // can't slip another gateway through this endpoint.
        req.PayOnline = true;
        req.PaymentMode = PaymentMode.Easebuzz;
        req.OriginBaseUrl = BuildOriginBase();

        _log.LogInformation("Easebuzz create-order BEGIN UserId={UserId} IP={IP} UA={UA}", userId, ClientIp, UserAgent);

        try
        {
            var result = await _checkout.PlaceOrderAsync(userId, req);
            if (result == null || !result.RequiresPayment || string.IsNullOrEmpty(result.RedirectUrl))
            {
                _log.LogWarning("Easebuzz create-order produced no redirect URL UserId={UserId} OrderNumber={OrderNumber}", userId, result?.OrderNumber);
                return BadRequest(new CreateOrderResponse { Success = false, Error = "Could not create the payment order." });
            }

            _log.LogInformation("Easebuzz create-order OK UserId={UserId} OrderNumber={OrderNumber} Amount={Amount} IP={IP}",
                userId, result.OrderNumber, result.Amount, ClientIp);

            return Ok(new CreateOrderResponse
            {
                Success = true,
                OrderNumber = result.OrderNumber,
                RedirectUrl = result.RedirectUrl,
                Amount = result.Amount
            });
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "Easebuzz create-order REJECTED UserId={UserId} IP={IP}", userId, ClientIp);
            return BadRequest(new CreateOrderResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Easebuzz create-order UNEXPECTED UserId={UserId} IP={IP}", userId, ClientIp);
            return StatusCode(500, new CreateOrderResponse { Success = false, Error = "Unexpected error while creating the payment order." });
        }
    }

    [HttpPost("retry-order")]
    [Authorize]
    public async Task<IActionResult> RetryOrder([FromBody] RetryRequest req)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized(new CreateOrderResponse { Success = false, Error = "Not signed in." });
        if (string.IsNullOrWhiteSpace(req?.LocalOrderId)) return BadRequest(new CreateOrderResponse { Success = false, Error = "Missing localOrderId." });

        _log.LogInformation("Easebuzz retry-order BEGIN UserId={UserId} OrderNumber={OrderNumber} IP={IP}", userId, req.LocalOrderId, ClientIp);

        try
        {
            var result = await _checkout.RetryPaymentAsync(userId, req.LocalOrderId, BuildOriginBase());
            if (result == null || string.IsNullOrEmpty(result.RedirectUrl))
                return NotFound(new CreateOrderResponse { Success = false, Error = "Order not found, not Easebuzz, or already paid." });

            _log.LogInformation("Easebuzz retry-order OK UserId={UserId} OrderNumber={OrderNumber} Amount={Amount}",
                userId, result.OrderNumber, result.Amount);

            return Ok(new CreateOrderResponse
            {
                Success = true,
                OrderNumber = result.OrderNumber,
                RedirectUrl = result.RedirectUrl,
                Amount = result.Amount
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CreateOrderResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Easebuzz retry-order UNEXPECTED UserId={UserId} OrderNumber={OrderNumber}", userId, req.LocalOrderId);
            return StatusCode(500, new CreateOrderResponse { Success = false, Error = "Unexpected error while retrying the payment." });
        }
    }

    /// <summary>
    /// Admin/franchise path: initiate an Easebuzz hosted-page intent for an EXISTING Pending order
    /// created at the counter on a customer's behalf. Role-gated to staff. Returns the redirect URL.
    /// </summary>
    [HttpPost("admin-initiate")]
    [Authorize(Roles = "super_admin,admin,operations,franchise_admin,backend_user")]
    public async Task<IActionResult> AdminInitiate([FromBody] RetryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req?.LocalOrderId))
            return BadRequest(new CreateOrderResponse { Success = false, Error = "Missing localOrderId." });
        try
        {
            var result = await _checkout.InitiateGatewayForOrderAsync(req.LocalOrderId, BuildOriginBase());
            if (result == null || string.IsNullOrEmpty(result.RedirectUrl))
                return NotFound(new CreateOrderResponse { Success = false, Error = "Order not found, not Easebuzz, or already paid." });

            return Ok(new CreateOrderResponse
            {
                Success = true,
                OrderNumber = result.OrderNumber,
                RedirectUrl = result.RedirectUrl,
                Amount = result.Amount
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new CreateOrderResponse { Success = false, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Easebuzz admin-initiate UNEXPECTED OrderNumber={OrderNumber}", req.LocalOrderId);
            return StatusCode(500, new CreateOrderResponse { Success = false, Error = "Unexpected error initiating payment." });
        }
    }

    /// <summary>
    /// surl/furl that Easebuzz POSTs the customer's browser back to after the hosted-page
    /// transaction. We verify the SHA-512 reverse-hash with our Salt and HTTP-redirect to
    /// the appropriate /checkout/payment-* page. The customer never sees a JSON response
    /// from this endpoint — only the final success/failed page.
    /// </summary>
    [HttpPost("callback")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Callback([FromForm] IFormCollection form)
    {
        var startedAt = DateTime.UtcNow;
        var status = form["status"].ToString();
        var txnid = form["txnid"].ToString();
        var easebuzzPaymentId = form["easepayid"].ToString();
        if (string.IsNullOrWhiteSpace(easebuzzPaymentId)) easebuzzPaymentId = form["payment_id"].ToString();

        _log.LogInformation("Easebuzz callback BEGIN txnid={Txnid} status={Status} easepayid={Pid} IP={IP}",
            txnid, status, easebuzzPaymentId, ClientIp);

        var dict = form.Keys.ToDictionary(k => k, k => form[k].ToString(), StringComparer.OrdinalIgnoreCase);

        // 1) Hash verification first — never act on an unverified form post.
        var gateway = _gateways.Get("Easebuzz") as EasebuzzGateway
            ?? throw new InvalidOperationException("EasebuzzGateway not registered.");
        var hashOk = await gateway.VerifyCallbackHash(dict);
        if (!hashOk)
        {
            _log.LogWarning("Easebuzz callback hash MISMATCH txnid={Txnid} status={Status} IP={IP}", txnid, status, ClientIp);
            return Redirect($"/checkout/payment-failed?o={Uri.EscapeDataString(txnid)}&r={Uri.EscapeDataString("Hash verification failed")}");
        }

        // 2) Branch on Easebuzz's status. "success" → settle; anything else → failed page.
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // The verified form post carries what the customer actually paid with (mode=UPI/CC/NB/…),
                // so the payment mode travels with the settlement — no follow-up API call.
                var instrument = EasebuzzGateway.ParseInstrument(dict);
                _log.LogInformation("Easebuzz callback payment mode txnid={Txnid} Mode={Mode} Raw={Raw} Detail={Detail}",
                    txnid, instrument?.Mode, instrument?.RawMode, instrument?.Detail);

                // ── Franchise orders settle through the FRANCHISE path ──
                // The customer path below (ConfirmByOrderNumberAsync) knows nothing about franchise
                // commission or the wallet-vs-invoice rule, so settling a franchise order through it
                // would produce the wrong commission and invoice. Hash verification has already
                // happened above and applies to both branches equally.
                if (await _franchisePortal.IsFranchiseOrderAsync(txnid))
                {
                    decimal? paid = decimal.TryParse(form["amount"].ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var amt) ? amt : null;

                    var (fok, ferr, fOrderNumber) = await _franchisePortal.SettleGatewayCallbackAsync(
                        txnid, easebuzzPaymentId, paid, instrument);

                    if (!fok)
                    {
                        _log.LogWarning("Easebuzz callback FRANCHISE settle refused txnid={Txnid} Reason={Reason}", txnid, ferr);
                        return Redirect($"/franchise/orders?payment=failed&o={Uri.EscapeDataString(txnid)}");
                    }

                    _log.LogInformation("Easebuzz callback FRANCHISE SUCCESS txnid={Txnid} OrderNumber={OrderNumber}", txnid, fOrderNumber);
                    return Redirect($"/franchise/orders?payment=success&o={Uri.EscapeDataString(fOrderNumber ?? txnid)}");
                }

                var receipt = await _checkout.ConfirmByOrderNumberAsync(txnid, easebuzzPaymentId, source: "easebuzz-callback", instrument: instrument);
                var elapsed = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;

                if (receipt == null)
                {
                    _log.LogWarning("Easebuzz callback NO ORDER txnid={Txnid} ElapsedMs={Elapsed}", txnid, elapsed);
                    return Redirect($"/checkout/payment-failed?o={Uri.EscapeDataString(txnid)}&r={Uri.EscapeDataString("Order not found")}");
                }

                _log.LogInformation("Easebuzz callback SUCCESS txnid={Txnid} OrderNumber={OrderNumber} Amount={Amount} Invoice={Invoice} ElapsedMs={Elapsed}",
                    txnid, receipt.OrderNumber, receipt.Total, receipt.InvoiceNumber, elapsed);

                return Redirect($"/checkout/payment-success?order={Uri.EscapeDataString(receipt.OrderNumber)}");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Easebuzz callback settle threw txnid={Txnid}", txnid);
                return Redirect($"/checkout/payment-failed?o={Uri.EscapeDataString(txnid)}&r={Uri.EscapeDataString("Settlement error")}");
            }
        }

        // status = "userCancelled" / "pending" / "failure" — keep the order Pending and route
        // the user to the appropriate /checkout/payment-* page.
        var reason = form["error_Message"].ToString();
        if (string.IsNullOrWhiteSpace(reason)) reason = form["error"].ToString();
        if (string.IsNullOrWhiteSpace(reason)) reason = status;

        if (string.Equals(status, "userCancelled", StringComparison.OrdinalIgnoreCase))
            return Redirect($"/checkout/payment-cancelled?o={Uri.EscapeDataString(txnid)}");

        return Redirect($"/checkout/payment-failed?o={Uri.EscapeDataString(txnid)}&r={Uri.EscapeDataString(reason)}");
    }

    /// <summary>
    /// Server-to-server reconciliation webhook. Easebuzz POSTs the same field set as
    /// the surl/furl callback. We verify the same hash and idempotently settle
    /// Pending orders. Browser-side flow doesn't depend on this; it's a safety net
    /// for the cases where the surl/furl roundtrip didn't complete.
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Webhook([FromForm] IFormCollection form)
    {
        var txnid = form["txnid"].ToString();
        var status = form["status"].ToString();
        var easebuzzPaymentId = form["easepayid"].ToString();
        if (string.IsNullOrWhiteSpace(easebuzzPaymentId)) easebuzzPaymentId = form["payment_id"].ToString();

        var dict = form.Keys.ToDictionary(k => k, k => form[k].ToString(), StringComparer.OrdinalIgnoreCase);
        var gateway = _gateways.Get("Easebuzz") as EasebuzzGateway
            ?? throw new InvalidOperationException("EasebuzzGateway not registered.");

        var hashOk = await gateway.VerifyCallbackHash(dict);
        if (!hashOk)
        {
            _log.LogWarning("Easebuzz webhook hash MISMATCH txnid={Txnid} status={Status} IP={IP}", txnid, status, ClientIp);
            return Unauthorized();
        }

        var instrument = EasebuzzGateway.ParseInstrument(dict);

        _log.LogInformation("Easebuzz webhook VERIFIED txnid={Txnid} status={Status} easepayid={Pid} PaymentMode={Mode} IP={IP}",
            txnid, status, easebuzzPaymentId, instrument?.Mode, ClientIp);

        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var receipt = await _checkout.ConfirmByOrderNumberAsync(txnid, easebuzzPaymentId, source: "easebuzz-webhook", instrument: instrument);
                _log.LogInformation("Easebuzz webhook RECONCILED txnid={Txnid} OrderNumber={OrderNumber} Status={Status} Invoice={Invoice}",
                    txnid, receipt?.OrderNumber, receipt?.PaymentStatus, receipt?.InvoiceNumber);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Easebuzz webhook settle threw txnid={Txnid}", txnid);
                return StatusCode(500); // Easebuzz will retry
            }
        }
        else
        {
            _log.LogInformation("Easebuzz webhook informational (non-success) txnid={Txnid} status={Status}", txnid, status);
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

    private string BuildOriginBase()
        => $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
}
