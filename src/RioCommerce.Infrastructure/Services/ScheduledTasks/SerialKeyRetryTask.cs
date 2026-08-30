using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// Drives the serial-key dispatch loop. Picks up <c>Pending</c> + <c>Failed</c> records
/// whose <c>NextRetryAt</c> has come due and asks <see cref="ISerialKeyService.ProcessDueAsync"/>
/// to process a batch. Seeded automatically on first boot by the scheduled-task seed loop
/// in <c>Program.cs</c>, keyed off <see cref="Key"/>.
/// </summary>
public sealed class SerialKeyRetryTask : IScheduledTaskHandler
{
    public string Key => "SerialKeyRetry";
    public string DefaultName => "Serial Key Dispatch & Retry";
    public string DefaultDescription => "Generates external serial keys for paid orders and retries failed attempts with exponential backoff.";
    public int DefaultIntervalSeconds => 60;        // every minute is fine — most batches will be empty

    private const int BatchSize = 100;

    private readonly ISerialKeyService _svc;
    private readonly ILogger<SerialKeyRetryTask> _log;

    public SerialKeyRetryTask(ISerialKeyService svc, ILogger<SerialKeyRetryTask> log)
    {
        _svc = svc;
        _log = log;
    }

    public async Task<TaskRunSummary> ExecuteAsync(CancellationToken ct)
    {
        var (processed, generated, failed, dead) = await _svc.ProcessDueAsync(BatchSize, ct);
        if (processed > 0)
            _log.LogInformation("SerialKeyRetry processed={Processed} generated={Generated} failed={Failed} deadLettered={Dead}",
                processed, generated, failed, dead);
        return new TaskRunSummary(
            ItemsChecked: processed,
            ItemsUpdated: generated + failed + dead,
            Output: processed == 0 ? "No due records." : $"generated={generated} failed={failed} dead-lettered={dead}");
    }
}
