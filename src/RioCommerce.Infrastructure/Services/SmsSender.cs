using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

// One sender that internally dispatches to whichever provider the admin has configured.
// Falls back to logging when SMS is disabled / unconfigured so dev flows still work end-to-end.
public class SmsSender : ISmsSender
{
    private readonly IIntegrationSettingsService _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SmsSender> _log;

    public SmsSender(IIntegrationSettingsService settings, IHttpClientFactory httpFactory, ILogger<SmsSender> log)
    {
        _settings = settings;
        _httpFactory = httpFactory;
        _log = log;
    }

    public async Task<(bool ok, string? error)> SendAsync(string toPhone, string body)
    {
        if (string.IsNullOrWhiteSpace(toPhone)) return (false, "No phone number.");
        var s = await _settings.GetSmsAsync();
        if (!s.Enabled || string.IsNullOrWhiteSpace(s.Provider) || string.IsNullOrWhiteSpace(s.ApiKey))
        {
            _log.LogInformation("📱 [SMS·logged] → {Phone} (provider not enabled/configured): {Body}", toPhone, body);
            return (true, null);
        }

        try
        {
            var http = _httpFactory.CreateClient("sms");
            http.Timeout = TimeSpan.FromSeconds(15);
            var ok = s.Provider.ToUpperInvariant() switch
            {
                "MSG91"     => await SendMsg91Async(http, s, toPhone, body),
                "TWILIO"    => await SendTwilioAsync(http, s, toPhone, body),
                "TEXTLOCAL" => await SendTextLocalAsync(http, s, toPhone, body),
                _           => await SendGenericAsync(http, s, toPhone, body)
            };
            if (ok)
                _log.LogInformation("📱 [SMS·{Provider}] sent → {Phone}", s.Provider, toPhone);
            else
                _log.LogWarning("📱 [SMS·{Provider}] non-success response for {Phone}", s.Provider, toPhone);
            return ok ? (true, null) : (false, $"{s.Provider} declined the request.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "📱 SMS send failed → {Phone}", toPhone);
            return (false, ex.Message);
        }
    }

    // ── Provider implementations ───────────────────────────────────────────
    // The Send*Async signatures intentionally take the resolved settings + recipient/body so each
    // provider can shape its own request without leaking provider knowledge into the caller.

    // MSG91 Flow API — short-form, no template id required for transactional sends via 'sms' endpoint.
    private static async Task<bool> SendMsg91Async(HttpClient http, SmsSettings s, string toPhone, string body)
    {
        var baseUrl = string.IsNullOrWhiteSpace(s.BaseUrl) ? "https://api.msg91.com/api/v5/" : s.BaseUrl;
        var url = baseUrl.TrimEnd('/') + "/flow/";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("authkey", s.ApiKey);
        req.Headers.TryAddWithoutValidation("accept", "application/json");
        req.Content = JsonContent.Create(new
        {
            sender = s.SenderId ?? "RIOCOM",
            short_url = "0",
            mobiles = NormalizePhone(toPhone),
            message = body
        });
        using var res = await http.SendAsync(req);
        return res.IsSuccessStatusCode;
    }

    // Twilio Messages API — basic-auth (AccountSid as user, AuthToken as password).
    private static async Task<bool> SendTwilioAsync(HttpClient http, SmsSettings s, string toPhone, string body)
    {
        if (string.IsNullOrWhiteSpace(s.ApiSecret)) return false;        // requires AuthToken
        var baseUrl = string.IsNullOrWhiteSpace(s.BaseUrl) ? "https://api.twilio.com/" : s.BaseUrl;
        var url = baseUrl.TrimEnd('/') + $"/2010-04-01/Accounts/{s.ApiKey}/Messages.json";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{s.ApiKey}:{s.ApiSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"]   = "+" + NormalizePhone(toPhone),
            ["From"] = s.SenderId ?? "",
            ["Body"] = body
        });
        using var res = await http.SendAsync(req);
        return res.IsSuccessStatusCode;
    }

    // TextLocal — query-string-style API.
    private static async Task<bool> SendTextLocalAsync(HttpClient http, SmsSettings s, string toPhone, string body)
    {
        var baseUrl = string.IsNullOrWhiteSpace(s.BaseUrl) ? "https://api.textlocal.in/send/" : s.BaseUrl;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["apikey"]  = s.ApiKey ?? "",
            ["numbers"] = NormalizePhone(toPhone),
            ["message"] = body,
            ["sender"]  = s.SenderId ?? "RIOCOM"
        });
        using var res = await http.PostAsync(baseUrl, content);
        return res.IsSuccessStatusCode;
    }

    // Catch-all: POST JSON with conventional field names. Configure a real provider before relying on this.
    private static async Task<bool> SendGenericAsync(HttpClient http, SmsSettings s, string toPhone, string body)
    {
        if (string.IsNullOrWhiteSpace(s.BaseUrl)) return false;
        using var req = new HttpRequestMessage(HttpMethod.Post, s.BaseUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {s.ApiKey}");
        req.Content = JsonContent.Create(new { to = NormalizePhone(toPhone), message = body, sender = s.SenderId });
        using var res = await http.SendAsync(req);
        return res.IsSuccessStatusCode;
    }

    // Strip any '+' or formatting and keep digits; collapse a leading '91' to make Indian numbers consistent.
    private static string NormalizePhone(string s)
    {
        var digits = new string(s.Where(char.IsDigit).ToArray());
        return digits.Length == 12 && digits.StartsWith("91") ? digits[2..] : digits;
    }
}
