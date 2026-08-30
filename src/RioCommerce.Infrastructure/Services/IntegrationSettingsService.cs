using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

// Persists integration credentials in the AppSettings key-value table.
// Keys are category-prefixed (e.g. "smtp.host") because AppSetting.Key is the global primary key.
// Secret fields (passwords, API secrets, salts) are encrypted at rest via ASP.NET Core Data Protection
// before being written, and decrypted on read; the key ring itself lives in the DB (see RioCommerceDbContext).
public class IntegrationSettingsService : IIntegrationSettingsService
{
    private readonly RioCommerceDbContext _db;
    private readonly IDataProtector _protector;
    private readonly SettingsCache _cache;

    public IntegrationSettingsService(RioCommerceDbContext db, IDataProtectionProvider dp, SettingsCache cache)
    {
        _db = db;
        _cache = cache;
        _protector = dp.CreateProtector("RioCommerce.IntegrationSettings.Secrets.v1");
    }

    // ── Email (SMTP) ──
    public async Task<SmtpSettings> GetSmtpAsync()
    {
        var v = await LoadAsync("smtp");
        return new SmtpSettings
        {
            Enabled = Bool(v, "enabled"),
            Host = Str(v, "host"),
            Port = Int(v, "port", 587),
            UseSsl = Bool(v, "use_ssl", true),
            Username = Str(v, "username"),
            Password = Dec(Str(v, "password")),
            FromEmail = Str(v, "from_email"),
            FromName = Str(v, "from_name"),
        };
    }

    public async Task SaveSmtpAsync(SmtpSettings s)
    {
        await UpsertAsync("smtp", "enabled", s.Enabled ? "true" : "false");
        await UpsertAsync("smtp", "host", s.Host);
        await UpsertAsync("smtp", "port", s.Port.ToString());
        await UpsertAsync("smtp", "use_ssl", s.UseSsl ? "true" : "false");
        await UpsertAsync("smtp", "username", s.Username);
        await UpsertAsync("smtp", "password", Enc(s.Password));
        await UpsertAsync("smtp", "from_email", s.FromEmail);
        await UpsertAsync("smtp", "from_name", s.FromName);
        await _db.SaveChangesAsync();
        _cache.Clear();   // keep the shared settings cache consistent
    }

    // ── SMS gateway ──
    public async Task<SmsSettings> GetSmsAsync()
    {
        var v = await LoadAsync("sms");
        return new SmsSettings
        {
            Enabled = Bool(v, "enabled"),
            Provider = Str(v, "provider"),
            ApiKey = Dec(Str(v, "api_key")),
            ApiSecret = Dec(Str(v, "api_secret")),
            SenderId = Str(v, "sender_id"),
            BaseUrl = Str(v, "base_url"),
        };
    }

    public async Task SaveSmsAsync(SmsSettings s)
    {
        await UpsertAsync("sms", "enabled", s.Enabled ? "true" : "false");
        await UpsertAsync("sms", "provider", s.Provider);
        await UpsertAsync("sms", "api_key", Enc(s.ApiKey));
        await UpsertAsync("sms", "api_secret", Enc(s.ApiSecret));
        await UpsertAsync("sms", "sender_id", s.SenderId);
        await UpsertAsync("sms", "base_url", s.BaseUrl);
        await _db.SaveChangesAsync();
        _cache.Clear();   // keep the shared settings cache consistent
    }

    // ── Razorpay ──
    // No live/test toggle is persisted — the gateway auto-detects the environment from
    // the Key ID prefix (rzp_test_* vs rzp_live_*) at checkout time.
    public async Task<RazorpaySettings> GetRazorpayAsync()
    {
        var v = await LoadAsync("razorpay");
        return new RazorpaySettings
        {
            Enabled = Bool(v, "enabled"),
            KeyId = Str(v, "key_id"),
            KeySecret = Dec(Str(v, "key_secret")),
            WebhookSecret = Dec(Str(v, "webhook_secret")),
        };
    }

    public async Task SaveRazorpayAsync(RazorpaySettings s)
    {
        await UpsertAsync("razorpay", "enabled", s.Enabled ? "true" : "false");
        await UpsertAsync("razorpay", "key_id", s.KeyId?.Trim());
        await UpsertAsync("razorpay", "key_secret", Enc(s.KeySecret?.Trim()));
        await UpsertAsync("razorpay", "webhook_secret", Enc(s.WebhookSecret?.Trim()));
        await _db.SaveChangesAsync();
        _cache.Clear();   // keep the shared settings cache consistent
    }

    // ── Easebuzz ──
    public async Task<EasebuzzSettings> GetEasebuzzAsync()
    {
        var v = await LoadAsync("easebuzz");
        return new EasebuzzSettings
        {
            Enabled = Bool(v, "enabled"),
            MerchantKey = Str(v, "merchant_key"),
            Salt = Dec(Str(v, "salt")),
            EndPoint = Str(v, "endpoint") ?? EasebuzzSettings.DefaultEndPoint,
            WebhookUrl = Str(v, "webhook_url"),
        };
    }

    public async Task SaveEasebuzzAsync(EasebuzzSettings s)
    {
        await UpsertAsync("easebuzz", "enabled", s.Enabled ? "true" : "false");
        await UpsertAsync("easebuzz", "merchant_key", s.MerchantKey?.Trim());
        await UpsertAsync("easebuzz", "salt", Enc(s.Salt?.Trim()));
        await UpsertAsync("easebuzz", "endpoint", (s.EndPoint ?? string.Empty).Trim().TrimEnd('/'));
        await UpsertAsync("easebuzz", "webhook_url", (s.WebhookUrl ?? string.Empty).Trim());
        await _db.SaveChangesAsync();
        _cache.Clear();   // keep the shared settings cache consistent
    }

    // ── Email provider router ──
    // Persists *which* transport is active (SMTP or HTTP API) plus the selected
    // API provider. Per-provider credentials live in their own categories
    // (e.g. "zeptomail"), so the router stays slim and adding a new provider is
    // a new category + a new IEmailProvider implementation — no schema change.
    public async Task<EmailProviderSettings> GetEmailProviderAsync()
    {
        var v = await LoadAsync("email");
        return new EmailProviderSettings
        {
            Transport   = Str(v, "transport")    ?? EmailTransport.Smtp,
            ApiProvider = Str(v, "api_provider") ?? EmailApiProvider.ZeptoMail,
        };
    }

    public async Task SaveEmailProviderAsync(EmailProviderSettings s)
    {
        await UpsertAsync("email", "transport", string.IsNullOrWhiteSpace(s.Transport) ? EmailTransport.Smtp : s.Transport.Trim().ToLowerInvariant());
        await UpsertAsync("email", "api_provider", string.IsNullOrWhiteSpace(s.ApiProvider) ? EmailApiProvider.ZeptoMail : s.ApiProvider.Trim().ToLowerInvariant());
        await _db.SaveChangesAsync();
        _cache.Clear();
    }

    // ── ZeptoMail HTTP API ──
    public async Task<ZeptoMailSettings> GetZeptoMailAsync()
    {
        var v = await LoadAsync("zeptomail");
        return new ZeptoMailSettings
        {
            Enabled     = Bool(v, "enabled"),
            Domain      = Str(v, "domain")       ?? ZeptoMailSettings.DefaultDomain,
            Host        = Str(v, "host")         ?? ZeptoMailSettings.DefaultHost,
            ApiEndpoint = Str(v, "api_endpoint") ?? ZeptoMailSettings.DefaultApiEndpoint,
            AgentAlias  = Str(v, "agent_alias"),
            Token       = Dec(Str(v, "token")),
            FromName    = Str(v, "from_name"),
            FromEmail   = Str(v, "from_email"),
            ReplyTo     = Str(v, "reply_to"),
        };
    }

    public async Task SaveZeptoMailAsync(ZeptoMailSettings s)
    {
        await UpsertAsync("zeptomail", "enabled",      s.Enabled ? "true" : "false");
        await UpsertAsync("zeptomail", "domain",       s.Domain?.Trim());
        await UpsertAsync("zeptomail", "host",         s.Host?.Trim());
        await UpsertAsync("zeptomail", "api_endpoint", (s.ApiEndpoint ?? string.Empty).Trim().TrimEnd('/'));
        await UpsertAsync("zeptomail", "agent_alias",  s.AgentAlias?.Trim());
        await UpsertAsync("zeptomail", "token",        Enc(s.Token?.Trim()));
        await UpsertAsync("zeptomail", "from_name",    s.FromName?.Trim());
        await UpsertAsync("zeptomail", "from_email",   s.FromEmail?.Trim());
        await UpsertAsync("zeptomail", "reply_to",     s.ReplyTo?.Trim());
        await _db.SaveChangesAsync();
        _cache.Clear();
    }

    // ── Helpers ──
    // Loads a category's rows keyed by the bare name (the part after "<category>.").
    private async Task<Dictionary<string, string>> LoadAsync(string category)
    {
        var prefix = category + ".";
        var rows = await _db.AppSettings.Where(a => a.Category == category).ToListAsync();
        return rows.ToDictionary(
            a => a.Key.StartsWith(prefix, StringComparison.Ordinal) ? a.Key[prefix.Length..] : a.Key,
            a => a.Value);
    }

    private static string? Str(Dictionary<string, string> map, string name) =>
        map.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    // Encrypt a secret before storing; empty/null stays empty so "not set" reads back as null.
    private string? Enc(string? v) => string.IsNullOrEmpty(v) ? v : _protector.Protect(v);

    // Decrypt a stored secret; tolerate legacy plaintext / undecryptable values by returning as-is.
    private string? Dec(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v;
        try { return _protector.Unprotect(v); }
        catch { return v; }
    }

    private static bool Bool(Dictionary<string, string> map, string name, bool def = false)
    {
        var s = Str(map, name);
        return s == null ? def : s == "true";
    }

    private static int Int(Dictionary<string, string> map, string name, int def)
    {
        var s = Str(map, name);
        return int.TryParse(s, out var n) ? n : def;
    }

    private async Task UpsertAsync(string category, string name, string? value)
    {
        var key = $"{category}.{name}";
        var row = await _db.AppSettings.FirstOrDefaultAsync(a => a.Key == key);
        if (row == null)
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value ?? "", Category = category, UpdatedAt = DateTime.UtcNow });
        else { row.Value = value ?? ""; row.Category = category; row.UpdatedAt = DateTime.UtcNow; }
    }
}
