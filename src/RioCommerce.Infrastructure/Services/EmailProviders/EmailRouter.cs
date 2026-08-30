using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.EmailProviders;

// Resolves the active IEmailProvider on every send.
// Live admin changes (transport switch / API provider switch) take effect on
// the very next email without any restart, because settings are re-read each call.
//
// Resolution rules:
//   • If EmailProviderSettings.Transport == "smtp"      → SmtpEmailProvider
//   • If EmailProviderSettings.Transport == "api"       → IEmailProvider whose Key == EmailProviderSettings.ApiProvider
//   • If the requested provider isn't registered yet    → falls back to SMTP (logged success)
public sealed class EmailRouter : IEmailRouter
{
    private readonly IIntegrationSettingsService _settings;
    private readonly IEnumerable<IEmailProvider> _providers;
    private readonly ILogger<EmailRouter> _log;

    public EmailRouter(IIntegrationSettingsService settings, IEnumerable<IEmailProvider> providers, ILogger<EmailRouter> log)
    {
        _settings = settings;
        _providers = providers;
        _log = log;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var key = await GetActiveProviderKeyAsync(ct);
        var provider = Resolve(key) ?? Resolve(EmailTransport.Smtp);
        if (provider == null)
        {
            _log.LogWarning("No email provider registered — dropping outbound email to {Recipient}.", message.ToEmail);
            return EmailSendResult.Failure("No email provider registered.", null, 0);
        }
        return await provider.SendAsync(message, ct);
    }

    public async Task<EmailSendResult> SendTestAsync(string providerKey, EmailMessage message, CancellationToken ct = default)
    {
        var provider = Resolve(providerKey);
        if (provider == null)
            return EmailSendResult.Failure($"Provider '{providerKey}' is not registered.", null, 0);
        return await provider.SendAsync(message, ct);
    }

    public async Task<string> GetActiveProviderKeyAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetEmailProviderAsync();
        return string.Equals(s.Transport, EmailTransport.Api, StringComparison.OrdinalIgnoreCase)
            ? (s.ApiProvider ?? EmailApiProvider.ZeptoMail).Trim().ToLowerInvariant()
            : EmailTransport.Smtp;
    }

    private IEmailProvider? Resolve(string key)
        => _providers.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
