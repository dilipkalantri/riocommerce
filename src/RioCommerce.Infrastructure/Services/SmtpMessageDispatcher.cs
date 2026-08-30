using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Multi-channel dispatcher. Email → IEmailRouter (which itself picks SMTP vs an HTTP-API
/// provider like ZeptoMail/SendGrid based on the admin's selection); SMS → ISmsSender;
/// WhatsApp / Push → logged for now. Each channel falls back to logging when its provider
/// is disabled or unconfigured so dev flows always succeed even without real credentials.
/// </summary>
public class SmtpMessageDispatcher : IMessageDispatcher
{
    private readonly IEmailRouter _email;
    private readonly ISmsSender _sms;
    private readonly ILogger<SmtpMessageDispatcher> _log;

    public SmtpMessageDispatcher(IEmailRouter email, ISmsSender sms, ILogger<SmtpMessageDispatcher> log)
    {
        _email = email;
        _sms = sms;
        _log = log;
    }

    public async Task<(bool ok, string? error)> DispatchAsync(string channel, string recipient, string subject, string body)
    {
        if (string.Equals(channel, "SMS", StringComparison.OrdinalIgnoreCase))
            return await _sms.SendAsync(recipient, body);

        if (!string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase))
        {
            // WhatsApp / Push provider wiring is still pending — log so the calling flow still succeeds.
            _log.LogInformation("✉️  [{Channel}] → {Recipient} | {Subject} (logged — provider sending pending)", channel, recipient, subject);
            return (true, null);
        }

        var result = await _email.SendAsync(new EmailMessage(recipient, null, subject, body));
        return (result.Ok, result.Error);
    }
}
