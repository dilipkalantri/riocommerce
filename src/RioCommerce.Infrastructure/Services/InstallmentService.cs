using RioCommerce.Core.DTOs.Installments;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

public class InstallmentService : IInstallmentService
{
    private readonly RioCommerceDbContext _db;
    private readonly IInvoiceService _invoices;
    private readonly INotificationService _notify;
    private readonly INotificationCenterService _center;
    private readonly IRealtimeBus _bus;
    private readonly IAuditService _audit;
    private readonly ILogger<InstallmentService> _log;

    public InstallmentService(
        RioCommerceDbContext db, IInvoiceService invoices, INotificationService notify,
        INotificationCenterService center, IRealtimeBus bus, IAuditService audit,
        ILogger<InstallmentService> log)
    {
        _db = db; _invoices = invoices; _notify = notify; _center = center;
        _bus = bus; _audit = audit; _log = log;
    }

    private const decimal Tolerance = 1m;   // ₹1 rounding tolerance, matching CreateOrderV2Async

    // Npgsql requires Kind=Utc for 'timestamp with time zone' columns. Dates arriving from the UI
    // can be Unspecified (e.g. from DateTime.Date or an <input type="date">), which throws on save.
    // Coerce defensively: treat Unspecified as UTC, convert Local to UTC, leave Utc as-is.
    private static DateTime ToUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };

    // ── Validation ────────────────────────────────────────────────────────────
    public async Task<string?> ValidatePlanAsync(decimal orderTotal, InstallmentPlanInput input, CancellationToken ct = default)
    {
        if (input is null || !input.Enabled) return null;   // not an installment order — nothing to validate

        var s = await GetSettingsEntityAsync(ct);
        if (!s.AllowInstallments) return "Installment payments are disabled in settings.";

        if (input.DownPayment < 0) return "Down payment can't be negative.";
        if (input.Installments is null || input.Installments.Count == 0)
            return "Add at least one installment.";
        if (input.Installments.Any(i => i.Amount <= 0))
            return "Every installment amount must be greater than zero.";

        // Minimum down payment (percentage of the order total).
        if (s.MinDownPaymentPercent > 0)
        {
            var minDown = Math.Round(orderTotal * s.MinDownPaymentPercent / 100m, 2);
            if (input.DownPayment + Tolerance < minDown)
                return $"Down payment must be at least ₹{minDown:N0} ({s.MinDownPaymentPercent:0.##}% of ₹{orderTotal:N0}).";
        }

        // Down payment can't exceed the total.
        if (input.DownPayment > orderTotal + Tolerance)
            return $"Down payment (₹{input.DownPayment:N0}) can't exceed the order total (₹{orderTotal:N0}).";

        // Down payment + all installments must equal the order total.
        var scheduleSum = input.DownPayment + input.Installments.Sum(i => i.Amount);
        if (Math.Abs(scheduleSum - orderTotal) > Tolerance)
            return $"Down payment + installments (₹{scheduleSum:N0}) must equal the order total (₹{orderTotal:N0}).";

        return null;
    }

    // ── Create plan ─────────────────────────────────────────────────────────────
    public async Task<(bool ok, string? error)> CreatePlanAsync(
        Guid orderId, InstallmentPlanInput input, Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        if (input is null || !input.Enabled) return (true, null);   // no plan requested

        var order = await _db.Orders.IgnoreQueryFilters()
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return (false, "Order not found.");

        if (await _db.OrderInstallmentPlans.AnyAsync(p => p.OrderId == orderId, ct))
            return (false, "This order already has an installment plan.");

        var err = await ValidatePlanAsync(order.TotalAmount, input, ct);
        if (err != null) return (false, err);

        var plan = new OrderInstallmentPlan
        {
            OrderId = orderId,
            TotalAmount = order.TotalAmount,
            DownPayment = input.DownPayment,
            InstallmentCount = input.Installments.Count,
            PaidAmount = input.DownPayment,                  // down payment is collected now
            IsCompleted = false,
        };

        var n = 1;
        foreach (var line in input.Installments.OrderBy(i => i.DueDate))
        {
            plan.Installments.Add(new OrderInstallment
            {
                OrderId = orderId,
                InstallmentNumber = n++,
                DueDate = ToUtc(line.DueDate),
                Amount = line.Amount,
                PaidAmount = 0,
                Status = InstallmentStatus.Pending,
            });
        }

        _db.OrderInstallmentPlans.Add(plan);

        // The order is a partial-payment order: keep PaymentStatus at Pending (no invoice yet) and
        // surface it as "Partially Paid" in the UI via the plan. Access is granted on the down
        // payment (handled by the caller via Status=Confirmed + enrollment activation), so we leave
        // OrderStatus as the caller set it (Confirmed/Activated) and only manage payment completion.
        order.PaymentStatus = PaymentStatus.Pending;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId, ActorName = actorName ?? "system",
            Module = "Orders", Action = "InstallmentPlanCreated",
            EntityType = "Order", EntityId = orderId.ToString(), EntityName = order.OrderNumber,
            Status = "Success",
            Details = $"Down ₹{input.DownPayment:N0} + {input.Installments.Count} installment(s) · total ₹{order.TotalAmount:N0}",
        });

        return (true, null);
    }

    // ── Plan view ─────────────────────────────────────────────────────────────
    public async Task<InstallmentPlanView?> GetPlanAsync(Guid orderId, CancellationToken ct = default)
    {
        var plan = await _db.OrderInstallmentPlans.AsNoTracking()
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (plan == null) return null;

        var order = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var rows = plan.Installments.OrderBy(i => i.InstallmentNumber)
            .Select(i => ToRow(i, now)).ToList();

        var next = plan.Installments
            .Where(i => i.Status != InstallmentStatus.Paid)
            .OrderBy(i => i.InstallmentNumber)
            .Select(i => ToRow(i, now))
            .FirstOrDefault();

        return new InstallmentPlanView
        {
            PlanId = plan.Id,
            OrderId = orderId,
            OrderNumber = order ?? string.Empty,
            TotalAmount = plan.TotalAmount,
            DownPayment = plan.DownPayment,
            PaidAmount = plan.PaidAmount,
            OutstandingAmount = plan.OutstandingAmount,
            IsCompleted = plan.IsCompleted,
            CompletedAt = plan.CompletedAt,
            Installments = rows,
            NextDue = next,
        };
    }

    private static InstallmentRow ToRow(OrderInstallment i, DateTime now) => new(
        i.Id, i.InstallmentNumber, i.DueDate, i.Amount, i.PaidAmount, i.PendingAmount,
        i.Status, i.PaidAt, i.PaidVia, i.PaymentReference,
        IsOverdue: i.Status != InstallmentStatus.Paid && i.DueDate.Date < now.Date);

    // ── Collect an installment ──────────────────────────────────────────────────
    public async Task<CollectInstallmentResult> CollectAsync(
        CollectInstallmentRequest req, Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        if (req.Amount <= 0)
            return new CollectInstallmentResult(false, "Enter an amount greater than zero.", false, 0, null);

        var inst = await _db.OrderInstallments.FirstOrDefaultAsync(i => i.Id == req.InstallmentId, ct);
        if (inst == null) return new CollectInstallmentResult(false, "Installment not found.", false, 0, null);

        if (inst.Status == InstallmentStatus.Paid)
        {
            // Idempotent: already settled — report current plan state, no double charge.
            var paidPlan = await _db.OrderInstallmentPlans.FirstOrDefaultAsync(p => p.Id == inst.PlanId, ct);
            return new CollectInstallmentResult(true, "This installment is already fully paid.",
                paidPlan?.IsCompleted ?? false, paidPlan?.OutstandingAmount ?? 0, null);
        }

        var plan = await _db.OrderInstallmentPlans
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == inst.PlanId, ct);
        if (plan == null) return new CollectInstallmentResult(false, "Installment plan not found.", false, 0, null);

        var order = await _db.Orders.IgnoreQueryFilters()
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == inst.OrderId, ct);
        if (order == null) return new CollectInstallmentResult(false, "Order not found.", false, 0, null);

        // Cap the collection at this installment's pending amount so a typo can't over-collect.
        var apply = Math.Min(req.Amount, inst.PendingAmount);
        var paidAt = ToUtc(req.PaidAt ?? DateTime.UtcNow);

        inst.PaidAmount += apply;
        inst.Status = inst.PaidAmount + Tolerance >= inst.Amount ? InstallmentStatus.Paid : InstallmentStatus.PartiallyPaid;
        if (inst.Status == InstallmentStatus.Paid)
        {
            inst.PaidAt = paidAt;
            inst.PaidVia = req.Mode;
            inst.PaymentReference = req.Reference;
        }

        plan.PaidAmount += apply;

        // Money trail: a Payment row (mirrors the split-payment model) + a PaymentTransaction ledger row.
        order.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Amount = apply,
            PaymentMode = req.Mode,
            Status = PaymentStatus.Success,
            BankRef = req.Reference,
            PaidAt = paidAt,
        });
        _db.PaymentTransactions.Add(new PaymentTransaction
        {
            OrderId = order.Id,
            Gateway = "Counter",
            Status = PaymentStatus.Success,
            Amount = apply,
            PaymentMethod = req.Mode,
            PaidOnUtc = paidAt,
            Reference = req.Reference,
            CreatedByName = actorName ?? "admin",
            CreatedById = actorId,
            Notes = $"Installment #{inst.InstallmentNumber} collection",
        });

        // Final installment? Complete the plan, flip the order to paid, and let the invoice generate.
        var fullyPaid = plan.PaidAmount + Tolerance >= plan.TotalAmount
                        && plan.Installments.All(i => i.Status == InstallmentStatus.Paid);

        Guid? invoiceId = null;
        if (fullyPaid)
        {
            plan.IsCompleted = true;
            plan.CompletedAt = paidAt;
            order.PaymentStatus = PaymentStatus.Success;
            order.Status = OrderStatus.Delivered;        // "Completed" in the wizard's vocabulary
            order.ConfirmedAt ??= paidAt;
            order.ActivatedAt ??= paidAt;
        }

        await _db.SaveChangesAsync(ct);

        // Generate the FINAL tax invoice only now (idempotent; gated on PaymentStatus.Success).
        if (fullyPaid)
        {
            try
            {
                var (newId, existingId, _) = await _invoices.EnsureForOrderAsync(order.Id, actorId, actorName ?? "installments", ct);
                invoiceId = newId ?? existingId;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Final invoice generation failed for installment order {OrderNumber}", order.OrderNumber);
            }
        }

        // Notifications: payment received + (on completion) final invoice ready.
        await SafeNotifyAsync("installment_paid", order, new Dictionary<string, string>
        {
            ["name"] = order.StudentName,
            ["order_number"] = order.OrderNumber,
            ["amount"] = apply.ToString("N0"),
            ["installment_number"] = inst.InstallmentNumber.ToString(),
            ["paid"] = plan.PaidAmount.ToString("N0"),
            ["outstanding"] = plan.OutstandingAmount.ToString("N0"),
        });
        if (fullyPaid)
        {
            await SafeNotifyAsync("installment_completed", order, new Dictionary<string, string>
            {
                ["name"] = order.StudentName,
                ["order_number"] = order.OrderNumber,
                ["total"] = plan.TotalAmount.ToString("N0"),
            });
            await _center.NotifyAsync(AdminNotificationType.PaymentSuccess, NotificationSeverity.Success,
                "Installment plan completed",
                $"Order #{order.OrderNumber} fully paid (₹{plan.TotalAmount:N0}). Final invoice generated.",
                $"/admin/orders/{order.Id}", order.Id.ToString());
        }
        else
        {
            await _center.NotifyAsync(AdminNotificationType.PaymentSuccess, NotificationSeverity.Info,
                "Installment collected",
                $"₹{apply:N0} collected on order #{order.OrderNumber}. ₹{plan.OutstandingAmount:N0} remaining.",
                $"/admin/orders/{order.Id}", order.Id.ToString());
        }

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId, ActorName = actorName ?? "system",
            Module = "Orders", Action = "InstallmentCollected",
            EntityType = "Order", EntityId = order.Id.ToString(), EntityName = order.OrderNumber,
            Status = "Success",
            Details = $"Installment #{inst.InstallmentNumber} · ₹{apply:N0} via {req.Mode}" + (fullyPaid ? " · PLAN COMPLETED" : ""),
        });

        _bus.PublishDataChanged(new RealtimeEvent("orders"));
        _bus.PublishDataChanged(new RealtimeEvent("dashboard"));

        return new CollectInstallmentResult(true, null, fullyPaid, plan.OutstandingAmount, invoiceId);
    }

    private async Task SafeNotifyAsync(string key, Order order, Dictionary<string, string> tokens)
    {
        try { await _notify.SendAsync(key, new NotificationRecipient(order.StudentEmail, order.StudentPhone), tokens); }
        catch (Exception ex) { _log.LogWarning(ex, "Installment notification '{Key}' failed for {OrderNumber}", key, order.OrderNumber); }
    }

    // ── Settings ──────────────────────────────────────────────────────────────
    private async Task<InstallmentSettings> GetSettingsEntityAsync(CancellationToken ct)
        => await _db.InstallmentSettings.FirstOrDefaultAsync(ct) ?? new InstallmentSettings();

    public async Task<InstallmentSettingsModel> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await _db.InstallmentSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new InstallmentSettings();
        return new InstallmentSettingsModel
        {
            AllowInstallments = s.AllowInstallments,
            DefaultInstallmentCount = s.DefaultInstallmentCount,
            MinDownPaymentPercent = s.MinDownPaymentPercent,
            AutoGenerateDueDates = s.AutoGenerateDueDates,
            DefaultIntervalDays = s.DefaultIntervalDays,
            AllowManualModification = s.AllowManualModification,
            ReminderDaysBefore = s.ReminderDaysBefore,
        };
    }

    public async Task SaveSettingsAsync(InstallmentSettingsModel model, CancellationToken ct = default)
    {
        var s = await _db.InstallmentSettings.FirstOrDefaultAsync(ct);
        if (s == null) { s = new InstallmentSettings(); _db.InstallmentSettings.Add(s); }
        s.AllowInstallments = model.AllowInstallments;
        s.DefaultInstallmentCount = Math.Clamp(model.DefaultInstallmentCount, 1, 36);
        s.MinDownPaymentPercent = Math.Clamp(model.MinDownPaymentPercent, 0m, 100m);
        s.AutoGenerateDueDates = model.AutoGenerateDueDates;
        s.DefaultIntervalDays = Math.Clamp(model.DefaultIntervalDays, 1, 365);
        s.AllowManualModification = model.AllowManualModification;
        s.ReminderDaysBefore = Math.Clamp(model.ReminderDaysBefore, 0, 60);
        await _db.SaveChangesAsync(ct);
    }

    // ── Reports ─────────────────────────────────────────────────────────────────
    public async Task<List<InstallmentReportRow>> ReportAsync(InstallmentReportFilter filter, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var plans = _db.OrderInstallmentPlans.AsNoTracking().Include(p => p.Installments).AsQueryable();

        // Order metadata for names/phones.
        var result = new List<InstallmentReportRow>();
        var planList = await plans.ToListAsync(ct);
        var orderIds = planList.Select(p => p.OrderId).ToList();
        var orders = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber, o.StudentName, o.StudentPhone })
            .ToDictionaryAsync(o => o.Id, ct);

        foreach (var p in planList)
        {
            if (!orders.TryGetValue(p.OrderId, out var o)) continue;

            switch (filter.Kind)
            {
                case InstallmentReportKind.Completed:
                    if (!p.IsCompleted) continue;
                    if (filter.StartDate is { } cs && (p.CompletedAt == null || p.CompletedAt < cs)) continue;
                    if (filter.EndDate is { } ce && (p.CompletedAt == null || p.CompletedAt > ce)) continue;
                    result.Add(new InstallmentReportRow(p.OrderId, o.OrderNumber, o.StudentName, o.StudentPhone,
                        null, p.CompletedAt, p.TotalAmount, p.PaidAmount, p.OutstandingAmount, null,
                        p.CompletedAt, null, p.TotalAmount, p.OutstandingAmount));
                    break;

                case InstallmentReportKind.CollectionHistory:
                    foreach (var i in p.Installments.Where(x => x.PaidAmount > 0).OrderBy(x => x.PaidAt))
                    {
                        if (filter.StartDate is { } hs && (i.PaidAt == null || i.PaidAt < hs)) continue;
                        if (filter.EndDate is { } he && (i.PaidAt == null || i.PaidAt > he)) continue;
                        result.Add(new InstallmentReportRow(p.OrderId, o.OrderNumber, o.StudentName, o.StudentPhone,
                            i.InstallmentNumber, i.DueDate, i.Amount, i.PaidAmount, i.PendingAmount, i.Status,
                            i.PaidAt, i.PaidVia, p.TotalAmount, p.OutstandingAmount));
                    }
                    break;

                case InstallmentReportKind.Overdue:
                    foreach (var i in p.Installments.Where(x => x.Status != InstallmentStatus.Paid && x.DueDate.Date < now.Date).OrderBy(x => x.DueDate))
                        result.Add(new InstallmentReportRow(p.OrderId, o.OrderNumber, o.StudentName, o.StudentPhone,
                            i.InstallmentNumber, i.DueDate, i.Amount, i.PaidAmount, i.PendingAmount, i.Status,
                            i.PaidAt, i.PaidVia, p.TotalAmount, p.OutstandingAmount));
                    break;

                default: // Pending
                    if (p.IsCompleted) continue;
                    foreach (var i in p.Installments.Where(x => x.Status != InstallmentStatus.Paid).OrderBy(x => x.DueDate))
                    {
                        if (filter.StartDate is { } ps && i.DueDate < ps) continue;
                        if (filter.EndDate is { } pe && i.DueDate > pe) continue;
                        result.Add(new InstallmentReportRow(p.OrderId, o.OrderNumber, o.StudentName, o.StudentPhone,
                            i.InstallmentNumber, i.DueDate, i.Amount, i.PaidAmount, i.PendingAmount, i.Status,
                            i.PaidAt, i.PaidVia, p.TotalAmount, p.OutstandingAmount));
                    }
                    break;
            }
        }

        return filter.Kind == InstallmentReportKind.CollectionHistory
            ? result.OrderByDescending(r => r.PaidAt).ToList()
            : result.OrderBy(r => r.DueDate).ToList();
    }

    // ── Reminders (scheduled task) ───────────────────────────────────────────────
    public async Task<int> SendDueRemindersAsync(CancellationToken ct = default)
    {
        var s = await GetSettingsEntityAsync(ct);
        if (s.ReminderDaysBefore <= 0) return 0;

        var now = DateTime.UtcNow;
        var windowEnd = now.AddDays(s.ReminderDaysBefore);

        // Unpaid installments due within the window that we haven't reminded for yet.
        var due = await _db.OrderInstallments
            .Where(i => i.Status != InstallmentStatus.Paid
                        && i.ReminderSentAt == null
                        && i.DueDate >= now && i.DueDate <= windowEnd)
            .OrderBy(i => i.DueDate)
            .Take(200)
            .ToListAsync(ct);
        if (due.Count == 0) return 0;

        var orderIds = due.Select(i => i.OrderId).Distinct().ToList();
        var orders = await _db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, ct);

        var sent = 0;
        foreach (var i in due)
        {
            if (!orders.TryGetValue(i.OrderId, out var o)) continue;
            try
            {
                await _notify.SendAsync("installment_reminder",
                    new NotificationRecipient(o.StudentEmail, o.StudentPhone),
                    new Dictionary<string, string>
                    {
                        ["name"] = o.StudentName,
                        ["order_number"] = o.OrderNumber,
                        ["installment_number"] = i.InstallmentNumber.ToString(),
                        ["amount"] = i.PendingAmount.ToString("N0"),
                        ["due_date"] = i.DueDate.ToLocalTime().ToString("dd MMM yyyy"),
                    });
                i.ReminderSentAt = now;
                sent++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Installment reminder failed for order {OrderNumber} installment #{Num}", o.OrderNumber, i.InstallmentNumber);
            }
        }

        if (sent > 0) await _db.SaveChangesAsync(ct);
        return sent;
    }
}
