using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// Sends installment due-date reminders. Delegates to <see cref="IInstallmentService.SendDueRemindersAsync"/>,
/// which finds unpaid installments coming due within the configured window that haven't been
/// reminded yet, sends the customer-facing reminder, and stamps ReminderSentAt to avoid duplicates.
/// </summary>
public sealed class InstallmentReminderTask : IScheduledTaskHandler
{
    public string Key => "InstallmentReminder";
    public string DefaultName => "Installment Reminders";
    public string DefaultDescription => "Sends reminders before installment due dates for counter orders.";
    public int DefaultIntervalSeconds => 6 * 60 * 60; // every 6 hours

    private readonly IInstallmentService _installments;
    private readonly ILogger<InstallmentReminderTask> _log;

    public InstallmentReminderTask(IInstallmentService installments, ILogger<InstallmentReminderTask> log)
    {
        _installments = installments;
        _log = log;
    }

    public async Task<TaskRunSummary> ExecuteAsync(CancellationToken ct)
    {
        var sent = await _installments.SendDueRemindersAsync(ct);
        _log.LogInformation("InstallmentReminder sent {Count} reminder(s)", sent);
        return new TaskRunSummary(sent, sent, $"{sent} installment reminder(s) sent");
    }
}
