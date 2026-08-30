using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// Marks orders older than 14 days that are still Pending as Cancelled. After two weeks
/// the gateway intent has expired anyway; the row clutter slows the admin list.
/// Idempotent — only flips rows that are actually still Pending.
/// </summary>
public sealed class PendingOrderCleanupTask : IScheduledTaskHandler
{
    public string Key => "PendingOrderCleanup";
    public string DefaultName => "Pending Order Cleanup";
    public string DefaultDescription => "Cancels Pending orders older than 14 days whose gateway intent has expired.";
    public int DefaultIntervalSeconds => 24 * 60 * 60; // daily

    private const int CleanupAfterDays = 14;

    private readonly RioCommerceDbContext _db;
    private readonly ILogger<PendingOrderCleanupTask> _log;

    public PendingOrderCleanupTask(RioCommerceDbContext db, ILogger<PendingOrderCleanupTask> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<TaskRunSummary> ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-CleanupAfterDays);
        var stale = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Pending
                     && o.Status == OrderStatus.Pending
                     && o.CreatedAt < cutoff)
            .Take(500)
            .ToListAsync(ct);

        foreach (var order in stale)
        {
            order.Status = OrderStatus.Cancelled;
        }

        if (stale.Count > 0) await _db.SaveChangesAsync(ct);
        _log.LogInformation("PendingOrderCleanup cancelled {Count} stale orders (older than {Days} days)", stale.Count, CleanupAfterDays);
        return new TaskRunSummary(stale.Count, stale.Count, $"{stale.Count} stale orders cancelled");
    }
}
