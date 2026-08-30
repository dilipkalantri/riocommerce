using System.Diagnostics;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// Admin-facing CRUD + Run Now path. Also exposed to <see cref="ScheduledTaskRunner"/>
/// which calls <see cref="ExecuteAsync"/> on every tick. The actual run code lives here
/// (not in the runner) so manual Run Now produces bit-identical side effects.
/// </summary>
public sealed class ScheduledTaskService : IScheduledTaskService
{
    private readonly RioCommerceDbContext _db;
    private readonly IServiceProvider _sp;
    private readonly Dictionary<string, IScheduledTaskHandler> _handlersByKey;
    private readonly ILogger<ScheduledTaskService> _log;

    public ScheduledTaskService(
        RioCommerceDbContext db,
        IServiceProvider sp,
        IEnumerable<IScheduledTaskHandler> handlers,
        ILogger<ScheduledTaskService> log)
    {
        _db = db;
        _sp = sp;
        _handlersByKey = handlers.ToDictionary(h => h.Key, h => h, StringComparer.OrdinalIgnoreCase);
        _log = log;
    }

    public Task<List<ScheduledTask>> ListAsync() =>
        _db.ScheduledTasks.AsNoTracking().OrderBy(t => t.Name).ToListAsync();

    public Task<ScheduledTask?> GetAsync(Guid id) =>
        _db.ScheduledTasks.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<bool> UpdateAsync(Guid id, int intervalSeconds, bool enabled, int timeoutSeconds, bool stopOnError)
    {
        var task = await _db.ScheduledTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task == null) return false;

        task.IntervalSeconds = Math.Max(30, intervalSeconds);          // hard floor — protects against typo zero
        task.TimeoutSeconds = Math.Max(10, Math.Min(3600, timeoutSeconds));
        task.Enabled = enabled;
        task.StopOnError = stopOnError;
        task.NextRunAt = DateTime.UtcNow.AddSeconds(task.IntervalSeconds);
        task.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<List<ScheduledTaskRun>> ListHistoryAsync(Guid? taskId, int take = 50)
    {
        var q = _db.ScheduledTaskRuns.AsNoTracking().Include(r => r.Task).AsQueryable();
        if (taskId.HasValue) q = q.Where(r => r.ScheduledTaskId == taskId.Value);
        return q.OrderByDescending(r => r.StartedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();
    }

    public async Task<TaskRunSummary?> RunNowAsync(Guid taskId, Guid? actorUserId, string? actorName, CancellationToken ct)
    {
        var task = await _db.ScheduledTasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task == null) return null;
        return await ExecuteAsync(task, trigger: "manual", actorName: actorName, ct);
    }

    /// <summary>The single execution path — called from both the runner and Run Now.</summary>
    public async Task<TaskRunSummary?> ExecuteAsync(ScheduledTask task, string trigger, string? actorName, CancellationToken outerCt)
    {
        if (!_handlersByKey.TryGetValue(task.TaskKey, out var handler))
        {
            _log.LogError("ScheduledTask {TaskKey} has no registered handler — skipping", task.TaskKey);
            return null;
        }

        // Open a sibling timeout token so a long-running task can't pin the host.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, task.TimeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt, timeoutCts.Token);

        var run = new ScheduledTaskRun
        {
            ScheduledTaskId = task.Id,
            Trigger = trigger,
            StartedAt = DateTime.UtcNow,
            Status = ScheduledTaskStatus.Running,
        };
        _db.ScheduledTaskRuns.Add(run);
        task.LastStatus = ScheduledTaskStatus.Running;
        task.LastRunAt = run.StartedAt;
        await _db.SaveChangesAsync(outerCt);

        var sw = Stopwatch.StartNew();
        TaskRunSummary? result = null;

        try
        {
            // The handler may need its own scoped DbContext / services — resolve it from a
            // fresh scope. The handler instance was registered as scoped in DI.
            using var scope = _sp.CreateScope();
            var scopedHandler = scope.ServiceProvider.GetService(handler.GetType()) as IScheduledTaskHandler
                                ?? handler; // fallback to the captured instance
            result = await scopedHandler.ExecuteAsync(linkedCts.Token);

            sw.Stop();
            run.CompletedAt = DateTime.UtcNow;
            run.DurationMs = (int)sw.ElapsedMilliseconds;
            run.Status = ScheduledTaskStatus.Success;
            run.ItemsChecked = result.ItemsChecked;
            run.ItemsUpdated = result.ItemsUpdated;
            run.Output = result.Output;

            task.LastStatus = ScheduledTaskStatus.Success;
            task.LastSuccessAt = run.CompletedAt;
            task.LastError = null;
            task.LastDurationMs = run.DurationMs;
            task.LastItemsChecked = result.ItemsChecked;
            task.LastItemsUpdated = result.ItemsUpdated;
            task.NextRunAt = run.CompletedAt.Value.AddSeconds(task.IntervalSeconds);

            _log.LogInformation("ScheduledTask {Key} OK Trigger={Trigger} Actor={Actor} DurationMs={Duration} Checked={Checked} Updated={Updated}",
                task.TaskKey, trigger, actorName ?? "system", run.DurationMs, run.ItemsChecked, run.ItemsUpdated);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            sw.Stop();
            run.CompletedAt = DateTime.UtcNow;
            run.DurationMs = (int)sw.ElapsedMilliseconds;
            run.Status = ScheduledTaskStatus.Failed;
            run.Error = $"Timed out after {task.TimeoutSeconds}s";

            task.LastStatus = ScheduledTaskStatus.Failed;
            task.LastError = run.Error;
            task.LastDurationMs = run.DurationMs;
            task.NextRunAt = run.CompletedAt.Value.AddSeconds(task.IntervalSeconds);
            if (task.StopOnError) task.Enabled = false;
            _log.LogWarning("ScheduledTask {Key} TIMEOUT after {Seconds}s", task.TaskKey, task.TimeoutSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            run.CompletedAt = DateTime.UtcNow;
            run.DurationMs = (int)sw.ElapsedMilliseconds;
            run.Status = ScheduledTaskStatus.Failed;
            // Unwrap inner exceptions — EF's DbUpdateException wraps the real DB error (e.g. the
            // Postgres message naming the offending column/constraint) in InnerException. Without this
            // the history only shows the generic "An error occurred while saving the entity changes".
            var head = FlattenException(ex);
            if (head.Length > 800) head = head[..800];
            run.Error = head;

            task.LastStatus = ScheduledTaskStatus.Failed;
            task.LastError = head;
            task.LastDurationMs = run.DurationMs;
            task.NextRunAt = run.CompletedAt.Value.AddSeconds(task.IntervalSeconds);
            if (task.StopOnError) task.Enabled = false;
            _log.LogError(ex, "ScheduledTask {Key} FAILED Trigger={Trigger}", task.TaskKey, trigger);
        }

        await _db.SaveChangesAsync(outerCt);
        return result;
    }

    /// <summary>Concatenates an exception's message chain (outer → inner) so DB errors wrapped by EF's
    /// DbUpdateException surface the underlying Postgres message rather than the generic wrapper text.</summary>
    private static string FlattenException(Exception ex)
    {
        var parts = new List<string>();
        var cur = ex;
        var guard = 0;
        while (cur != null && guard++ < 5)
        {
            parts.Add(cur.Message);
            cur = cur.InnerException;
        }
        return string.Join(" → ", parts);
    }
}
