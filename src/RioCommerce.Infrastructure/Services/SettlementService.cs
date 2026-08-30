using System.Globalization;
using System.Text;
using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

// Settlement-batch lifecycle + reconciliation. Generation delegates the maths to IPayoutCalculationService;
// large/adjustment-heavy batches route through the shared approval engine; payment goes through IPayoutProvider.
// Every state change is audited and broadcast on the "payouts" realtime scope.
public class SettlementService : ISettlementService
{
    private const string BatchEntity = "SettlementBatch";
    private const decimal DefaultApprovalThreshold = 50000m;
    private const decimal AdjustmentHeavyRatio = 0.25m;   // adjustments+clawbacks above 25% of gross → approval

    private static readonly SettlementStatus[] ActiveBatch =
        { SettlementStatus.Draft, SettlementStatus.PendingApproval, SettlementStatus.Approved, SettlementStatus.Processing };
    private static readonly PayoutStatus[] OwedPayout =
        { PayoutStatus.Draft, PayoutStatus.PendingApproval, PayoutStatus.Approved, PayoutStatus.Processing };

    private readonly RioCommerceDbContext _db;
    private readonly IPayoutCalculationService _calc;
    private readonly IApprovalWorkflowService _approvals;
    private readonly IPayoutProvider _provider;
    private readonly IAuditService _audit;
    private readonly INotificationCenterService _notify;
    private readonly IRealtimeBus _bus;
    private readonly ISettingService _settings;

    public SettlementService(RioCommerceDbContext db, IPayoutCalculationService calc, IApprovalWorkflowService approvals,
        IPayoutProvider provider, IAuditService audit, INotificationCenterService notify, IRealtimeBus bus, ISettingService settings)
    {
        _db = db; _calc = calc; _approvals = approvals; _provider = provider; _audit = audit; _notify = notify; _bus = bus; _settings = settings;
    }

    // ── Generation ──
    public async Task<(bool ok, string? error, Guid? batchId)> GenerateBatchAsync(PayoutType type, DateTime fromUtc, DateTime toUtc, Guid? actorId, string actorName)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        if (toUtc <= fromUtc) return (false, "End of period must be after the start.", null);

        var minPayout = await _settings.GetDecimalAsync(FinanceSettingsService.MinimumPayoutAmount, 0m);
        var preview = await _calc.PreviewAsync(type, fromUtc, toUtc, excludeAlreadySettled: true);
        // financial safety: never settle a zero/negative net, and honour the configured minimum payout.
        var payable = preview.Where(p => p.Net > 0 && p.Net >= minPayout).ToList();
        if (payable.Count == 0) return (false, "No payable beneficiaries for this period (after refunds, minimum payout and prior settlements).", null);

        var batch = new SettlementBatch
        {
            BatchNumber = await NextBatchNumberAsync(type),
            BeneficiaryType = type,
            Status = SettlementStatus.Draft,
            PeriodStartUtc = fromUtc,
            PeriodEndUtc = toUtc,
            CreatedById = actorId,
            CreatedByName = actorName,
            Provider = _provider.Name
        };

        foreach (var p in payable)
        {
            batch.Payouts.Add(new Payout
            {
                BeneficiaryType = type, BeneficiaryId = p.BeneficiaryId, BeneficiaryName = p.BeneficiaryName,
                GrossAmount = p.Gross, Adjustments = 0m, RefundAdjustments = p.RefundClawback, TaxDeduction = p.TaxDeduction,
                NetAmount = p.Net, Status = PayoutStatus.Draft, PeriodStartUtc = fromUtc, PeriodEndUtc = toUtc,
                Items = p.Lines.Select(l => new PayoutItem
                {
                    Source = l.Source, OrderId = l.OrderId, OrderNumber = l.OrderNumber, OrderItemId = l.OrderItemId,
                    RefundId = l.RefundId, SharingRuleId = l.SharingRuleId, Description = l.Description, Amount = l.Amount
                }).ToList()
            });
        }
        RecomputeBatchTotals(batch);

        _db.SettlementBatches.Add(batch);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "SettlementBatchGenerated", BatchEntity, batch.Id.ToString(),
            JsonSerializer.Serialize(new { batch.BatchNumber, type = type.ToString(), batch.BeneficiaryCount, batch.TotalNet }));
        await _notify.NotifyAsync(AdminNotificationType.FranchisePayout, NotificationSeverity.Info,
            "Settlement batch created", $"{batch.BatchNumber}: {batch.BeneficiaryCount} {type} payouts, ₹{batch.TotalNet:N0}.", "/admin/settlements", batch.Id.ToString());
        _bus.PublishDataChanged(new RealtimeEvent("payouts"));
        return (true, null, batch.Id);
    }

    private async Task<string> NextBatchNumberAsync(PayoutType type)
    {
        var code = type switch { PayoutType.Faculty => "FAC", PayoutType.Franchise => "FRA", PayoutType.Affiliate => "AFF", _ => "VEN" };
        var prefix = $"STL-{code}-{DateTime.UtcNow:yyyyMMdd}-";
        var todayCount = await _db.SettlementBatches.CountAsync(b => b.BatchNumber.StartsWith(prefix));
        return $"{prefix}{(todayCount + 1):D4}";
    }

    // ── Reads ──
    public async Task<List<SettlementBatchListItem>> ListBatchesAsync(SettlementStatus? status = null, int take = 100)
    {
        var q = _db.SettlementBatches.AsQueryable();
        if (status.HasValue) q = q.Where(b => b.Status == status);
        return await q.OrderByDescending(b => b.CreatedAt).Take(take).Select(BatchProject).ToListAsync();
    }

    public async Task<SettlementBatchDetailDto?> GetBatchAsync(Guid batchId)
    {
        var batch = await _db.SettlementBatches.Where(b => b.Id == batchId).Select(BatchProject).FirstOrDefaultAsync();
        if (batch == null) return null;
        var payouts = await _db.Payouts.Where(p => p.SettlementBatchId == batchId)
            .OrderByDescending(p => p.NetAmount).Select(PayoutProject).ToListAsync();
        var adjustments = await _db.SettlementAdjustments.Where(a => a.SettlementBatchId == batchId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new SettlementAdjustmentDto(a.Id, a.PayoutId, a.Type, a.Amount, a.Reason, a.CreatedByName, a.CreatedAt))
            .ToListAsync();
        return new SettlementBatchDetailDto(batch, payouts, adjustments);
    }

    public async Task<(IReadOnlyList<PayoutListItem> rows, int total)> ListPayoutsAsync(PayoutType? type, PayoutStatus? status, int page, int pageSize)
    {
        var q = _db.Payouts.AsQueryable();
        if (type.HasValue) q = q.Where(p => p.BeneficiaryType == type);
        if (status.HasValue) q = q.Where(p => p.Status == status);
        var total = await q.CountAsync();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var rows = await q.OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(PayoutProject).ToListAsync();
        return (rows, total);
    }

    public async Task<PayoutDashboardDto> GetDashboardAsync()
    {
        var owed = await _db.Payouts.Where(p => OwedPayout.Contains(p.Status))
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Net = g.Sum(x => (decimal?)x.NetAmount) ?? 0 }).FirstOrDefaultAsync();
        var paidLiab = await _db.Payouts.Where(p => p.Status == PayoutStatus.Paid).SumAsync(p => (decimal?)p.NetAmount) ?? 0;
        var upcoming = await _db.SettlementBatches.CountAsync(b => ActiveBatch.Contains(b.Status));

        var paidBatches = await _db.SettlementBatches.CountAsync(b => b.Status == SettlementStatus.Paid);
        var failedBatches = await _db.SettlementBatches.CountAsync(b => b.Status == SettlementStatus.Failed);
        var successRate = (paidBatches + failedBatches) > 0 ? Math.Round((decimal)paidBatches / (paidBatches + failedBatches) * 100, 1) : 100m;

        var payable = await _calc.OutstandingPayableAsync();
        return new PayoutDashboardDto(
            owed?.Count ?? 0, owed?.Net ?? 0, upcoming, owed?.Net ?? 0, paidLiab, successRate, payable);
    }

    // ── Lifecycle ──
    private async Task<bool> NeedsApprovalAsync(SettlementBatch b)
    {
        var threshold = await _settings.GetDecimalAsync(FinanceSettingsService.SettlementApprovalThreshold, DefaultApprovalThreshold);
        var adjustmentHeavy = b.TotalGross > 0 &&
            (b.TotalRefundAdjustments + Math.Abs(b.TotalAdjustments)) > AdjustmentHeavyRatio * b.TotalGross;
        return b.TotalNet > threshold || adjustmentHeavy;
    }

    public async Task<(bool ok, string? error)> SubmitForApprovalAsync(Guid batchId, Guid? actorId, string actorName)
    {
        var b = await _db.SettlementBatches.FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return (false, "Batch not found.");
        if (b.Status != SettlementStatus.Draft) return (false, $"Only draft batches can be submitted (this one is {b.Status}).");

        if (await NeedsApprovalAsync(b))
        {
            var approvalType = b.BeneficiaryType switch
            {
                PayoutType.Faculty => ApprovalType.FacultyPayoutApproval,
                PayoutType.Franchise => ApprovalType.FranchisePayoutApproval,
                PayoutType.Affiliate => ApprovalType.AffiliatePayoutApproval,
                _ => ApprovalType.VendorPayoutApproval
            };
            var reqId = await _approvals.CreateRequestAsync(approvalType,
                $"Settlement {b.BatchNumber} — ₹{b.TotalNet:N0}", $"{b.BeneficiaryCount} {b.BeneficiaryType} payouts", b.TotalNet,
                BatchEntity, b.Id, actorId, actorName);
            b.ApprovalRequestId = reqId;
            b.Status = SettlementStatus.PendingApproval;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(actorId, actorName, "SettlementSubmittedForApproval", BatchEntity, b.Id.ToString(),
                JsonSerializer.Serialize(new { b.BatchNumber, b.TotalNet }));
            _bus.PublishDataChanged(new RealtimeEvent("payouts"));
            return (true, null);
        }

        await MarkApprovedAsync(b, null, actorId, actorName, auto: true);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ApproveBatchAsync(Guid batchId, string? notes, Guid? actorId, string actorName)
    {
        var b = await _db.SettlementBatches.FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return (false, "Batch not found.");
        if (b.Status is not (SettlementStatus.Draft or SettlementStatus.PendingApproval))
            return (false, $"This batch cannot be approved (status {b.Status}).");
        // If it's queued for approval, the decision must come through the approvals queue.
        if (b.Status == SettlementStatus.PendingApproval && b.ApprovalRequestId.HasValue)
            return (false, "This batch is awaiting a decision in the approvals queue.");
        await MarkApprovedAsync(b, notes, actorId, actorName, auto: false);
        return (true, null);
    }

    private async Task MarkApprovedAsync(SettlementBatch b, string? notes, Guid? actorId, string actorName, bool auto)
    {
        b.Status = SettlementStatus.Approved;
        b.ApprovedById = actorId;
        b.ApprovedByName = actorName;
        b.ApprovedOnUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes)) b.Notes = notes.Trim();
        foreach (var p in await _db.Payouts.Where(p => p.SettlementBatchId == b.Id && p.Status != PayoutStatus.Cancelled).ToListAsync())
        {
            p.Status = PayoutStatus.Approved; p.ApprovedById = actorId; p.ApprovedByName = actorName; p.ApprovedOnUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, auto ? "SettlementAutoApproved" : "SettlementBatchApproved", BatchEntity, b.Id.ToString(),
            JsonSerializer.Serialize(new { b.BatchNumber, b.TotalNet }));
        _bus.PublishDataChanged(new RealtimeEvent("payouts"));
    }

    public async Task<(bool ok, string? error)> RejectBatchAsync(Guid batchId, string? notes, Guid? actorId, string actorName)
        => await CancelInternalAsync(batchId, notes, actorId, actorName, "SettlementBatchRejected");

    public async Task<(bool ok, string? error)> CancelBatchAsync(Guid batchId, string? notes, Guid? actorId, string actorName)
        => await CancelInternalAsync(batchId, notes, actorId, actorName, "SettlementBatchCancelled");

    private async Task<(bool ok, string? error)> CancelInternalAsync(Guid batchId, string? notes, Guid? actorId, string actorName, string action)
    {
        var b = await _db.SettlementBatches.Include(x => x.Payouts).FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return (false, "Batch not found.");
        if (b.Status == SettlementStatus.Paid) return (false, "A paid batch cannot be cancelled.");
        if (b.Status == SettlementStatus.Cancelled) return (false, "This batch is already cancelled.");

        // Reject any open approval request so it leaves the queue.
        if (b.ApprovalRequestId is Guid reqId)
        {
            var ar = await _db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == reqId);
            if (ar is { Status: ApprovalStatus.Pending }) await _approvals.RejectAsync(reqId, notes, actorId, actorName);
        }

        b.Status = SettlementStatus.Cancelled;
        if (!string.IsNullOrWhiteSpace(notes)) b.Notes = notes.Trim();
        foreach (var p in b.Payouts.Where(p => p.Status != PayoutStatus.Paid)) p.Status = PayoutStatus.Cancelled;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, action, BatchEntity, b.Id.ToString(),
            JsonSerializer.Serialize(new { b.BatchNumber, notes }));
        _bus.PublishDataChanged(new RealtimeEvent("payouts"));
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ResolveBatchApprovalAsync(Guid approvalRequestId, bool approve, string? notes, Guid? actorId, string actorName)
    {
        var ar = await _db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == approvalRequestId);
        if (ar == null || ar.RelatedEntityType != BatchEntity || ar.RelatedEntityId == null)
            return (false, "Settlement approval request not found.");
        if (ar.Status != ApprovalStatus.Pending) return (false, $"This request is already {ar.Status}.");

        var b = await _db.SettlementBatches.FirstOrDefaultAsync(x => x.Id == ar.RelatedEntityId);
        if (b == null) return (false, "Batch not found.");

        if (!approve)
        {
            await _approvals.RejectAsync(approvalRequestId, notes, actorId, actorName);
            return await CancelInternalAsync(b.Id, notes ?? "Rejected by approver", actorId, actorName, "SettlementBatchRejected");
        }

        await _approvals.ApproveAsync(approvalRequestId, notes, actorId, actorName);
        await MarkApprovedAsync(b, notes, actorId, actorName, auto: false);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> MarkPaidAsync(Guid batchId, MarkPaidRequest req, Guid? actorId, string actorName)
    {
        var b = await _db.SettlementBatches.Include(x => x.Payouts).FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return (false, "Batch not found.");
        if (b.Status == SettlementStatus.Paid) return (false, "This batch is already marked paid.");
        if (b.Status != SettlementStatus.Approved) return (false, "Only an approved batch can be marked paid.");

        b.Status = SettlementStatus.Processing;
        b.ProcessedOnUtc = DateTime.UtcNow;
        b.Provider = string.IsNullOrWhiteSpace(req.Provider) ? _provider.Name : req.Provider.Trim();
        await _db.SaveChangesAsync();

        var anyFailed = false;
        foreach (var p in b.Payouts.Where(p => p.Status == PayoutStatus.Approved))
        {
            PayoutProviderResult result;
            try
            {
                result = await _provider.SendAsync(new PayoutInstruction(
                    p.Id, p.BeneficiaryType, p.BeneficiaryName, p.NetAmount, null, null, null, req.PaymentReference));
            }
            catch (Exception ex) { result = new PayoutProviderResult(false, null, ex.Message, PayoutDispatchStatus.Failed); }

            if (result.Success)
            {
                p.Status = PayoutStatus.Paid; p.PaidOnUtc = DateTime.UtcNow; p.ProcessedOnUtc = DateTime.UtcNow;
                p.Provider = b.Provider; p.PaymentReference = result.Reference ?? req.PaymentReference;
                if (!string.IsNullOrWhiteSpace(req.Notes)) p.Notes = req.Notes.Trim();
                if (p.BeneficiaryType == PayoutType.Affiliate) await SettleAffiliateReferralsAsync(p);
            }
            else { p.Status = PayoutStatus.Failed; p.Notes = result.Message; anyFailed = true; }
        }

        b.Status = anyFailed ? SettlementStatus.Failed : SettlementStatus.Paid;
        b.PaidOnUtc = anyFailed ? null : DateTime.UtcNow;
        b.PaymentReference = req.PaymentReference;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, anyFailed ? "SettlementBatchPartiallyFailed" : "SettlementBatchPaid", BatchEntity, b.Id.ToString(),
            JsonSerializer.Serialize(new { b.BatchNumber, b.Provider, req.PaymentReference, b.TotalNet, anyFailed }));
        await _notify.NotifyAsync(AdminNotificationType.FranchisePayout, anyFailed ? NotificationSeverity.Error : NotificationSeverity.Success,
            anyFailed ? "Settlement partially failed" : "Settlement paid",
            $"{b.BatchNumber}: ₹{b.TotalNet:N0} {(anyFailed ? "had failures" : "settled")}.", "/admin/settlements", b.Id.ToString());
        _bus.PublishDataChanged(new RealtimeEvent("payouts"));
        _bus.PublishDataChanged(new RealtimeEvent("dashboard"));
        return anyFailed ? (false, "One or more payouts failed; batch marked Failed.") : (true, null);
    }

    // Mark the affiliate's underlying referrals paid and roll up the affiliate's lifetime total.
    private async Task SettleAffiliateReferralsAsync(Payout p)
    {
        var orderIds = await _db.PayoutItems.Where(i => i.PayoutId == p.Id && i.OrderId != null)
            .Select(i => i.OrderId!.Value).Distinct().ToListAsync();
        if (orderIds.Count == 0) return;
        var referrals = await _db.AffiliateReferrals
            .Where(r => r.AffiliateId == p.BeneficiaryId && orderIds.Contains(r.OrderId) && !r.IsPaid).ToListAsync();
        if (referrals.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var r in referrals) { r.IsPaid = true; r.PaidAt = now; }
        var affiliate = await _db.Affiliates.FirstOrDefaultAsync(a => a.Id == p.BeneficiaryId);
        if (affiliate != null) affiliate.TotalPaid += referrals.Sum(r => r.Commission);
    }

    public async Task<(bool ok, string? error)> RecalculateBatchAsync(Guid batchId, Guid? actorId, string actorName)
    {
        var b = await _db.SettlementBatches.Include(x => x.Payouts).ThenInclude(p => p.Items).FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return (false, "Batch not found.");
        if (b.Status != SettlementStatus.Draft) return (false, "Only draft batches can be recalculated.");

        // Drop existing payouts so their lines don't dedupe-block the recompute, then rebuild from source.
        _db.Payouts.RemoveRange(b.Payouts);
        await _db.SaveChangesAsync();

        var minPayout = await _settings.GetDecimalAsync(FinanceSettingsService.MinimumPayoutAmount, 0m);
        var preview = (await _calc.PreviewAsync(b.BeneficiaryType, b.PeriodStartUtc, b.PeriodEndUtc, excludeAlreadySettled: true))
            .Where(p => p.Net > 0 && p.Net >= minPayout).ToList();
        b.Payouts.Clear();
        foreach (var p in preview)
        {
            b.Payouts.Add(new Payout
            {
                BeneficiaryType = b.BeneficiaryType, BeneficiaryId = p.BeneficiaryId, BeneficiaryName = p.BeneficiaryName,
                GrossAmount = p.Gross, Adjustments = 0m, RefundAdjustments = p.RefundClawback, TaxDeduction = p.TaxDeduction,
                NetAmount = p.Net, Status = PayoutStatus.Draft, PeriodStartUtc = b.PeriodStartUtc, PeriodEndUtc = b.PeriodEndUtc,
                Items = p.Lines.Select(l => new PayoutItem
                {
                    Source = l.Source, OrderId = l.OrderId, OrderNumber = l.OrderNumber, OrderItemId = l.OrderItemId,
                    RefundId = l.RefundId, SharingRuleId = l.SharingRuleId, Description = l.Description, Amount = l.Amount
                }).ToList()
            });
        }
        RecomputeBatchTotals(b);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "SettlementBatchRecalculated", BatchEntity, b.Id.ToString(),
            JsonSerializer.Serialize(new { b.BatchNumber, b.BeneficiaryCount, b.TotalNet }));
        _bus.PublishDataChanged(new RealtimeEvent("payouts"));
        return (true, null);
    }

    public async Task<(bool ok, string? error)> AddAdjustmentAsync(Guid batchId, SettlementAdjustmentRequest req, Guid? actorId, string actorName)
    {
        var b = await _db.SettlementBatches.Include(x => x.Payouts).FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return (false, "Batch not found.");
        if (b.Status != SettlementStatus.Draft) return (false, "Adjustments can only be made on a draft batch.");
        if (req.Amount == 0) return (false, "Adjustment amount cannot be zero.");
        if (string.IsNullOrWhiteSpace(req.Reason)) return (false, "An adjustment reason is required.");

        if (req.PayoutId is Guid pid)
        {
            var payout = b.Payouts.FirstOrDefault(p => p.Id == pid);
            if (payout == null) return (false, "Target payout not found in this batch.");
            if (payout.NetAmount + req.Amount < 0) return (false, "Adjustment would make the payout negative.");
            payout.Adjustments += req.Amount;
            payout.NetAmount = payout.GrossAmount + payout.Adjustments - payout.RefundAdjustments - payout.TaxDeduction;
        }

        _db.SettlementAdjustments.Add(new SettlementAdjustment
        {
            SettlementBatchId = b.Id, PayoutId = req.PayoutId, Type = req.Type, Amount = req.Amount,
            Reason = req.Reason.Trim(), CreatedById = actorId, CreatedByName = actorName
        });
        RecomputeBatchTotals(b);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "SettlementAdjustmentAdded", BatchEntity, b.Id.ToString(),
            JsonSerializer.Serialize(new { b.BatchNumber, req.Type, req.Amount, req.Reason, req.PayoutId }));
        _bus.PublishDataChanged(new RealtimeEvent("payouts"));
        return (true, null);
    }

    // ── Export (bank-transfer / provider ready) ──
    public async Task<(string fileName, string csv)?> ExportBatchCsvAsync(Guid batchId)
    {
        var b = await _db.SettlementBatches.FirstOrDefaultAsync(x => x.Id == batchId);
        if (b == null) return null;
        var payouts = await _db.Payouts.Where(p => p.SettlementBatchId == batchId && p.Status != PayoutStatus.Cancelled)
            .OrderByDescending(p => p.NetAmount).ToListAsync();

        // Faculty carry bank details for the actual transfer file.
        var facultyIds = payouts.Where(p => p.BeneficiaryType == PayoutType.Faculty).Select(p => p.BeneficiaryId).ToList();
        var bank = facultyIds.Count == 0 ? new() : await _db.Faculty.Where(f => facultyIds.Contains(f.Id))
            .Select(f => new { f.Id, f.BankName, f.BankAccount, f.BankIfsc, f.PanNumber }).ToDictionaryAsync(f => f.Id);

        var sb = new StringBuilder();
        sb.AppendLine("BatchNumber,Beneficiary,Type,BankName,Account,IFSC,PAN,Gross,Adjustments,RefundAdj,Tax,Net,Status,Reference");
        foreach (var p in payouts)
        {
            bank.TryGetValue(p.BeneficiaryId, out var bk);
            string F(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            string N(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
            sb.AppendLine(string.Join(',', new[]
            {
                F(b.BatchNumber), F(p.BeneficiaryName), F(p.BeneficiaryType.ToString()),
                F(bk?.BankName), F(bk?.BankAccount), F(bk?.BankIfsc), F(bk?.PanNumber),
                N(p.GrossAmount), N(p.Adjustments), N(p.RefundAdjustments), N(p.TaxDeduction), N(p.NetAmount),
                F(p.Status.ToString()), F(p.PaymentReference)
            }));
        }
        return ($"settlement-{b.BatchNumber}.csv", sb.ToString());
    }

    // ── helpers ──
    private static void RecomputeBatchTotals(SettlementBatch b)
    {
        var live = b.Payouts.Where(p => p.Status != PayoutStatus.Cancelled).ToList();
        b.BeneficiaryCount = live.Count;
        b.TotalGross = live.Sum(p => p.GrossAmount);
        b.TotalAdjustments = live.Sum(p => p.Adjustments);
        b.TotalRefundAdjustments = live.Sum(p => p.RefundAdjustments);
        b.TotalTax = live.Sum(p => p.TaxDeduction);
        b.TotalNet = live.Sum(p => p.NetAmount);
    }

    private static readonly System.Linq.Expressions.Expression<Func<SettlementBatch, SettlementBatchListItem>> BatchProject =
        b => new SettlementBatchListItem(b.Id, b.BatchNumber, b.BeneficiaryType, b.Status, b.PeriodStartUtc, b.PeriodEndUtc,
            b.BeneficiaryCount, b.TotalGross, b.TotalNet, b.ApprovalRequestId, b.CreatedByName, b.CreatedAt);

    private static readonly System.Linq.Expressions.Expression<Func<Payout, PayoutListItem>> PayoutProject =
        p => new PayoutListItem(p.Id, p.BeneficiaryType, p.BeneficiaryName, p.GrossAmount, p.Adjustments, p.RefundAdjustments,
            p.TaxDeduction, p.NetAmount, p.Status, p.SettlementBatchId, p.SettlementBatch == null ? null : p.SettlementBatch.BatchNumber,
            p.PeriodStartUtc, p.PeriodEndUtc, p.CreatedAt, p.PaymentReference);
}
