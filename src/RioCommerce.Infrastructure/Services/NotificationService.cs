using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Notifications;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class NotificationService : INotificationService, INotificationSender
{
    private readonly RioCommerceDbContext _db;
    private readonly IMessageDispatcher _dispatcher;
    private readonly IEmailRouter _email;

    public NotificationService(RioCommerceDbContext db, IMessageDispatcher dispatcher, IEmailRouter email)
    {
        _db = db;
        _dispatcher = dispatcher;
        _email = email;
    }

    // ── INotificationSender (transactional trigger used by checkout) ──
    public async Task SendOrderConfirmationAsync(Order order)
    {
        var tokens = new Dictionary<string, string>
        {
            ["name"] = order.StudentName,
            ["order_number"] = order.OrderNumber,
            ["total"] = order.TotalAmount.ToString("N0"),
            ["items"] = order.Items.Count.ToString()
        };
        await SendAsync("order_confirmation",
            new NotificationRecipient(order.StudentEmail, order.StudentPhone),
            tokens,
            triggeredBy: "Order Placed");
    }

    // ── INotificationService ──
    // Phase 6: fan out one event across every active template that shares the key (one per channel).
    // Email picks NotificationRecipient.Email, SMS picks Phone, WhatsApp picks WhatsApp. Skips channels
    // that have no destination. Each channel is logged independently into notification_logs.
    public async Task<(bool ok, string? error, int channelsSent)> SendAsync(string templateKey, NotificationRecipient to, IDictionary<string, string> tokens, string? triggeredBy = null)
    {
        var templates = await _db.Set<MessageTemplate>()
            .Where(t => t.Key == templateKey && t.IsActive)
            .ToListAsync();
        if (templates.Count == 0)
        {
            await LogAsync(templateKey, "—", "", null, null, "Failed", $"No active template for '{templateKey}'.", triggeredBy);
            return (false, "No active template for that trigger.", 0);
        }

        var sent = 0;
        var errors = new List<string>();
        foreach (var tpl in templates)
        {
            var recipient = ResolveRecipient(tpl.Channel, to);
            if (string.IsNullOrWhiteSpace(recipient))
            {
                await LogAsync(templateKey, tpl.Channel, "", tpl.Subject, null, "Skipped", "No recipient for this channel.", triggeredBy);
                continue;
            }

            var subject = Render(tpl.Subject, tokens);
            var body = Render(tpl.Body, tokens);

            if (string.Equals(tpl.Channel, "Email", StringComparison.OrdinalIgnoreCase))
            {
                // Email channel goes through IEmailRouter directly so we can capture the
                // full EmailSendResult (provider + response + duration) into the log row.
                //
                // CC comes from the template row itself, so it applies to whatever triggers this key
                // without any caller passing it — and no service anywhere holds a staff address.
                // Only the Email channel has a CC concept; SMS/WhatsApp ignore the column.
                var cc = CcRecipients.Resolve(tpl.CcEmails, recipient);
                var msg = new EmailMessage(recipient, null, subject, body, IsHtml: LooksLikeHtml(body), Cc: cc);
                var provider = await _email.GetActiveProviderKeyAsync();
                var result = await _email.SendAsync(msg);
                await LogEmailAsync(templateKey, tpl.Channel, recipient, subject, body, msg.IsHtml,
                    provider, result, triggeredBy, cc);
                if (result.Ok) sent++;
                else if (!string.IsNullOrWhiteSpace(result.Error)) errors.Add($"Email: {result.Error}");
            }
            else
            {
                // SMS / WhatsApp keep using the dispatcher — no rich response yet.
                var (ok, err) = await _dispatcher.DispatchAsync(tpl.Channel, recipient, subject, body);
                await LogAsync(templateKey, tpl.Channel, recipient, subject, body, ok ? "Sent" : "Failed", err, triggeredBy);
                if (ok) sent++; else if (!string.IsNullOrWhiteSpace(err)) errors.Add($"{tpl.Channel}: {err}");
            }
        }

        if (sent == 0)
            return (false, errors.Count == 0 ? "No channel had a recipient." : string.Join("; ", errors), 0);
        return (true, errors.Count == 0 ? null : string.Join("; ", errors), sent);
    }

    private static bool LooksLikeHtml(string? body)
        => !string.IsNullOrEmpty(body) && body.IndexOf('<') >= 0 && body.IndexOf('>') > 0;

    private static string? ResolveRecipient(string channel, NotificationRecipient to) =>
        channel.ToUpperInvariant() switch
        {
            "EMAIL"    => to.Email,
            "SMS"      => to.Phone,
            "WHATSAPP" => to.WhatsApp ?? to.Phone,    // WhatsApp typically uses the same phone
            _          => to.Email ?? to.Phone ?? to.WhatsApp
        };

    public async Task<NotificationStats> StatsAsync() => new(
        await _db.Set<MessageTemplate>().CountAsync(),
        await _db.Set<NotificationLog>().CountAsync(l => l.Status == "Sent"),
        await _db.Set<NotificationLog>().CountAsync(l => l.Status == "Failed"));

    public async Task<List<MessageTemplateItem>> ListTemplatesAsync() =>
        await _db.Set<MessageTemplate>().OrderBy(t => t.Name)
            .Select(t => new MessageTemplateItem(t.Id, t.Key, t.Name, t.Channel, t.Subject, t.IsActive)).ToListAsync();

    public async Task<MessageTemplateEditModel?> GetTemplateAsync(Guid id)
    {
        var t = await _db.Set<MessageTemplate>().FirstOrDefaultAsync(x => x.Id == id);
        return t == null ? null : new MessageTemplateEditModel
        {
            Id = t.Id, Key = t.Key, Name = t.Name, Channel = t.Channel, Subject = t.Subject, Body = t.Body,
            IsActive = t.IsActive, CcEmails = t.CcEmails
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveTemplateAsync(MessageTemplateEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Key)) return (false, "Trigger key is required.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);
        var key = m.Key.Trim().ToLower().Replace(' ', '_');
        var channel = string.IsNullOrWhiteSpace(m.Channel) ? "Email" : m.Channel.Trim();

        // Rejected at save time, not at send time — an invalid CC that only surfaces when a customer
        // is waiting for a dispatch email is a silent failure. Same rules the sender applies.
        var cc = CcRecipients.Parse(m.CcEmails);
        if (!cc.Ok) return (false, cc.Error, Guid.Empty);

        // Uniqueness is (Key, Channel) — Phase 6 allows multiple templates per key, one per channel.
        if (await _db.Set<MessageTemplate>().AnyAsync(t => t.Key == key && t.Channel == channel && t.Id != (m.Id ?? Guid.Empty)))
            return (false, $"Another template already exists for key '{key}' on channel '{channel}'.", Guid.Empty);

        MessageTemplate e;
        if (m.Id is { } id && id != Guid.Empty)
            e = await _db.Set<MessageTemplate>().FirstOrDefaultAsync(t => t.Id == id) ?? throw new InvalidOperationException("Template not found.");
        else { e = new MessageTemplate(); _db.Add(e); }

        e.Key = key; e.Name = m.Name.Trim(); e.Channel = string.IsNullOrWhiteSpace(m.Channel) ? "Email" : m.Channel.Trim();
        e.Subject = m.Subject?.Trim() ?? ""; e.Body = m.Body ?? ""; e.IsActive = m.IsActive;
        // Stored in the canonical trimmed/deduped form, and only meaningful on the Email channel —
        // a CC on an SMS template would be dead data that later reads as configuration.
        e.CcEmails = string.Equals(e.Channel, "Email", StringComparison.OrdinalIgnoreCase) ? cc.Normalised : null;
        await _db.SaveChangesAsync();
        return (true, null, e.Id);
    }

    public async Task ToggleTemplateAsync(Guid id)
    {
        var t = await _db.Set<MessageTemplate>().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return;
        t.IsActive = !t.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteTemplateAsync(Guid id)
    {
        var t = await _db.Set<MessageTemplate>().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return (false, "Template not found.");
        _db.Remove(t); await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<NotificationLogItem>> LogsAsync(int take = 50) =>
        await _db.Set<NotificationLog>().OrderByDescending(l => l.CreatedAt).Take(take)
            .Select(l => new NotificationLogItem(l.Id, l.TemplateKey, l.Channel, l.Recipient, l.Subject, l.Status, l.Error, l.CreatedAt))
            .ToListAsync();

    // ── helpers ──
    private static string Render(string template, IDictionary<string, string> tokens)
    {
        foreach (var kv in tokens) template = template.Replace("{{" + kv.Key + "}}", kv.Value);
        return template;
    }

    private async Task LogAsync(string? key, string channel, string recipient, string? subject, string? body, string status, string? error, string? triggeredBy = null)
    {
        _db.Set<NotificationLog>().Add(new NotificationLog
        {
            TemplateKey = key, Channel = channel, Recipient = recipient,
            Subject = subject, Body = body, Status = status, Error = error,
            TriggeredBy = triggeredBy,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Email-specific logger that captures the provider id, the raw response, timing,
    /// and a JSON snapshot of the EmailMessage payload.
    /// </summary>
    private async Task LogEmailAsync(string? key, string channel, string recipient, string subject, string body, bool isHtml,
        string? provider, EmailSendResult result, string? triggeredBy, IReadOnlyList<string>? cc = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            to = recipient,
            // Recorded so an admin can confirm from the log who actually got copied, without having
            // to reason backwards from the template's current setting.
            cc = cc is { Count: > 0 } ? cc : null,
            subject,
            isHtml,
            bodyLength = body?.Length ?? 0,
        });

        _db.Set<NotificationLog>().Add(new NotificationLog
        {
            TemplateKey = key,
            Channel = channel,
            Recipient = recipient,
            Subject = subject,
            Body = Truncate(body, 8000),
            IsHtml = isHtml,
            Status = result.Ok ? "Sent" : "Failed",
            Error = Truncate(result.Error, 500),
            Provider = provider,
            Response = Truncate(result.Response, 2000),
            DurationMs = result.DurationMs,
            TriggeredBy = triggeredBy,
            RequestPayload = payload,
        });
        await _db.SaveChangesAsync();
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
