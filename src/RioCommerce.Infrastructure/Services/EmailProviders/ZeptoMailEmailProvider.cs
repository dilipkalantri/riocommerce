using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.EmailProviders;

// ZeptoMail (Zoho) HTTP API transport.
// Posts a JSON envelope to the admin-configured endpoint with header
//   Authorization: Zoho-enczapikey <token>
// All values (host, endpoint URL, alias, token, sender) come from
// IIntegrationSettingsService — nothing is hard-coded except the well-known
// defaults exposed via ZeptoMailSettings constants so a fresh install just works.
public sealed class ZeptoMailEmailProvider : IEmailProvider
{
    public string Key => "zeptomail";
    public string DisplayName => "ZeptoMail";

    private readonly HttpClient _http;
    private readonly IIntegrationSettingsService _settings;
    private readonly ILogger<ZeptoMailEmailProvider> _log;

    public ZeptoMailEmailProvider(HttpClient http, IIntegrationSettingsService settings, ILogger<ZeptoMailEmailProvider> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var z = await _settings.GetZeptoMailAsync();
        return z.Enabled
               && !string.IsNullOrWhiteSpace(z.ApiEndpoint)
               && !string.IsNullOrWhiteSpace(z.Token)
               && !string.IsNullOrWhiteSpace(z.FromEmail);
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var z = await _settings.GetZeptoMailAsync();

        // Single structured log line summarising every value the gate evaluates.
        // The token is never logged — only its presence and length so we can rule
        // out "missing" vs "wrong" without leaking the secret.
        _log.LogInformation(
            "ZeptoMail Configuration Loaded — Provider: {Provider}, Host: {Host}, Domain: {Domain}, Endpoint: {Endpoint}, Sender: {Sender} <{FromName}>, AgentAlias: {Alias}, TokenPresent: {TokenPresent} (len={TokenLen}), Enabled: {Enabled}",
            Key, z.Host, z.Domain, z.ApiEndpoint, z.FromEmail, z.FromName, z.AgentAlias,
            !string.IsNullOrWhiteSpace(z.Token), z.Token?.Length ?? 0, z.Enabled);

        // Build an explicit missing-field list rather than silently treating a
        // misconfigured provider as "Success". The popup renders this as the
        // Error line so the admin sees exactly which value is empty.
        var missing = new List<string>();
        if (!z.Enabled) missing.Add("Enabled flag (Settings → Email → Active Provider)");
        if (string.IsNullOrWhiteSpace(z.ApiEndpoint)) missing.Add("Api Endpoint");
        if (string.IsNullOrWhiteSpace(z.Host)) missing.Add("Host");
        if (string.IsNullOrWhiteSpace(z.Token)) missing.Add("Token");
        if (string.IsNullOrWhiteSpace(z.FromEmail)) missing.Add("Sender Email");

        if (missing.Count > 0)
        {
            var err = "Missing configuration: " + string.Join(", ", missing);
            _log.LogWarning("✉️  [ZeptoMail] cannot send → {Recipient}: {Err}", message.ToEmail, err);
            sw.Stop();
            return EmailSendResult.Failure(err, null, sw.ElapsedMilliseconds);
        }

        var fromEmail = message.FromEmailOverride ?? z.FromEmail!;
        var fromName  = message.FromNameOverride  ?? (string.IsNullOrWhiteSpace(z.FromName) ? fromEmail : z.FromName);
        var replyTo   = message.ReplyToOverride   ?? z.ReplyTo;

        // Envelope per ZeptoMail Send-Mail API (v1.1).
        var payload = new Dictionary<string, object?>
        {
            ["from"] = new Dictionary<string, string?> { ["address"] = fromEmail, ["name"] = fromName },
            ["to"]   = new[]
            {
                new Dictionary<string, object?>
                {
                    ["email_address"] = new Dictionary<string, string?>
                    {
                        ["address"] = message.ToEmail,
                        ["name"]    = message.ToName ?? message.ToEmail,
                    }
                }
            },
            ["subject"] = message.Subject,
        };

        // CC recipients ride on the same envelope (same "email_address" shape as "to"), so ZeptoMail
        // delivers one message with a real CC header. The key is omitted entirely when there is no
        // CC — an empty array is rejected by the API.
        if (message.Cc is { Count: > 0 })
            payload["cc"] = message.Cc
                .Select(a => new Dictionary<string, object?>
                {
                    ["email_address"] = new Dictionary<string, string?> { ["address"] = a, ["name"] = a }
                })
                .ToArray();

        if (message.IsHtml) payload["htmlbody"] = message.Body;
        else                payload["textbody"] = message.Body;

        if (!string.IsNullOrWhiteSpace(replyTo))
            payload["reply_to"] = new[] { new Dictionary<string, string?> { ["address"] = replyTo } };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, z.ApiEndpoint)
            {
                Content = JsonContent.Create(payload, options: new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
            };
            // ZeptoMail requires this exact scheme — NOT a Bearer token.
            req.Headers.TryAddWithoutValidation("Authorization", "Zoho-enczapikey " + z.Token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(z.AgentAlias))
                req.Headers.TryAddWithoutValidation("X-Sender-Alias", z.AgentAlias);

            _log.LogInformation("✉️  [ZeptoMail] POST {Endpoint} → {Recipient}", z.ApiEndpoint, message.ToEmail);

            using var res = await _http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            sw.Stop();

            // Always log the response status + truncated body so success and
            // failure both leave a breadcrumb for the admin to inspect.
            _log.LogInformation("✉️  [ZeptoMail] response {Status} {Reason} ({Ms} ms): {Body}",
                (int)res.StatusCode, res.ReasonPhrase, sw.ElapsedMilliseconds, Truncate(body));

            if (!res.IsSuccessStatusCode)
                return EmailSendResult.Failure($"HTTP {(int)res.StatusCode} {res.ReasonPhrase}", Truncate(body), sw.ElapsedMilliseconds);

            return EmailSendResult.Success(Truncate(body), sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "ZeptoMail send failed → {Recipient}", message.ToEmail);
            return EmailSendResult.Failure(ex.Message, null, sw.ElapsedMilliseconds);
        }
    }

    // Keep response previews readable inside admin tables / logs.
    private static string? Truncate(string? s, int max = 1200)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max] + "…");
}
