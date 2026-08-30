using System.Text.Json;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class RefundService : IRefundService
{
    private const string EntityType = "Order";
    private const decimal DefaultApprovalThreshold = 5000m;
    private readonly RioCommerceDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IAuditService _audit;
    private readonly INotificationCenterService _notify;
    private readonly IRealtimeBus _bus;
    private readonly IApprovalWorkflowService _approvals;
    private readonly ISettingService _settings;

    public RefundService(RioCommerceDbContext db, IPaymentGateway gateway, IAuditService audit,
        INotificationCenterService notify, IRealtimeBus bus, IApprovalWorkflowService approvals, ISettingService settings)
    {
        _db = db; _gateway = gateway; _audit = audit; _notify = notify; _bus = bus; _approvals = approvals; _settings = settings;
    }

    // Configurable via Finance Settings (key: finance.refund_approval_threshold); refunds above this need approval.
    private Task<decimal> GetThresholdAsync()
        => _settings.GetDecimalAsync(FinanceSettingsService.RefundApprovalThreshold, DefaultApprovalThreshold);

    public async Task<RefundSummaryDto> GetSummaryAsync(Guid orderId)
    {
        var o = await _db.Orders.IgnoreQueryFilters()
            .Include(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(r => r.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return new RefundSummaryDto();
        return BuildSummary(o);
    }

    private static RefundSummaryDto BuildSummary(Order o)
    {
        var paid = o.PaymentStatus == PaymentStatus.Pending ? 0m : o.TotalAmount;
        var succeeded = o.Refunds.Where(r => r.Status == RefundStatus.Succeeded).ToList();
        var refunded = succeeded.Sum(r => r.Amount);
        // Committed = succeeded + still-pending (awaiting approval) so we never let total refunds exceed paid.
        var committed = o.Refunds.Where(r => r.Status != RefundStatus.Failed).ToList();
        var committedAmount = committed.Sum(r => r.Amount);
        var remaining = Math.Max(0m, paid - committedAmount);

        // qty committed per order item (succeeded or pending) — blocks double item refunds.
        var refundedQty = committed.SelectMany(r => r.Items)
            .GroupBy(i => i.OrderItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var summary = new RefundSummaryDto
        {
            OrderTotal = o.TotalAmount,
            PaidAmount = paid,
            RefundedAmount = refunded,
            RemainingRefundable = remaining,
            CanRefund = paid > 0 && remaining > 0,
            Items = o.Items.Select(i =>
            {
                var rq = refundedQty.TryGetValue(i.Id, out var n) ? n : 0;
                return new RefundableItemDto(i.Id, i.ProductTitle, i.Quantity, rq, Math.Max(0, i.Quantity - rq), i.UnitPrice, i.LineTotal);
            }).ToList(),
            History = o.Refunds.OrderByDescending(r => r.CreatedAt).Select(r => new RefundDto(
                r.Id, r.Amount, r.Reason, r.Status, r.RefundType, r.IsOffline, r.GatewayRefundId, r.FailureReason,
                r.InitiatedByName, r.CreatedAt,
                r.Items.Select(it => new RefundLineDto(it.OrderItemId, it.ProductTitle, it.Quantity, it.Amount)).ToList())).ToList()
        };
        return summary;
    }

    public async Task<(bool ok, string? error, Guid? refundId, bool pendingApproval)> CreateRefundAsync(Guid orderId, RefundRequest req, Guid? actorId, string actorName)
    {
        var o = await _db.Orders.IgnoreQueryFilters()
            .Include(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(r => r.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return (false, "Order not found.", null, false);

        var summary = BuildSummary(o);
        if (summary.PaidAmount <= 0) return (false, "This order has no captured payment to refund.", null, false);
        if (summary.RemainingRefundable <= 0) return (false, "This order has already been fully refunded.", null, false);

        decimal amount;
        var items = new List<RefundItem>();

        if (string.Equals(req.Type, "Item", StringComparison.OrdinalIgnoreCase))
        {
            if (req.Items.Count == 0) return (false, "Select at least one item to refund.", null, false);
            decimal sum = 0;
            foreach (var line in req.Items.Where(l => l.Quantity > 0))
            {
                var oi = o.Items.FirstOrDefault(i => i.Id == line.OrderItemId);
                if (oi == null) return (false, "Item not found on this order.", null, false);
                var refundable = summary.Items.First(s => s.OrderItemId == oi.Id).RefundableQuantity;
                if (line.Quantity > refundable) return (false, $"Cannot refund {line.Quantity} × {oi.ProductTitle} — only {refundable} refundable.", null, false);
                var lineAmount = Math.Round((oi.Quantity > 0 ? oi.LineTotal / oi.Quantity : 0) * line.Quantity, 2);
                sum += lineAmount;
                items.Add(new RefundItem { OrderItemId = oi.Id, ProductTitle = oi.ProductTitle, Quantity = line.Quantity, Amount = lineAmount });
            }
            if (sum <= 0) return (false, "Nothing selected to refund.", null, false);
            amount = sum;
        }
        else if (string.Equals(req.Type, "Full", StringComparison.OrdinalIgnoreCase))
        {
            amount = summary.RemainingRefundable;
        }
        else // Partial / Offline (custom amount)
        {
            amount = Math.Round(req.Amount, 2);
            if (amount <= 0) return (false, "Refund amount must be greater than zero.", null, false);
        }

        if (amount > summary.RemainingRefundable)
            return (false, $"Refund amount ₹{amount:N2} exceeds the remaining refundable balance of ₹{summary.RemainingRefundable:N2}.", null, false);

        var isOffline = req.IsOffline || string.Equals(req.Type, "Offline", StringComparison.OrdinalIgnoreCase);

        // Always create the refund as Pending first — it's the system of record either way.
        var refund = new Refund
        {
            OrderId = orderId, Amount = amount,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
            Status = RefundStatus.Pending, RefundType = req.Type, IsOffline = isOffline,
            Gateway = isOffline ? "Offline" : _gateway.Name,
            InitiatedByName = actorName, InitiatedById = actorId, Items = items
        };
        _db.Refunds.Add(refund);
        await _db.SaveChangesAsync();

        var threshold = await GetThresholdAsync();
        if (amount > threshold)
        {
            await _approvals.CreateRequestAsync(ApprovalType.RefundApproval,
                $"Refund ₹{amount:N0} on order #{o.OrderNumber}", refund.Reason, amount, "Refund", refund.Id, actorId, actorName);
            await _audit.LogAsync(actorId, actorName, "RefundPendingApproval", EntityType, orderId.ToString(),
                JsonSerializer.Serialize(new { o.OrderNumber, Amount = amount, threshold }));
            _bus.PublishDataChanged(new RealtimeEvent("orders"));
            return (true, null, refund.Id, true);
        }

        await ProcessCoreAsync(o, refund, actorId, actorName);
        return refund.Status == RefundStatus.Succeeded
            ? (true, null, refund.Id, false)
            : (false, refund.FailureReason, refund.Id, false);
    }

    public async Task<(bool ok, string? error)> ResolveRefundApprovalAsync(Guid approvalRequestId, bool approve, string? notes, Guid? actorId, string actorName)
    {
        var request = await _db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == approvalRequestId);
        if (request == null || request.Type != ApprovalType.RefundApproval || request.RelatedEntityId == null)
            return (false, "Refund approval request not found.");
        if (request.Status != ApprovalStatus.Pending) return (false, $"This request is already {request.Status}.");

        var refund = await _db.Refunds.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == request.RelatedEntityId);
        if (refund == null) return (false, "Refund not found.");

        if (!approve)
        {
            await _approvals.RejectAsync(approvalRequestId, notes, actorId, actorName);
            refund.Status = RefundStatus.Failed;
            refund.FailureReason = string.IsNullOrWhiteSpace(notes) ? "Rejected by approver" : notes.Trim();
            await _db.SaveChangesAsync();
            await _audit.LogAsync(actorId, actorName, "RefundRejected", EntityType, refund.OrderId.ToString(),
                JsonSerializer.Serialize(new { refund.Amount, notes }));
            _bus.PublishDataChanged(new RealtimeEvent("orders"));
            return (true, null);
        }

        await _approvals.ApproveAsync(approvalRequestId, notes, actorId, actorName);
        var o = await _db.Orders.IgnoreQueryFilters().Include(x => x.Refunds).FirstOrDefaultAsync(x => x.Id == refund.OrderId);
        if (o == null) return (false, "Order not found.");
        await ProcessCoreAsync(o, refund, actorId, actorName);
        return (true, null);
    }

    // Executes a Pending refund: gateway/offline → ledger entry → order status → audit + notify. Caller-agnostic.
    private async Task ProcessCoreAsync(Order o, Refund refund, Guid? actorId, string actorName)
    {
        if (refund.Status != RefundStatus.Pending) return;

        if (!refund.IsOffline)
        {
            var capturePaymentId = await _db.Payments.Where(p => p.OrderId == o.Id && p.Status == PaymentStatus.Success)
                .OrderByDescending(p => p.PaidAt).Select(p => p.GatewayPaymentId).FirstOrDefaultAsync();
            GatewayRefundResult result;
            try { result = await _gateway.RefundAsync(capturePaymentId, refund.Amount); }
            catch (Exception ex) { result = new GatewayRefundResult(false, null, ex.Message); }
            if (result.Success) { refund.GatewayRefundId = result.RefundId; refund.ResponseMessage = result.Message; refund.Status = RefundStatus.Succeeded; }
            else { refund.Status = RefundStatus.Failed; refund.FailureReason = result.Message ?? "Gateway refund failed."; }
        }
        else { refund.Status = RefundStatus.Succeeded; refund.ResponseMessage = "Offline refund"; }

        if (refund.Status == RefundStatus.Succeeded)
        {
            var txn = new PaymentTransaction
            {
                OrderId = o.Id, Gateway = refund.Gateway ?? "Offline", GatewayTransactionId = refund.GatewayRefundId,
                Status = PaymentStatus.Refunded, Amount = refund.Amount, PaymentMethod = o.PaymentMode ?? PaymentMode.Razorpay,
                IsRefund = true, PaidOnUtc = DateTime.UtcNow, CreatedByName = actorName, CreatedById = actorId,
                ResponseMessage = refund.ResponseMessage, Reference = refund.GatewayRefundId
            };
            _db.PaymentTransactions.Add(txn);
            refund.PaymentTransactionId = txn.Id;

            var priorRefunded = o.Refunds.Where(r => r.Status == RefundStatus.Succeeded && r.Id != refund.Id).Sum(r => r.Amount);
            if (priorRefunded + refund.Amount >= o.TotalAmount)
            {
                o.PaymentStatus = PaymentStatus.Refunded;
                o.Status = OrderStatus.Refunded;
                o.CancelledAt ??= DateTime.UtcNow;
            }
            else o.PaymentStatus = PaymentStatus.PartialRefund;
        }

        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, refund.Status == RefundStatus.Succeeded ? "RefundCreated" : "RefundFailed", EntityType, o.Id.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber, refund.Amount, refund.RefundType, refund.IsOffline, status = refund.Status.ToString(), refund.FailureReason }));
        if (refund.Status == RefundStatus.Succeeded)
            await _notify.NotifyAsync(AdminNotificationType.Refund, NotificationSeverity.Warning,
                "Refund processed", $"₹{refund.Amount:N0} refunded for order #{o.OrderNumber}.", $"/admin/orders/{o.Id}", o.Id.ToString());

        _bus.PublishDataChanged(new RealtimeEvent("orders"));
        _bus.PublishDataChanged(new RealtimeEvent("dashboard"));
    }
}
