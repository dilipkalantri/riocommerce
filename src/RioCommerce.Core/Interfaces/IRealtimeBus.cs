using RioCommerce.Core.DTOs.Realtime;
namespace RioCommerce.Core.Interfaces;

// In-process pub/sub bus (singleton). Blazor circuits subscribe; services publish.
// Delivers cross-session live updates without a separate SignalR hub (Blazor Server already rides on SignalR).
public interface IRealtimeBus
{
    event Action<AdminNotificationDto>? NotificationPublished;
    event Action<RealtimeEvent>? DataChanged;

    void PublishNotification(AdminNotificationDto notification);
    void PublishDataChanged(RealtimeEvent evt);
}
