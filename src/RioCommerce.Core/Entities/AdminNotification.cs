using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// A persisted admin notification feed item (powers the bell dropdown + history page).
// Stored as the shared admin feed; per-user read state is tracked via UserPreference (lastReadAt timestamp).
public class AdminNotification : BaseEntity
{
    public AdminNotificationType Type { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }     // e.g. /admin/orders/{id}
    public string? EntityId { get; set; }
}
