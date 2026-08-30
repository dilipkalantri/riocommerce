using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>Deletes cart rows that haven't been touched in 30 days.</summary>
public sealed class ExpiredCartCleanupTask : IScheduledTaskHandler
{
    public string Key => "ExpiredCartCleanup";
    public string DefaultName => "Expired Cart Cleanup";
    public string DefaultDescription => "Deletes cart rows that haven't been touched in 30 days.";
    public int DefaultIntervalSeconds => 24 * 60 * 60; // daily

    private const int ExpireAfterDays = 30;

    private readonly RioCommerceDbContext _db;
    private readonly ILogger<ExpiredCartCleanupTask> _log;

    public ExpiredCartCleanupTask(RioCommerceDbContext db, ILogger<ExpiredCartCleanupTask> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<TaskRunSummary> ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-ExpireAfterDays);
        var stale = await _db.CartItems.Where(c => c.UpdatedAt < cutoff).Take(2000).ToListAsync(ct);
        if (stale.Count > 0)
        {
            _db.CartItems.RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
        }
        _log.LogInformation("ExpiredCartCleanup removed {Count} cart rows older than {Days} days", stale.Count, ExpireAfterDays);
        return new TaskRunSummary(stale.Count, stale.Count, $"{stale.Count} cart rows deleted");
    }
}
