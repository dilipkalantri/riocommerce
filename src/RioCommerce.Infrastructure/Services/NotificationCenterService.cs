using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class NotificationCenterService : INotificationCenterService
{
    private const string ReadKey = "notif.lastReadAt";
    private readonly RioCommerceDbContext _db;
    private readonly IRealtimeBus _bus;
    private readonly IUserPreferenceService _prefs;

    public NotificationCenterService(RioCommerceDbContext db, IRealtimeBus bus, IUserPreferenceService prefs)
    {
        _db = db; _bus = bus; _prefs = prefs;
    }

    public async Task NotifyAsync(AdminNotificationType type, NotificationSeverity severity, string title, string message,
        string? linkUrl = null, string? entityId = null)
    {
        var n = new AdminNotification
        {
            Type = type, Severity = severity, Title = title, Message = message, LinkUrl = linkUrl, EntityId = entityId
        };
        _db.AdminNotifications.Add(n);
        await _db.SaveChangesAsync();
        _bus.PublishNotification(new AdminNotificationDto(n.Id, n.Type, n.Severity, n.Title, n.Message, n.LinkUrl, n.CreatedAt));
    }

    public Task<List<AdminNotificationDto>> ListAsync(int take = 100) =>
        _db.AdminNotifications.OrderByDescending(n => n.CreatedAt).Take(take)
            .Select(n => new AdminNotificationDto(n.Id, n.Type, n.Severity, n.Title, n.Message, n.LinkUrl, n.CreatedAt))
            .ToListAsync();

    public async Task<NotificationFeed> GetFeedAsync(Guid? userId, int take = 15)
    {
        var items = await ListAsync(take);
        var since = await LastReadAsync(userId);
        var unread = await _db.AdminNotifications.CountAsync(n => n.CreatedAt > since);
        return new NotificationFeed(items, unread);
    }

    public async Task<int> GetUnreadCountAsync(Guid? userId)
    {
        var since = await LastReadAsync(userId);
        return await _db.AdminNotifications.CountAsync(n => n.CreatedAt > since);
    }

    public Task MarkAllReadAsync(Guid? userId) =>
        userId is null ? Task.CompletedTask : _prefs.SetAsync(userId.Value, ReadKey, DateTime.UtcNow);

    private async Task<DateTime> LastReadAsync(Guid? userId)
    {
        if (userId is null) return DateTime.MinValue;
        var v = await _prefs.GetAsync<DateTime?>(userId.Value, ReadKey);
        return v ?? DateTime.MinValue;
    }
}
