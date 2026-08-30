using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.EmailProviders;

// SMTP transport — wraps System.Net.Mail.SmtpClient using the admin-configured
// SmtpSettings. Disabled / unconfigured installations resolve to a logged
// success so dev flows never break for a missing credential.
public sealed class SmtpEmailProvider : IEmailProvider
{
    public string Key => "smtp";
    public string DisplayName => "SMTP";

    private readonly IIntegrationSettingsService _settings;
    private readonly ILogger<SmtpEmailProvider> _log;

    public SmtpEmailProvider(IIntegrationSettingsService settings, ILogger<SmtpEmailProvider> log)
    {
        _settings = settings;
        _log = log;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var s = await _settings.GetSmtpAsync();
        return s.Enabled
               && !string.IsNullOrWhiteSpace(s.Host)
               && !string.IsNullOrWhiteSpace(s.FromEmail);
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var smtp = await _settings.GetSmtpAsync();

        if (!smtp.Enabled || string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromEmail))
        {
            _log.LogInformation("✉️  [SMTP] → {Recipient} | {Subject} (logged — SMTP not enabled/configured)", message.ToEmail, message.Subject);
            sw.Stop();
            return EmailSendResult.Success("Logged (SMTP not configured)", sw.ElapsedMilliseconds);
        }

        try
        {
            using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.UseSsl };
            if (!string.IsNullOrWhiteSpace(smtp.Username))
                client.Credentials = new NetworkCredential(smtp.Username, smtp.Password ?? "");

            var fromEmail = message.FromEmailOverride ?? smtp.FromEmail!;
            var fromName  = message.FromNameOverride  ?? (string.IsNullOrWhiteSpace(smtp.FromName) ? fromEmail : smtp.FromName);

            using var mail = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = message.Subject,
                Body = message.Body,
                IsBodyHtml = message.IsHtml,
            };
            mail.To.Add(string.IsNullOrWhiteSpace(message.ToName) ? message.ToEmail : new MailAddress(message.ToEmail, message.ToName).ToString());
            // One message with a real CC header — not a second send. MailAddress parses each value,
            // so a malformed entry throws here and is reported as a send failure rather than being
            // pasted into the header verbatim.
            foreach (var cc in message.Cc ?? Array.Empty<string>())
                mail.CC.Add(new MailAddress(cc));
            if (!string.IsNullOrWhiteSpace(message.ReplyToOverride))
                mail.ReplyToList.Add(new MailAddress(message.ReplyToOverride));

            await client.SendMailAsync(mail, ct);
            sw.Stop();
            _log.LogInformation("✉️  [SMTP] sent → {Recipient} | {Subject} in {Ms} ms", message.ToEmail, message.Subject, sw.ElapsedMilliseconds);
            return EmailSendResult.Success("Sent via SMTP", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogError(ex, "SMTP send failed → {Recipient}", message.ToEmail);
            return EmailSendResult.Failure(ex.Message, null, sw.ElapsedMilliseconds);
        }
    }
}
