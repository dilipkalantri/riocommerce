using System.Text.Json;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.DTOs.Realtime;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class PaymentTransactionService : IPaymentTransactionService
{
    private const string EntityType = "Order";
    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    private readonly INotificationCenterService _notify;
    private readonly IRealtimeBus _bus;

    public PaymentTransactionService(RioCommerceDbContext db, IAuditService audit, INotificationCenterService notify, IRealtimeBus bus)
    {
        _db = db; _audit = audit; _notify = notify; _bus = bus;
    }

    public async Task<List<PaymentTransactionDto>> ListAsync(Guid orderId)
    {
        // Unified ledger = the new PaymentTransaction rows + legacy gateway Payment rows.
        var txns = await _db.PaymentTransactions.Where(t => t.OrderId == orderId)
            .Select(t => new PaymentTransactionDto(
                t.Id, t.Gateway, t.GatewayTransactionId, t.Status, t.Amount, t.Currency, t.PaymentMethod,
                t.IsRefund, t.Reference, t.ResponseMessage, t.RawResponseJson, t.CreatedByName, t.CreatedAt, t.PaidOnUtc))
            .ToListAsync();

        var payments = await _db.Payments.Where(p => p.OrderId == orderId)
            .Select(p => new PaymentTransactionDto(
                p.Id, p.GatewayName ?? "Gateway", p.GatewayPaymentId, p.Status, p.Amount, "INR", p.PaymentMode,
                false, p.BankRef, null, null, "gateway", p.CreatedAt, p.PaidAt,
                // What the customer actually paid with at the gateway (UPI / Credit Card / …).
                p.GatewayPaymentMode))
            .ToListAsync();

        return txns.Concat(payments).OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<(bool ok, string? error)> AddManualPaymentAsync(Guid orderId, ManualPaymentRequest req, Guid? actorId, string actorName)
    {
        if (req.Amount <= 0) return (false, "Amount must be greater than zero.");
        var o = await _db.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == orderId);
        if (o == null) return (false, "Order not found.");

        var txn = new PaymentTransaction
        {
            OrderId = orderId,
            Gateway = req.Method == PaymentMode.Cash ? "Counter" : "Offline",
            Status = PaymentStatus.Success,
            Amount = Math.Round(req.Amount, 2),
            PaymentMethod = req.Method,
            Reference = string.IsNullOrWhiteSpace(req.Reference) ? null : req.Reference.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PaidOnUtc = DateTime.UtcNow,
            CreatedByName = actorName,
            CreatedById = actorId,
            ResponseMessage = "Manual entry"
        };
        _db.PaymentTransactions.Add(txn);

        if (req.MarkOrderPaid && o.PaymentStatus == PaymentStatus.Pending)
        {
            o.PaymentStatus = PaymentStatus.Success;
            if (o.PaymentMode == null) o.PaymentMode = req.Method;
        }
        await _db.SaveChangesAsync();

        await _audit.LogAsync(actorId, actorName, "ManualPaymentRecorded", EntityType, orderId.ToString(),
            JsonSerializer.Serialize(new { o.OrderNumber, txn.Amount, Method = req.Method.ToString(), txn.Reference }));
        await _notify.NotifyAsync(AdminNotificationType.PaymentSuccess, NotificationSeverity.Success,
            "Manual payment recorded", $"₹{txn.Amount:N0} recorded for order #{o.OrderNumber}.", $"/admin/orders/{o.Id}", o.Id.ToString());
        _bus.PublishDataChanged(new RealtimeEvent("orders"));
        _bus.PublishDataChanged(new RealtimeEvent("dashboard"));
        return (true, null);
    }
}
