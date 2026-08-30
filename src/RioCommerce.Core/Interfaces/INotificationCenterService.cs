using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Interfaces;

// Admin notification center: persists feed items, broadcasts them live, tracks per-user unread state.
public interface INotificationCenterService
{
    Task NotifyAsync(AdminNotificationType type, NotificationSeverity severity, string title, string message,
        string? linkUrl = null, string? entityId = null);

    Task<NotificationFeed> GetFeedAsync(Guid? userId, int take = 15);
    Task<List<AdminNotificationDto>> ListAsync(int take = 100);
    Task<int> GetUnreadCountAsync(Guid? userId);
    Task MarkAllReadAsync(Guid? userId);
}
