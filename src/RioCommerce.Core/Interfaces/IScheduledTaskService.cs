using RioCommerce.Core.Entities;

namespace RioCommerce.Core.Interfaces;

/// <summary>Admin-side surface for the scheduled-tasks grid + Run Now + History.</summary>
public interface IScheduledTaskService
{
    Task<List<ScheduledTask>> ListAsync();
    Task<ScheduledTask?> GetAsync(Guid id);

    /// <summary>Patch the editable fields (interval, enabled, timeout). NextRunAt is
    /// recomputed from <c>now + interval</c> so a freshly-edited task fires on its
    /// new cadence rather than its old one.</summary>
    Task<bool> UpdateAsync(Guid id, int intervalSeconds, bool enabled, int timeoutSeconds, bool stopOnError);

    /// <summary>Most-recent run rows. Pass null taskId to list across all tasks.</summary>
    Task<List<ScheduledTaskRun>> ListHistoryAsync(Guid? taskId, int take = 50);

    /// <summary>Execute the task NOW, on the calling thread (admin "Run Now" path). The
    /// runner uses the same code path with <c>trigger=schedule</c>.</summary>
    Task<TaskRunSummary?> RunNowAsync(Guid taskId, Guid? actorUserId, string? actorName, CancellationToken ct);
}
