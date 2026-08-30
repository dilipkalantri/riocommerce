using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.Payments;

/// <summary>
/// Easebuzz implementation of <see cref="IPaymentGateway"/>.
///
/// Mirrors the API/hash/redirect flow used in the reference nopCommerce plugin
/// (Antargyan.Plugin.Payments.Easebuzz). Specifically:
///
///   • Hash on initiate :  SHA-512(key|txnid|amount|productinfo|firstname|email|udf1|…|udf10|SALT)
///   • Initiate API     :  POST {Endpoint}/payment/initiateLink   (form-encoded)
///   • Response shape   :  { "status": 1, "data": "&lt;access_key&gt;" }
///   • Browser redirect :  {Endpoint}/pay/{access_key}             (full page nav, no popup)
///   • Hash on callback :  SHA-512(SALT|status|udf10|…|udf1|email|firstname|productinfo|amount|txnid|key)
///                          — note: REVERSED field order, SALT prepended, status prepended
///
/// Credentials AND endpoint are read from the database (Admin → Payment Gateways →
/// Easebuzz) on every call via <see cref="IIntegrationSettingsService"/>. No cache,
/// no app restart — the next checkout picks up new values immediately. The endpoint
/// is fully editable on the admin form so a merchant can point it at production,
/// sandbox, or any URL Easebuzz provides.
/// </summary>
public sealed class EasebuzzGateway : IPaymentGateway
{
    // Exact hash sequence from the plugin's Easebuzz.InitiatePaymentAPI (line 62).
    private static readonly string[] InitiateHashSeq =
        "key|txnid|amount|productinfo|firstname|email|udf1|udf2|udf3|udf4|udf5|udf6|udf7|udf8|udf9|udf10".Split('|');

    private readonly HttpClient _http;
    private readonly IIntegrationSettingsService _settings;
    private readonly ILogger<EasebuzzGateway> _log;

    public string Name => "Easebuzz";

    public EasebuzzGateway(HttpClient http, IIntegrationSettingsService settings, ILogger<EasebuzzGateway> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<GatewayOrder> CreateOrderAsync(string orderNumber, decimal amount, string currency = "INR", GatewayCreateContext? context = null)
    {
        var cred = await LoadCredentialsAsync();
        if (context == null)
            throw new InvalidOperationException("Easebuzz requires customer details (name, email, phone, return URL). The checkout flow must pass GatewayCreateContext.");

        // Easebuzz expects rupees as a decimal string with 2 decimal places ("250.00").
        // Plugin uses ((float)order.OrderTotal).ToString() which we normalise to invariant.
        var amountStr = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        var fields = BuildInitiateFields(
            key: cred.Key,
            txnid: orderNumber,
            amount: amountStr,
            productInfo: context.ProductInfo,
            firstName: context.CustomerName,
            email: context.CustomerEmail,
            phone: context.CustomerPhone,
            returnUrl: context.ReturnUrl);

        var hash = Sha512Lower(BuildInitiateHashString(fields, cred.Salt));
        fields["hash"] = hash;

        _log.LogInformation("Easebuzz CreateOrder begin OrderNumber={OrderNumber} Amount={Amount} Endpoint={Endpoint} Key={Key}",
            orderNumber, amountStr, cred.Endpoint, cred.Masked);

        var url = $"{cred.Endpoint}/payment/initiateLink";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields!) };
        using var res = await _http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("Easebuzz initiateLink HTTP {Status} OrderNumber={OrderNumber} Body={Body}", (int)res.StatusCode, orderNumber, body);
            throw new InvalidOperationException($"Easebuzz initiate failed (HTTP {(int)res.StatusCode}).");
        }

        // Response shape: {"status":1,"data":"<access_key>"}  on success
        //                 {"status":0,"data":"<error message>"} on failure
        string? accessKey = null;
        string? errorText = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.ToString() : null;
            var data = root.TryGetProperty("data", out var d) ? d.GetString() : null;
            if (status == "1" || string.Equals(status, "true", StringComparison.OrdinalIgnoreCase))
                accessKey = data;
            else
                errorText = data;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Easebuzz initiateLink JSON parse failed OrderNumber={OrderNumber} Body={Body}", orderNumber, body);
            throw new InvalidOperationException("Easebuzz returned an unexpected response. Please try again.");
        }

        if (string.IsNullOrEmpty(accessKey))
        {
            _log.LogError("Easebuzz initiateLink REJECTED OrderNumber={OrderNumber} Reason={Reason} Body={Body}", orderNumber, errorText, body);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(errorText)
                    ? "Easebuzz did not return an access key."
                    : $"Easebuzz rejected the order: {errorText}");
        }

        var redirectUrl = $"{cred.Endpoint}/pay/{accessKey}";
        _log.LogInformation("Easebuzz CreateOrder OK OrderNumber={OrderNumber} AccessKey={AccessKey} RedirectUrl={Redirect}",
            orderNumber, MaskAccess(accessKey), redirectUrl);

        // GatewayOrderId stores the txnid (=our OrderNumber) — Easebuzz reconciliation looks up
        // orders by txnid in the callback/webhook, not by access_key (which is single-use).
        return new GatewayOrder(Name, orderNumber, cred.Key, amount, redirectUrl);
    }

    /// <summary>
    /// Easebuzz's verification model is form-based (full field dict + reverse-hash),
    /// not the (orderId, paymentId, signature) shape this method expects.
    /// The Easebuzz controller calls <see cref="VerifyCallbackHash"/> directly instead.
    /// </summary>
    public bool VerifyPayment(string gatewayOrderId, string? gatewayPaymentId, string? signature) => false;

    public Task<GatewayRefundResult> RefundAsync(string? gatewayPaymentId, decimal amount)
        => Task.FromResult(new GatewayRefundResult(false, null, "Easebuzz refunds are processed via the dashboard."));

    /// <summary>
    /// Polls Easebuzz for the current state of a transaction. Used by the Payment Status
    /// Sync scheduled task. Per the reference plugin's TransactionAPI (Easebuzz.cs:161-211)
    /// the request is POST {dashboard}/transaction/v1/retrieve with hash sequence
    /// key|txnid|amount|email|phone|SALT.
    /// </summary>
    public async Task<GatewayPaymentStatus> QueryOrderStatusAsync(string txnid, decimal amount, string email, string phone)
    {
        if (string.IsNullOrWhiteSpace(txnid))
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "missing txnid");

        var cred = await LoadCredentialsAsync(throwIfMissing: false);
        if (string.IsNullOrEmpty(cred.Key) || string.IsNullOrEmpty(cred.Salt))
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "easebuzz keys not configured");

        // ── Amount format here is NOT the same as on initiate, and that is not a typo ──
        // initiateLink is happy with "299.00". The retrieve API computes its side of the hash from
        // the amount as a Python float, whose repr drops trailing zeros but always keeps one
        // decimal: 299.00 -> "299.0", 15000.00 -> "15000.0", 598.50 -> "598.5". Sending "299.00"
        // (or "299") returns {"status":false,"msg":"Hash mismatch"} — verified against the live API
        // on a known-paid order. This silently broke every reconciliation query: the Payment Status
        // Sync task kept reporting "checked N, updated 0", so an order whose callback never arrived
        // could never be recovered automatically.
        var amountStr = PythonFloatAmount(amount);
        var hashStr = $"{cred.Key}|{txnid}|{amountStr}|{email}|{phone}|{cred.Salt}";
        var hash = Sha512Lower(hashStr);

        var form = new Dictionary<string, string>
        {
            ["key"] = cred.Key,
            ["txnid"] = txnid,
            ["amount"] = amountStr,
            ["email"] = email ?? string.Empty,
            ["phone"] = phone ?? string.Empty,
            ["hash"] = hash,
        };

        // The retrieve endpoint lives at dashboard.easebuzz.in for BOTH test and live, per
        // the plugin (line 184). It's not affected by the configured payment EndPoint.
        const string url = "https://dashboard.easebuzz.in/transaction/v1/retrieve";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form!) };
        using var res = await _http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();

        if (!res.IsSuccessStatusCode)
        {
            _log.LogWarning("Easebuzz QueryOrderStatus FAILED Txnid={Txnid} Status={Status} Body={Body}", txnid, (int)res.StatusCode, body);
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, $"http {(int)res.StatusCode}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // Shape: { "status": true, "msg": { "status": "success" | "pending" | "failed", "easepayid": "...", ... } }
            // "status" comes back as a JSON BOOLEAN here (initiateLink uses a number instead), so it
            // has to be read by ValueKind. The old code called GetString() on it, which throws on a
            // boolean — the throw was swallowed by the catch below and every call, success or
            // failure, degraded to "parse error".
            var apiOk = root.TryGetProperty("status", out var s1) && IsTruthy(s1);
            if (!apiOk || !root.TryGetProperty("msg", out var msg))
                return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "no msg");

            var raw = msg.TryGetProperty("status", out var st) ? st.GetString() : null;
            var pid = msg.TryGetProperty("easepayid", out var p) ? p.GetString() : null;

            var state = raw?.ToLowerInvariant() switch
            {
                "success" or "completed" => GatewayPaymentState.Success,
                "failure" or "failed" or "bounced" => GatewayPaymentState.Failed,
                "userCancelled" or "usercancelled" => GatewayPaymentState.Cancelled,
                "pending" or "initiated" or "in progress" or "" or null => GatewayPaymentState.Pending,
                _ => GatewayPaymentState.Unknown,
            };

            // The retrieve payload carries the same instrument fields as the callback form
            // (mode / card_type / bank_name / …), so reconciliation labels the mode for free.
            var instrument = state == GatewayPaymentState.Success ? ParseInstrument(FlattenJsonObject(msg)) : null;
            return new GatewayPaymentStatus(state, pid, null, raw, null, instrument);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Easebuzz QueryOrderStatus parse failed Txnid={Txnid} Body={Body}", txnid, body);
            return new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "parse error");
        }
    }

    /// <summary>
    /// Reverse-hash verification for the callback/webhook payload. Reads the merchant Salt
    /// from settings, builds the canonical reverse-hash string (per the plugin's
    /// EasebuzzController.PaymentResult — line 327ff), and constant-time compares.
    /// </summary>
    public async Task<bool> VerifyCallbackHash(IReadOnlyDictionary<string, string> form)
    {
        var cred = await LoadCredentialsAsync(throwIfMissing: false);
        if (string.IsNullOrEmpty(cred.Salt))
        {
            _log.LogWarning("Easebuzz callback verification skipped — Salt is not configured.");
            return false;
        }

        var hashFromBody = form.TryGetValue("hash", out var h) ? h?.Trim() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(hashFromBody))
        {
            _log.LogWarning("Easebuzz callback missing hash field. Keys: {Keys}", string.Join(",", form.Keys));
            return false;
        }

        var status = form.TryGetValue("status", out var st) ? st ?? string.Empty : string.Empty;

        // From the plugin (line 331): reverse the same initiate sequence, prepend "{salt}|{status}",
        // then walk the reversed sequence inserting each form value (empty string when absent).
        var reversedSeq = InitiateHashSeq.Reverse().ToArray();
        var sb = new StringBuilder();
        sb.Append(cred.Salt).Append('|').Append(status);
        foreach (var key in reversedSeq)
        {
            sb.Append('|');
            if (form.TryGetValue(key, out var v) && v != null) sb.Append(v);
        }

        var computed = Sha512Lower(sb.ToString());
        var ok = FixedTimeEquals(computed, hashFromBody.ToLowerInvariant());
        if (!ok)
        {
            _log.LogWarning("Easebuzz callback hash MISMATCH txnid={Txnid} status={Status}",
                form.TryGetValue("txnid", out var t) ? t : "?", status);
        }
        return ok;
    }

    /// <summary>
    /// Normalises the instrument the customer actually paid with out of an Easebuzz payload —
    /// the surl/furl + webhook form post, or the flattened <c>msg</c> object from
    /// <c>transaction/v1/retrieve</c>. Both carry the same field names.
    ///
    /// <para>Easebuzz reports the channel in <c>mode</c> as a short code (CC / DC / NB / UPI / WL /
    /// EMI / DBQR / NEFT …) with specifics alongside: <c>card_type</c> (CREDIT|DEBIT),
    /// <c>bank_name</c>, <c>issuing_bank</c>, <c>upi_va</c>, <c>PG_TYPE</c>. Card codes are folded
    /// into "Credit Card" / "Debit Card" so the label reads the way the customer experienced it.</para>
    ///
    /// <para>Returns null when the payload carries no usable mode — callers treat that as unknown.</para>
    /// </summary>
    public static GatewayPaymentInstrument? ParseInstrument(IReadOnlyDictionary<string, string> fields)
    {
        var raw = Get(fields, "mode");
        var cardType = Get(fields, "card_type");        // CREDIT | DEBIT (also set for EMI)
        var bank = Get(fields, "bank_name") ?? Get(fields, "issuing_bank");
        var vpa = Get(fields, "upi_va");
        var pgType = Get(fields, "PG_TYPE");

        var code = (raw ?? string.Empty).Trim().ToUpperInvariant();
        var mode = code switch
        {
            "CC" => "Credit Card",
            "DC" => "Debit Card",
            "PPC" or "PPI" => "Prepaid Card",
            "NB" => "Net Banking",
            "UPI" or "UPIQR" => "UPI",
            "WL" or "WALLET" => "Wallet",
            "EMI" => "EMI",
            "CARDLESS_EMI" or "CARDLESSEMI" => "Cardless EMI",
            "PAYLATER" or "PL" => "Pay Later",
            "DBQR" or "QR" => "Bank QR",
            "NEFT" or "RTGS" or "IMPS" => "Bank Transfer",
            "CASH" => "Cash Card",
            // No mode code, but the card fields say what it was — common on some Easebuzz responses.
            "" => CardFallback(cardType),
            // Anything Easebuzz adds later: prefer the card hint, else present the code as-is.
            _ => CardFallback(cardType) ?? Humanise(code),
        };

        if (string.IsNullOrWhiteSpace(mode)) return null;

        var detail = code switch
        {
            "UPI" or "UPIQR" => vpa,
            "NB" or "DBQR" or "NEFT" or "RTGS" or "IMPS" => bank,
            "CC" or "DC" or "EMI" or "PPC" or "PPI" => bank,
            "WL" or "WALLET" => Humanise(pgType) is { Length: > 0 } w ? w : bank,
            _ => bank ?? vpa,
        };

        return new GatewayPaymentInstrument(mode!, NullIfBlank(detail), NullIfBlank(raw));

        // "CREDIT"/"DEBIT" → a proper card label; null when the payload gives us nothing to go on.
        static string? CardFallback(string? cardType) => cardType?.Trim().ToUpperInvariant() switch
        {
            "CREDIT" => "Credit Card",
            "DEBIT" => "Debit Card",
            "PREPAID" => "Prepaid Card",
            _ => null,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed record Credentials(string Key, string Salt, string Endpoint, string Masked);

    /// <summary>
    /// The amount rendered the way Python's <c>str(float(x))</c> renders it, which is what the
    /// Easebuzz retrieve API hashes on its side: trailing zeros dropped, but always at least one
    /// decimal place. 299.00 → "299.0", 598.50 → "598.5", 299.25 → "299.25", 15000 → "15000.0".
    /// </summary>
    public static string PythonFloatAmount(decimal amount)
    {
        var s = amount.ToString("0.0############################", System.Globalization.CultureInfo.InvariantCulture);
        // "0.0#…" already guarantees one decimal and trims the rest; nothing further to do.
        return s;
    }

    /// <summary>
    /// Easebuzz is inconsistent about the type of its top-level <c>status</c> flag — a number on
    /// initiateLink, a boolean on retrieve, and a quoted string in some payloads. Read it by kind
    /// so no shape throws.
    /// </summary>
    private static bool IsTruthy(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.TryGetInt32(out var n) && n == 1,
        JsonValueKind.String => e.GetString() is { } v
            && (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    /// <summary>Flattens a one-level JSON object into the same string dictionary shape the form
    /// callback produces, so <see cref="ParseInstrument"/> handles both sources unchanged.</summary>
    private static Dictionary<string, string> FlattenJsonObject(JsonElement obj)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (obj.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in obj.EnumerateObject())
        {
            var value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => prop.Value.ToString(),
                _ => null,
            };
            if (value != null) dict[prop.Name] = value;
        }
        return dict;
    }

    private static string? Get(IReadOnlyDictionary<string, string> fields, string key)
        => fields.TryGetValue(key, out var v) ? NullIfBlank(v) : null;

    /// <summary>"easebuzz_wallet" → "Easebuzz Wallet". Keeps unknown gateway codes presentable
    /// rather than printing a raw token on an invoice.</summary>
    private static string Humanise(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        var parts = token.Replace('_', ' ').Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', parts);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<Credentials> LoadCredentialsAsync(bool throwIfMissing = true)
    {
        var s = await _settings.GetEasebuzzAsync();
        var key = (s.MerchantKey ?? string.Empty).Trim();
        var salt = (s.Salt ?? string.Empty).Trim();
        var endpoint = (s.EndPoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(endpoint)) endpoint = EasebuzzSettings.DefaultEndPoint;

        if (throwIfMissing)
        {
            if (string.IsNullOrEmpty(key))      throw new InvalidOperationException("Please configure Easebuzz Merchant Key.");
            if (string.IsNullOrEmpty(salt))     throw new InvalidOperationException("Please configure Easebuzz Salt.");
            if (string.IsNullOrEmpty(endpoint)) throw new InvalidOperationException("Please configure Easebuzz End Point.");
            if (!s.Enabled)                     throw new InvalidOperationException("Easebuzz is currently disabled. Enable it in Admin → Configuration → Payment Gateways → Easebuzz.");
        }

        var masked = key.Length > 6 ? $"{key[..3]}…{key[^3..]}" : "<empty>";
        return new Credentials(key, salt, endpoint, masked);
    }

    private static Dictionary<string, string> BuildInitiateFields(
        string key, string txnid, string amount, string productInfo,
        string firstName, string email, string phone, string returnUrl)
    {
        // Field set + ordering must match the plugin's Easebuzz.InitiatePaymentAPI hashtable.
        //
        // productinfo and firstname are SANITISED here, before the hash is taken over this same
        // dictionary — so what we hash is always exactly what we post. Doing it any later would
        // break the signature; doing it in CheckoutService would push a gateway quirk into the
        // order logic, where it does not belong.
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["txnid"]       = txnid,
            ["key"]         = key,
            ["amount"]      = amount,
            ["firstname"]   = SanitiseName(firstName),
            ["email"]       = (email ?? string.Empty).Trim(),
            ["phone"]       = (phone ?? string.Empty).Trim(),
            ["productinfo"] = SanitiseProductInfo(productInfo, txnid),
            ["surl"]        = returnUrl.Trim(),
            ["furl"]        = returnUrl.Trim(),
            ["udf1"]        = string.Empty,
            ["udf2"]        = string.Empty,
            ["udf3"]        = string.Empty,
            ["udf4"]        = string.Empty,
            ["udf5"]        = string.Empty,
        };
    }

    /// <summary>
    /// Makes a product description Easebuzz will accept.
    ///
    /// <para>Easebuzz validates <c>productinfo</c> against a very narrow character set — verified
    /// against the live initiateLink endpoint, it accepts <b>letters, digits, spaces and hyphens
    /// only</b>. Every one of <c>. , &amp; ( ) / : + '</c> is rejected with
    /// <c>"Invalid value for productinfo."</c>, which surfaces to the customer as the generic
    /// "Parameter validation failed". Length is NOT the constraint: 255 characters of plain text
    /// passes.</para>
    ///
    /// <para>That made this gateway unusable for real catalogue titles, which routinely look like
    /// "CA Inter SM Regular Batch for May &amp; Sept. 2027 by CA Nipurn Modi" — an ampersand AND a
    /// period. A basket of two courses failed too, because the caller joins titles with ", ".</para>
    ///
    /// <para>"&amp;" becomes "and" so the description still reads naturally; anything else outside the
    /// safe set becomes a space, runs of whitespace collapse, and the result is capped at 100
    /// characters on a word boundary. Falls back to the order number if nothing usable survives.</para>
    /// </summary>
    public static string SanitiseProductInfo(string? productInfo, string txnid)
    {
        var text = (productInfo ?? string.Empty).Replace("&", " and ");

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch == '-' ? ch : ' ');

        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Trim to a word boundary so the payment page never shows a chopped word.
        const int max = 100;
        if (cleaned.Length > max)
        {
            cleaned = cleaned[..max];
            var lastSpace = cleaned.LastIndexOf(' ');
            if (lastSpace > 40) cleaned = cleaned[..lastSpace];
        }

        if (cleaned.Length > 0) return cleaned;

        // Nothing survived (e.g. a title that was entirely punctuation or non-Latin script).
        // The order number is always safe: alphanumerics and a hyphen.
        var fallback = SanitiseProductInfo0(txnid);
        return fallback.Length > 0 ? $"Order {fallback}" : "Order";

        static string SanitiseProductInfo0(string s)
            => new(s.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
    }

    /// <summary>
    /// Easebuzz is far more permissive on <c>firstname</c> than on productinfo — probing the live
    /// endpoint, only <c>:</c> and <c>+</c> are rejected, while periods, apostrophes and ampersands
    /// all pass. So this strips just those two rather than mangling legitimate names like
    /// "Dr. Rao" or "O'Brien". Empty stays empty: the caller decides whether that is an error.
    /// </summary>
    public static string SanitiseName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        var cleaned = new string(trimmed.Where(c => c != ':' && c != '+').ToArray()).Trim();
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildInitiateHashString(IDictionary<string, string> fields, string salt)
    {
        var sb = new StringBuilder();
        foreach (var name in InitiateHashSeq)
        {
            if (fields.TryGetValue(name, out var v) && v != null) sb.Append(v);
            sb.Append('|');
        }
        sb.Append(salt);
        return sb.ToString();
    }

    private static string Sha512Lower(string text)
    {
        var hash = SHA512.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static string MaskAccess(string s)
        => s.Length > 8 ? $"{s[..4]}…{s[^4..]}" : "*";
}
