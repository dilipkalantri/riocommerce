using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Realtime;

public record AdminNotificationDto(
    Guid Id, AdminNotificationType Type, NotificationSeverity Severity,
    string Title, string Message, string? LinkUrl, DateTime CreatedAt);

// Lightweight signal that a data set changed, so subscribed grids/cards can refresh the affected scope.
public record RealtimeEvent(string Scope, string? EntityId = null);

public record NotificationFeed(List<AdminNotificationDto> Items, int UnreadCount);
