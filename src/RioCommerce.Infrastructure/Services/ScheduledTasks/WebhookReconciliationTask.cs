using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// Faster-cadence sibling of PaymentStatusSync. Same logic, half-hourly. The shared
/// idempotency in CheckoutService.CompleteOrderAsync means it can run alongside the
/// hourly sync without risk of duplicate invoices or notifications — a later run
/// against an already-settled order is a no-op.
/// </summary>
public sealed class WebhookReconciliationTask : IScheduledTaskHandler
{
    public string Key => "WebhookReconciliation";
    public string DefaultName => "Payment Webhook Reconciliation";
    public string DefaultDescription => "Faster-cadence poll for missed webhooks across all gateways.";
    public int DefaultIntervalSeconds => 1800; // 30 minutes

    private readonly PaymentStatusSyncTask _inner;

    public WebhookReconciliationTask(PaymentStatusSyncTask inner) => _inner = inner;

    // Delegates to the same poller — the only thing that changes is the seeded cadence.
    public Task<TaskRunSummary> ExecuteAsync(CancellationToken ct) => _inner.ExecuteAsync(ct);
}
