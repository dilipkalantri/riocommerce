using RioCommerce.Core.DTOs.Checkout;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.ScheduledTasks;

/// <summary>
/// Polls every payment gateway for orders that our server marks as Pending — typically
/// because the customer's browser died between gateway success and our callback. Settles
/// the order through the same idempotent <c>CompleteOrderAsync</c> path the webhook uses,
/// so invoice generation / enrollment activation / admin notification all flow through
/// the same single source of truth and stay duplicate-free.
///
/// <para><b>DRY RUN.</b> Set <c>Payments:ReconciliationDryRun=true</c> and the task still polls
/// every gateway and still reports exactly what it found, but performs <b>no writes of any
/// kind</b> — no settlement, no invoice, no enrollment, no email, no commission, no status
/// change, not even an audit row. Use it when pointing the app at a restored backup or any
/// database whose orders must not be acted on, and to preview a reconciliation before
/// authorising it. The per-order verdicts land in the run's Output and in the log.</para>
/// </summary>
public sealed class PaymentStatusSyncTask : IScheduledTaskHandler
{
    public string Key => "PaymentStatusSync";
    public string DefaultName => "Payment Status Synchronization";
    public string DefaultDescription => "Polls Razorpay and Easebuzz for pending orders and settles any that completed at the gateway.";
    public int DefaultIntervalSeconds => 3600; // hourly

    private static readonly TimeSpan LookbackWindow = TimeSpan.FromDays(7);

    private readonly RioCommerceDbContext _db;
    private readonly ICheckoutService _checkout;
    private readonly RazorpayGateway _razorpay;
    private readonly EasebuzzGateway _easebuzz;
    private readonly IAuditService _audit;
    private readonly ILogger<PaymentStatusSyncTask> _log;
    private readonly IConfiguration _config;

    public PaymentStatusSyncTask(RioCommerceDbContext db, ICheckoutService checkout,
        RazorpayGateway razorpay, EasebuzzGateway easebuzz,
        IAuditService audit, ILogger<PaymentStatusSyncTask> log, IConfiguration config)
    {
        _db = db;
        _checkout = checkout;
        _razorpay = razorpay;
        _easebuzz = easebuzz;
        _audit = audit;
        _log = log;
        _config = config;
    }

    /// <summary>Read fresh on every run, so the switch can be flipped without a redeploy.</summary>
    private bool DryRun => _config.GetValue<bool>("Payments:ReconciliationDryRun");

    public async Task<TaskRunSummary> ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - LookbackWindow;
        var pending = await _db.Orders
            .Include(o => o.Payments)
            .Where(o => o.PaymentStatus == PaymentStatus.Pending
                     && o.PaymentMode != null
                     && o.PaymentMode != PaymentMode.Cash
                     && o.CreatedAt >= cutoff)
            .OrderByDescending(o => o.CreatedAt)
            .Take(200) // hard cap per run so a backlog can't lock us up
            .ToListAsync(ct);

        var updated = 0;
        var settled = 0;
        var failed = 0;
        var cancelled = 0;

        // Snapshot once per run so a mid-run config reload cannot half-apply the guard.
        var dryRun = DryRun;
        var report = new List<string>();
        if (dryRun)
            _log.LogWarning("PaymentStatusSync running in DRY RUN — {Count} pending order(s) will be polled and reported, nothing will be written.", pending.Count);

        foreach (var order in pending)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var mode = order.PaymentMode!.Value;
                var payment = order.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
                var gwOrderId = payment?.GatewayOrderId;
                if (string.IsNullOrWhiteSpace(gwOrderId))
                {
                    _log.LogDebug("PaymentStatusSync skip — no GatewayOrderId OrderNumber={OrderNumber}", order.OrderNumber);
                    continue;
                }

                GatewayPaymentStatus status = mode switch
                {
                    PaymentMode.Razorpay => await _razorpay.QueryOrderStatusAsync(gwOrderId),
                    PaymentMode.Easebuzz => await _easebuzz.QueryOrderStatusAsync(order.OrderNumber, order.TotalAmount, order.StudentEmail ?? string.Empty, order.StudentPhone ?? string.Empty),
                    _ => new GatewayPaymentStatus(GatewayPaymentState.Unknown, null, null, null, "unsupported gateway"),
                };

                _log.LogInformation("PaymentStatusSync polled OrderNumber={OrderNumber} Gateway={Gateway} GwOrderId={GwOrderId} State={State} Raw={Raw} PaymentMode={PaymentMode}",
                    order.OrderNumber, mode, gwOrderId, status.State, status.RawStatus, status.Instrument?.Mode);

                var oldPaymentStatus = order.PaymentStatus;
                var oldOrderStatus = order.Status;

                // ── DRY RUN: report the verdict and move on, WITHOUT entering the switch ──
                // The guard sits here rather than inside each branch on purpose: every write in
                // this task lives below this line, so there is exactly one thing to be sure of
                // rather than four. Nothing here mutates the context, saves, settles or audits.
                if (dryRun)
                {
                    var wouldDo = status.State switch
                    {
                        GatewayPaymentState.Success   => "WOULD SETTLE (invoice + enrollment + email + commission)",
                        GatewayPaymentState.Failed    => "would mark payment Failed",
                        GatewayPaymentState.Cancelled => "would mark order Cancelled",
                        _                             => "no action",
                    };
                    var line = $"{order.OrderNumber} · {mode} · {order.TotalAmount:0.00} · gateway={status.State}"
                             + $"{(status.RawStatus is { Length: > 0 } r ? $" ({r})" : "")}"
                             + $"{(status.GatewayPaymentId is { Length: > 0 } p ? $" · {p}" : "")} → {wouldDo}";
                    report.Add(line);
                    _log.LogInformation("PaymentStatusSync DRY RUN {Line}", line);
                    if (status.State == GatewayPaymentState.Success) settled++;
                    else if (status.State == GatewayPaymentState.Failed) failed++;
                    else if (status.State == GatewayPaymentState.Cancelled) cancelled++;
                    continue;
                }

                switch (status.State)
                {
                    case GatewayPaymentState.Success:
                        // Settle through the shared path — idempotent. Generates invoice,
                        // grants enrollments, records affiliate commission, sends order
                        // confirmation, fires admin notification.
                        // The poll response carries the instrument the customer paid with, so the
                        // reconciled order gets the same payment mode as a live callback would.
                        OrderReceipt? receipt = mode == PaymentMode.Razorpay
                            ? await _checkout.ConfirmFromWebhookAsync(gwOrderId, status.GatewayPaymentId, status.Instrument)
                            : await _checkout.ConfirmByOrderNumberAsync(order.OrderNumber, status.GatewayPaymentId, "scheduled-task", status.Instrument);
                        if (receipt?.PaymentStatus == PaymentStatus.Success)
                        {
                            settled++; updated++;
                            await WriteAuditAsync(order.OrderNumber, mode.ToString(), oldPaymentStatus, PaymentStatus.Success, status.GatewayPaymentId);
                        }
                        break;

                    case GatewayPaymentState.Failed:
                        if (payment != null) payment.Status = PaymentStatus.Failed;
                        order.PaymentStatus = PaymentStatus.Failed;
                        await _db.SaveChangesAsync(ct);
                        failed++; updated++;
                        await WriteAuditAsync(order.OrderNumber, mode.ToString(), oldPaymentStatus, PaymentStatus.Failed, status.GatewayPaymentId);
                        break;

                    case GatewayPaymentState.Cancelled:
                        order.Status = OrderStatus.Cancelled;
                        await _db.SaveChangesAsync(ct);
                        cancelled++; updated++;
                        await WriteAuditAsync(order.OrderNumber, mode.ToString(), oldPaymentStatus, oldPaymentStatus, status.GatewayPaymentId, customAction: "OrderCancelled");
                        break;

                    case GatewayPaymentState.Pending:
                    case GatewayPaymentState.Unknown:
                    default:
                        // Leave alone — this is the normal "not yet" branch.
                        break;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PaymentStatusSync threw on OrderNumber={OrderNumber}", order.OrderNumber);
            }
        }

        if (dryRun)
        {
            // ItemsUpdated stays 0 — nothing was written, and the run history must not imply
            // otherwise. The verdicts go in Output so the report is readable from the admin
            // Scheduled Tasks screen without digging through logs.
            var head = $"DRY RUN — no changes written. {pending.Count} checked · "
                     + $"{settled} would settle · {failed} would fail · {cancelled} would cancel";
            _log.LogWarning("PaymentStatusSync {Head}", head);
            return new TaskRunSummary(pending.Count, 0,
                report.Count == 0 ? head : head + "\n" + string.Join("\n", report));
        }

        var summary = $"{pending.Count} checked · {settled} settled · {failed} failed · {cancelled} cancelled";
        return new TaskRunSummary(pending.Count, updated, summary);
    }

    private async Task WriteAuditAsync(string orderNumber, string gateway, PaymentStatus oldStatus, PaymentStatus newStatus, string? paymentId, string? customAction = null)
    {
        try
        {
            await _audit.WriteAsync(new AuditEntry
            {
                ActorUserId = null,
                ActorName = "ScheduledTask:PaymentStatusSync",
                Module = "Payments",
                Action = customAction ?? "PaymentStatusReconciled",
                EntityType = "Order",
                EntityId = orderNumber,
                EntityName = $"Order {orderNumber}",
                OldValue = oldStatus.ToString(),
                NewValue = newStatus.ToString(),
                Status = "Success",
                Details = string.IsNullOrEmpty(paymentId) ? gateway : $"{gateway} · {paymentId}",
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PaymentStatusSync audit write failed OrderNumber={OrderNumber}", orderNumber);
        }
    }
}
