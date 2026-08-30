using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface INotificationService
{
    /// <summary>
    /// Fan out an event across every active template with the given key (one per channel).
    /// Each matched template picks its recipient by channel: Email → Email, SMS → Phone, WhatsApp → WhatsApp.
    /// Channels missing a destination are skipped (logged). Returns ok=true if any channel dispatched.
    /// </summary>
    Task<(bool ok, string? error, int channelsSent)> SendAsync(string templateKey, NotificationRecipient to, IDictionary<string, string> tokens, string? triggeredBy = null);

    Task<NotificationStats> StatsAsync();
    Task<List<MessageTemplateItem>> ListTemplatesAsync();
    Task<MessageTemplateEditModel?> GetTemplateAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveTemplateAsync(MessageTemplateEditModel model);
    Task ToggleTemplateAsync(Guid id);
    Task<(bool ok, string? error)> DeleteTemplateAsync(Guid id);
    Task<List<NotificationLogItem>> LogsAsync(int take = 50);
}
