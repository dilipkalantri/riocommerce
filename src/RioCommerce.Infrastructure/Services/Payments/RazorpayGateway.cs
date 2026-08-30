using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.Payments;

/// <summary>
/// Live Razorpay implementation of <see cref="IPaymentGateway"/>.
///
/// Credentials are read from the database (Admin → Configuration → Payment Gateways →
/// Razorpay) on EVERY call via <see cref="IIntegrationSettingsService"/>. There is no
/// in-memory cache here and no app restart is required after the admin saves new keys —
/// the very next checkout picks them up.
///
/// The environment (test/live) is auto-detected from the Key ID prefix:
///   • rzp_test_* → Razorpay test mode
///   • rzp_live_* → Razorpay live mode
/// Both modes hit api.razorpay.com; Razorpay routes by the key, so there is no toggle.
///
/// • CreateOrderAsync → POST /v1/orders with Basic auth (KeyId:KeySecret).
/// • VerifyPayment    → HMAC-SHA256("<orderId>|<paymentId>", KeySecret), hex lowercase.
/// • RefundAsync      → POST /v1/payments/{id}/refund.
/// • GetPaymentInstrumentAsync → GET /v1/payments/{id}, reads <c>method</c>/<c>card</c>/<c>bank</c>/
///   <c>wallet</c>/<c>vpa</c> to report what the customer actually paid with (UPI / Credit Card / …).
/// </summary>
public sealed class RazorpayGateway : IPaymentGateway
{
    private const string ApiBase = "https://api.razorpay.com";

    /// <summary>Cap on the instrument lookup. It runs on the popup-verify path, between the
    /// customer's payment and their redirect to the success page — a slow Razorpay must not hold
    /// that up, and an unknown payment mode is a far better outcome than a stalled checkout.</summary>
    private static readonly TimeSpan InstrumentLookupTimeout = TimeSpan.FromSeconds(6);

    private readonly HttpClient _http;
    private readonly IIntegrationSettingsService _settings;
    private readonly ILogger<RazorpayGateway> _log;

    public string Name => "Razorpay";

    public RazorpayGateway(HttpClient http, IIntegrationSettingsService settings, ILogger<RazorpayGateway> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
        _http.BaseAddress ??= new Uri(ApiBase);
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<GatewayOrder> CreateOrderAsync(string orderNumber, decimal amount, string currency = "INR", GatewayCreateContext? context = null)
    {
        _ = context; // Razorpay doesn't use the context (popup flow gets prefill from the browser).
        var cred = await LoadCredentialsAsync(requireForLive: true);

        // Razorpay expects the smallest currency unit (paise). Round to avoid Decimal→int truncation.
        var paise = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var resolvedCurrency = string.IsNullOrWhiteSpace(currency) ? "INR" : currency;
        var body = new
        {
            amount = paise,
            currency = resolvedCurrency,
            receipt = orderNumber,
            payment_capture = 1
        };

        _log.LogInformation("Razorpay CreateOrder begin Key={Key} Mode={Mode} OrderNumber={OrderNumber} AmountPaise={Paise} Currency={Currency}",
            cred.Masked, cred.Mode, orderNumber, paise, resolvedCurrency);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/orders") { Content = JsonContent.Create(body) };
        req.Headers.Authorization = cred.AuthHeader;
        using var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            var detail = TryExtractRazorpayError(json);
            _log.LogError("Razorpay CreateOrder FAILED Key={Key} Mode={Mode} OrderNumber={OrderNumber} Status={Status} ErrorCode={Code} Description={Desc} RawBody={Body}",
                cred.Masked, cred.Mode, orderNumber, (int)res.StatusCode, detail.code, detail.description, json);

            var msg = !string.IsNullOrWhiteSpace(detail.description)
                ? $"Razorpay rejected the order: {detail.description}"
                : $"Razorpay order creation failed (HTTP {(int)res.StatusCode}).";
            if ((int)res.StatusCode == 401)
                msg += " The Key ID or Key Secret is invalid. Check Admin → Configuration → Payment Gateways → Razorpay.";
            throw new InvalidOperationException(msg);
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var gwOrderId = root.GetProperty("id").GetString() ?? throw new InvalidOperationException("Razorpay response missing 'id'.");
        var gwAmount = root.TryGetProperty("amount", out var a) ? a.GetInt64() : -1;
        var gwCurrency = root.TryGetProperty("currency", out var c) ? c.GetString() : null;
        var gwStatus = root.TryGetProperty("status", out var s) ? s.GetString() : null;

        // Defensive cross-check: if Razorpay echoed a different amount/currency we'd see a
        // confusing failure inside the popup. Surface it now instead.
        if (gwAmount != paise || !string.Equals(gwCurrency, resolvedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogError("Razorpay order echo MISMATCH AskedPaise={AskedPaise} GotPaise={GotPaise} AskedCcy={AskedCcy} GotCcy={GotCcy}",
                paise, gwAmount, resolvedCurrency, gwCurrency);
            throw new InvalidOperationException("Razorpay order echo mismatch (amount/currency). Please contact support.");
        }

        _log.LogInformation("Razorpay CreateOrder OK Key={Key} Mode={Mode} OrderNumber={OrderNumber} GatewayOrderId={GwId} GwAmount={GwAmt} GwStatus={GwStatus}",
            cred.Masked, cred.Mode, orderNumber, gwOrderId, gwAmount, gwStatus);

        return new GatewayOrder(Name, gwOrderId, cred.KeyId, amount);
    }

    public bool VerifyPayment(string gatewayOrderId, string? gatewayPaymentId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(gatewayOrderId) || string.IsNullOrWhiteSpace(gatewayPaymentId) || string.IsNullOrWhiteSpace(signature))
        {
            _log.LogWarning("Razorpay VerifyPayment missing fields OrderId={OrderId} PaymentId={PaymentId} HasSig={HasSig}",
                gatewayOrderId, gatewayPaymentId, !string.IsNullOrWhiteSpace(signature));
            return false;
        }

        // Synchronous interface — block on the secret read. Settings are simple key/value
        // rows so this returns quickly; the verification is also off the hot UI thread.
        var s = _settings.GetRazorpayAsync().GetAwaiter().GetResult();
        if (string.IsNullOrEmpty(s.KeySecret))
        {
            _log.LogError("Razorpay VerifyPayment cannot run: Key Secret is not configured.");
            return false;
        }

        var payload = $"{gatewayOrderId}|{gatewayPaymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(s.KeySecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();
        var ok = FixedTimeEquals(computed, signature.Trim().ToLowerInvariant());
        if (!ok)
        {
            _log.LogWarning("Razorpay signature mismatch OrderId={OrderId} PaymentId={PaymentId}", gatewayOrderId, gatewayPaymentId);
        }
        return ok;
    }

    /// <summary>
    /// Polls Razorpay for the current state of an order. Used by the Payment Status Sync
    /// scheduled task. Razorpay's /v1/orders/{id}/payments returns every payment attempt
    /// against the order; we report the first one in a terminal state (captured / failed).
    /// </summary>
    public async Task<GatewayPaymentStatus> QueryOrderStatusAsync(string gatewayOrderId)
    {
        if (string.IsNullOrWhiteSpace(gatewayOrderId))
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "missing order id");

        var cred = await LoadCredentialsAsync(requireForLive: false);
        if (string.IsNullOrEmpty(cred.KeyId) || string.IsNullOrEmpty(cred.Secret))
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "razorpay keys not configured");

        using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/orders/{gatewayOrderId}/payments");
        req.Headers.Authorization = cred.AuthHeader;
        using var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Razorpay QueryOrderStatus FAILED GwOrderId={GwOrderId} Status={Status} Body={Body}",
                gatewayOrderId, (int)res.StatusCode, json);
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, $"http {(int)res.StatusCode}");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return new GatewayPaymentStatus(GatewayPaymentState.Pending, null, null, "no_items", "no payments yet");

            // Walk in order — the most recent attempt is usually last. Find the first one
            // in a terminal state (captured / failed) and report that.
            string? lastRaw = null;
            string? capturedPid = null; long? capturedAmt = null;
            GatewayPaymentInstrument? capturedInstrument = null;
            string? failedPid = null;
            foreach (var item in items.EnumerateArray())
            {
                var status = item.TryGetProperty("status", out var s) ? s.GetString() : null;
                var pid = item.TryGetProperty("id", out var p) ? p.GetString() : null;
                var amt = item.TryGetProperty("amount", out var a) ? a.GetInt64() : (long?)null;
                lastRaw = status;
                if (string.Equals(status, "captured", StringComparison.OrdinalIgnoreCase))
                {
                    capturedPid = pid; capturedAmt = amt;
                    // The list response carries full payment entities, so the instrument comes free —
                    // no extra GET /v1/payments/{id} needed on the reconciliation path.
                    capturedInstrument = ParseInstrument(item);
                }
                else if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                { failedPid = pid; }
            }

            if (capturedPid != null)
                return new GatewayPaymentStatus(GatewayPaymentState.Success, capturedPid, capturedAmt, "captured", null, capturedInstrument);
            if (failedPid != null)
                return new GatewayPaymentStatus(GatewayPaymentState.Failed, failedPid, null, lastRaw, null);

            return new GatewayPaymentStatus(GatewayPaymentState.Pending, null, null, lastRaw, "no terminal payments");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Razorpay QueryOrderStatus parse failed GwOrderId={GwOrderId}", gatewayOrderId);
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "parse error");
        }
    }

    /// <summary>
    /// Reads the instrument a captured payment was made with — GET /v1/payments/{id}, then
    /// <see cref="ParseInstrument"/>. Called on the popup-verify path, where the browser only hands
    /// us the payment id.
    ///
    /// <para>Purely informational: every failure mode (keys missing, HTTP error, timeout, odd body)
    /// returns null rather than throwing, and the request carries its own short timeout so a slow
    /// Razorpay can't stall the customer's redirect to the success page.</para>
    /// </summary>
    public async Task<GatewayPaymentInstrument?> GetPaymentInstrumentAsync(string? gatewayPaymentId)
    {
        if (string.IsNullOrWhiteSpace(gatewayPaymentId)) return null;

        try
        {
            var cred = await LoadCredentialsAsync(requireForLive: false);
            if (string.IsNullOrEmpty(cred.KeyId) || string.IsNullOrEmpty(cred.Secret))
            {
                _log.LogWarning("Razorpay GetPaymentInstrument skipped — keys not configured PaymentId={PaymentId}", gatewayPaymentId);
                return null;
            }

            using var cts = new CancellationTokenSource(InstrumentLookupTimeout);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/v1/payments/{gatewayPaymentId}");
            req.Headers.Authorization = cred.AuthHeader;
            using var res = await _http.SendAsync(req, cts.Token);
            var json = await res.Content.ReadAsStringAsync(cts.Token);

            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Razorpay GetPaymentInstrument FAILED PaymentId={PaymentId} Status={Status} Body={Body}",
                    gatewayPaymentId, (int)res.StatusCode, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var instrument = ParseInstrument(doc.RootElement);
            _log.LogInformation("Razorpay payment instrument PaymentId={PaymentId} Mode={Mode} Raw={Raw} Detail={Detail}",
                gatewayPaymentId, instrument?.Mode, instrument?.RawMode, instrument?.Detail);
            return instrument;
        }
        catch (Exception ex)
        {
            // Never propagate — a settled payment must not fail because we couldn't label its mode.
            _log.LogWarning(ex, "Razorpay GetPaymentInstrument threw PaymentId={PaymentId}", gatewayPaymentId);
            return null;
        }
    }

    /// <summary>
    /// Normalises a Razorpay payment entity into a display label + detail. Works on the entity
    /// from GET /v1/payments/{id}, from /v1/orders/{id}/payments items, and from the
    /// <c>payload.payment.entity</c> of a webhook — all three share the same shape.
    ///
    /// <para>Razorpay reports the channel in <c>method</c> (card / netbanking / wallet / upi / emi /
    /// bank_transfer / cardless_emi / paylater) and the instrument specifics alongside it:
    /// <c>card.type</c> credit|debit|prepaid, <c>bank</c>, <c>wallet</c>, <c>vpa</c>. "Card" alone
    /// isn't what the customer sees, so credit/debit is folded into the label.</para>
    /// </summary>
    public static GatewayPaymentInstrument? ParseInstrument(JsonElement payment)
    {
        if (payment.ValueKind != JsonValueKind.Object) return null;

        var method = Str(payment, "method");
        if (string.IsNullOrWhiteSpace(method)) return null;

        JsonElement card = default;
        var hasCard = payment.TryGetProperty("card", out card) && card.ValueKind == JsonValueKind.Object;
        var cardType = hasCard ? Str(card, "type") : null;          // credit | debit | prepaid
        var cardNetwork = hasCard ? Str(card, "network") : null;    // Visa | MasterCard | RuPay …
        var cardIssuer = hasCard ? Str(card, "issuer") : null;      // HDFC | ICIC …
        var bank = Str(payment, "bank");
        var wallet = Str(payment, "wallet");
        var vpa = Str(payment, "vpa");

        var mode = method.ToLowerInvariant() switch
        {
            "upi" => "UPI",
            "card" => CardLabel(cardType),
            "emi" => "EMI",
            "cardless_emi" => "Cardless EMI",
            "netbanking" => "Net Banking",
            "wallet" => "Wallet",
            "paylater" => "Pay Later",
            "bank_transfer" => "Bank Transfer",
            "nach" => "NACH",
            "upi_transfer" => "UPI",
            // Anything Razorpay adds later still lands somewhere sensible instead of blank.
            _ => Humanise(method),
        };

        // Detail = the most specific extra context available for that mode.
        var detail = method.ToLowerInvariant() switch
        {
            "upi" or "upi_transfer" => vpa,
            // Provider tokens ("payzapp", "getsimpl") are lowercase — title-case them so an
            // invoice never prints a raw gateway token.
            "wallet" or "paylater" or "cardless_emi" => Humanise(wallet),
            "netbanking" => bank,
            "card" or "emi" => JoinNonEmpty(cardNetwork, cardIssuer),
            _ => bank ?? wallet ?? vpa,
        };

        return new GatewayPaymentInstrument(mode, NullIfBlank(detail), method);

        static string CardLabel(string? type) => type?.ToLowerInvariant() switch
        {
            "credit" => "Credit Card",
            "debit" => "Debit Card",
            "prepaid" => "Prepaid Card",
            _ => "Card",
        };
    }

    public async Task<GatewayRefundResult> RefundAsync(string? gatewayPaymentId, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(gatewayPaymentId))
            return new GatewayRefundResult(false, null, "Missing gateway payment id.");

        var cred = await LoadCredentialsAsync(requireForLive: true);
        var paise = (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        var body = new { amount = paise };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/v1/payments/{gatewayPaymentId}/refund") { Content = JsonContent.Create(body) };
        req.Headers.Authorization = cred.AuthHeader;
        using var res = await _http.SendAsync(req);
        var json = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Razorpay refund failed PaymentId={PaymentId} Status={Status} Body={Body}", gatewayPaymentId, (int)res.StatusCode, json);
            var detail = TryExtractRazorpayError(json);
            return new GatewayRefundResult(false, null, detail.description ?? $"Refund failed ({(int)res.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(json);
        var refundId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        return new GatewayRefundResult(true, refundId, "Refund accepted by Razorpay.");
    }

    // ── Credential loading & validation ──────────────────────────────────────

    private sealed record Credentials(string KeyId, string Secret, string Mode, string Masked, AuthenticationHeaderValue AuthHeader);

    private async Task<Credentials> LoadCredentialsAsync(bool requireForLive)
    {
        // Pulled fresh from DB on every call → new keys saved in the admin UI are live
        // immediately, no cache invalidation, no app restart.
        var s = await _settings.GetRazorpayAsync();
        var keyId = (s.KeyId ?? string.Empty).Trim();
        var secret = (s.KeySecret ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(keyId))
            throw new InvalidOperationException("Please configure Razorpay Key ID.");
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("Please configure Razorpay Key Secret.");
        if (!s.Enabled && requireForLive)
            throw new InvalidOperationException("Razorpay is currently disabled. Enable it in Admin → Configuration → Payment Gateways → Razorpay.");
        if (!keyId.StartsWith("rzp_test_", StringComparison.Ordinal) && !keyId.StartsWith("rzp_live_", StringComparison.Ordinal))
            throw new InvalidOperationException("Razorpay Key ID looks invalid. It must start with rzp_test_ or rzp_live_.");

        var mode = keyId.StartsWith("rzp_live_", StringComparison.Ordinal) ? "Live" : "Test";
        var masked = keyId.Length > 12 ? $"{keyId[..9]}…{keyId[^4..]}" : keyId;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{secret}"));
        var auth = new AuthenticationHeaderValue("Basic", token);
        return new Credentials(keyId, secret, mode, masked, auth);
    }

    // Razorpay error shape: { "error": { "code": "BAD_REQUEST_ERROR", "description": "…", "source": "…", "reason": "…" } }
    private static (string? code, string? description) TryExtractRazorpayError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
                var desc = err.TryGetProperty("description", out var d) ? d.GetString() : null;
                return (code, desc);
            }
        }
        catch { /* not JSON / unexpected shape — fall through */ }
        return (null, null);
    }

    // Length-checked constant-time compare — guards against timing leaks on the signature check.
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    // ── Instrument-parsing helpers ────────────────────────────────────────────

    /// <summary>Reads a string property, tolerating nulls and non-string values.</summary>
    private static string? Str(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? NullIfBlank(v.GetString()) : null;

    /// <summary>"cardless_emi" → "Cardless Emi", "payzapp" → "Payzapp". Keeps unknown gateway
    /// tokens presentable instead of leaking snake_case onto an invoice.</summary>
    private static string Humanise(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        var parts = token.Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', parts);
    }

    private static string? JoinNonEmpty(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return kept.Length == 0 ? null : string.Join(" · ", kept);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
