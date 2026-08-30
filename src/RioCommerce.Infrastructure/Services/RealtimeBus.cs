using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

// Singleton in-process pub/sub. Services publish; Blazor circuits subscribe and marshal to their own
// dispatcher via InvokeAsync. A faulted/disposed subscriber must never break delivery to the others.
public class RealtimeBus : IRealtimeBus
{
    public event Action<AdminNotificationDto>? NotificationPublished;
    public event Action<RealtimeEvent>? DataChanged;

    public void PublishNotification(AdminNotificationDto notification) => Raise(NotificationPublished, notification);
    public void PublishDataChanged(RealtimeEvent evt) => Raise(DataChanged, evt);

    private static void Raise<T>(Action<T>? handlers, T arg)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try { h(arg); }
            catch { /* dead/faulted circuit — skip, never break the broadcast */ }
        }
    }
}
