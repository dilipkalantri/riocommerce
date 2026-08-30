using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

// Read/write integration credentials (SMTP, SMS, payment gateways) persisted in the AppSettings key-value table.
public interface IIntegrationSettingsService
{
    Task<SmtpSettings> GetSmtpAsync();
    Task SaveSmtpAsync(SmtpSettings s);

    Task<SmsSettings> GetSmsAsync();
    Task SaveSmsAsync(SmsSettings s);

    Task<RazorpaySettings> GetRazorpayAsync();
    Task SaveRazorpayAsync(RazorpaySettings s);

    Task<EasebuzzSettings> GetEasebuzzAsync();
    Task SaveEasebuzzAsync(EasebuzzSettings s);

    // ─── Email provider router + per-API-provider credentials ────────────────
    // The router selects SMTP vs HTTP API on every send so an admin switch takes
    // effect without an application restart.
    Task<EmailProviderSettings> GetEmailProviderAsync();
    Task SaveEmailProviderAsync(EmailProviderSettings s);

    Task<ZeptoMailSettings> GetZeptoMailAsync();
    Task SaveZeptoMailAsync(ZeptoMailSettings s);
}
