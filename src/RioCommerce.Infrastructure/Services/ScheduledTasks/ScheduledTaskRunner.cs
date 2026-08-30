using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// BackgroundService that ticks every 30 seconds, finds tasks whose <c>NextRunAt</c> has
/// passed, and executes them via <see cref="ScheduledTaskService.ExecuteAsync"/>.
/// Independent of any HTTP request — runs even when no admin is signed in.
///
/// One tick processes all due tasks SEQUENTIALLY (so a slow task doesn't pile up
/// concurrent DbContexts). A task that exceeds its <c>TimeoutSeconds</c> cancels itself
/// — see ScheduledTaskService for the linked-token plumbing.
/// </summary>
public sealed class ScheduledTaskRunner : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WarmupDelay  = TimeSpan.FromSeconds(45);

    private readonly IServiceProvider _sp;
    private readonly ILogger<ScheduledTaskRunner> _log;

    public ScheduledTaskRunner(IServiceProvider sp, ILogger<ScheduledTaskRunner> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the app to finish booting (migrations apply, seed runs) before we start polling.
        try { await Task.Delay(WarmupDelay, stoppingToken); } catch (OperationCanceledException) { return; }

        _log.LogInformation("ScheduledTaskRunner started — tick every {Seconds}s", TickInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex) { _log.LogError(ex, "ScheduledTaskRunner tick threw"); }

            try { await Task.Delay(TickInterval, stoppingToken); } catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("ScheduledTaskRunner stopped");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RioCommerceDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        var now = DateTime.UtcNow;
        var due = await db.ScheduledTasks
            .Where(t => t.Enabled && (t.NextRunAt == null || t.NextRunAt <= now))
            .ToListAsync(ct);

        foreach (var task in due)
        {
            if (ct.IsCancellationRequested) break;
            await svc.ExecuteAsync(task, trigger: "schedule", actorName: null, ct);
        }
    }
}
