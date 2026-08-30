using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Shipping;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

public class OperationsService : IOperationsService
{
    // Orders that may need physical fulfilment (paid/confirmed lifecycle).
    private static readonly OrderStatus[] Fulfillable =
        { OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Activated, OrderStatus.Delivered };

    private readonly RioCommerceDbContext _db;
    private readonly INotificationService _notify;
    private readonly ILogger<OperationsService> _log;
    public OperationsService(RioCommerceDbContext db, INotificationService notify, ILogger<OperationsService> log)
    {
        _db = db;
        _notify = notify;
        _log = log;
    }

    public async Task<OperationsStats> StatsAsync()
    {
        var shipped = await _db.Shipments.CountAsync(s => s.Status == ShipmentStatus.Dispatched);
        var delivered = await _db.Shipments.CountAsync(s => s.Status == ShipmentStatus.Delivered);
        var handledOrderIds = await _db.Shipments.Where(s => s.Status != ShipmentStatus.Pending).Select(s => s.OrderId).ToListAsync();
        // A wallet top-up is Confirmed and carries a line item, so it would otherwise queue up for
        // dispatch. There is nothing to ship — it's money, not goods.
        var pending = await _db.Orders.ExcludeWalletTopUps()
            .CountAsync(o => Fulfillable.Contains(o.Status) && !handledOrderIds.Contains(o.Id));
        var openReturns = await _db.ReturnRequests.CountAsync(r => r.Status == "Requested");
        return new OperationsStats(pending, shipped, delivered, openReturns);
    }

    public async Task<List<DispatchRow>> DispatchBoardAsync(string? status)
    {
        var orders = await _db.Orders.ExcludeWalletTopUps().Include(o => o.Items)
            .Where(o => Fulfillable.Contains(o.Status))
            .OrderByDescending(o => o.CreatedAt).ToListAsync();
        var shipments = await _db.Shipments.ToDictionaryAsync(s => s.OrderId, s => s);

        var rows = orders.Select(o =>
        {
            shipments.TryGetValue(o.Id, out var sh);
            return new DispatchRow(
                o.Id, o.OrderNumber, o.CreatedAt, o.StudentName, o.StudentPhone, o.StudentCity,
                o.Items.Count == 0 ? "—" : o.Items.First().ProductTitle + (o.Items.Count > 1 ? $" +{o.Items.Count - 1}" : ""),
                o.Status, (sh?.Status ?? ShipmentStatus.Pending).ToString(), sh?.Courier, sh?.TrackingNumber);
        });
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(r => r.ShipmentStatus == status);
        return rows.ToList();
    }

    public async Task<(bool ok, string? error)> SaveShipmentAsync(ShipmentSaveRequest req)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == req.OrderId);
        if (order == null) return (false, "Order not found.");
        // ── Courier + tracking rules, enforced HERE and not only in the dropdown ──
        // The page is one caller; this method is the guarantee.
        var courier = string.IsNullOrWhiteSpace(req.Courier) ? null : req.Courier.Trim();
        var tracking = string.IsNullOrWhiteSpace(req.TrackingNumber) ? null : req.TrackingNumber.Trim();

        if (courier != null)
        {
            var option = Couriers.Find(courier);
            if (option == null)
                return (false, $"“{courier}” is not a courier we despatch with. Choose one from the list.");

            // Normalise to the canonical casing so the stored value always matches the list.
            courier = option.Name;

            if (option.RequiresTracking && tracking == null)
                return (false, $"{option.Name} needs a tracking number.");

            // A courier with no tracking page cannot have a meaningful number — drop anything sent so
            // the row cannot claim trackability it does not have.
            if (!option.RequiresTracking) tracking = null;
        }

        var sh = await GetOrCreateShipmentAsync(req.OrderId);
        sh.Courier = courier;
        sh.TrackingNumber = tracking;
        sh.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        // Populate the lines here too, not only on dispatch. The shipping report joins shipments to
        // their items, so a consignment that has had a courier and AWB assigned but has not left yet
        // would otherwise be invisible on the report — which is exactly the pending queue an
        // operator needs to see.
        await EnsureShipmentItemsAsync(sh);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> MarkDispatchedAsync(Guid orderId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return (false, "Order not found.");
        var sh = await GetOrCreateShipmentAsync(orderId);

        // A courier must be chosen, and a tracking number only where that courier actually tracks.
        // This used to demand a number unconditionally, which made a PCMC same-day delivery
        // impossible to dispatch — there is no consignment number to give it.
        if (string.IsNullOrWhiteSpace(sh.Courier))
            return (false, "Select a courier before dispatching.");
        if (Couriers.RequiresTracking(sh.Courier) && string.IsNullOrWhiteSpace(sh.TrackingNumber))
            return (false, $"Add a {sh.Courier} tracking number before dispatching.");

        // Was it ALREADY dispatched? The state transition is the duplicate guard: pressing Dispatch
        // twice must not email the customer twice. Checked before the write, used after it.
        var alreadyDispatched = sh.Status == ShipmentStatus.Dispatched || sh.DispatchedAt != null;

        sh.Status = ShipmentStatus.Dispatched;
        sh.DispatchedAt ??= DateTime.UtcNow;
        await EnsureShipmentItemsAsync(sh);
        if (order.Status == OrderStatus.Confirmed) order.Status = OrderStatus.Processing;
        await _db.SaveChangesAsync();

        // Only after the state is safely persisted. A failed validation or a failed save returns
        // above, so no email can go out for a dispatch that did not happen.
        if (!alreadyDispatched) await TryNotifyDispatchedAsync(order, sh);
        return (true, null);
    }

    /// <summary>
    /// Tells the customer their order is on its way. Best-effort by design: the goods HAVE shipped,
    /// so a mail-server problem must never undo that — it is logged and the dispatch stands. Mirrors
    /// how serial-key notifications are handled.
    /// </summary>
    private async Task TryNotifyDispatchedAsync(Order order, Shipment sh)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(order.StudentEmail) && string.IsNullOrWhiteSpace(order.StudentPhone))
            {
                _log.LogInformation("Dispatch notification skipped — no contact on OrderNumber={OrderNumber}", order.OrderNumber);
                return;
            }

            var titles = await _db.OrderItems.AsNoTracking()
                .Where(i => i.OrderId == order.Id)
                .Select(i => i.ProductTitle)
                .ToListAsync();
            var products = string.Join(", ", titles.Where(t => !string.IsNullOrWhiteSpace(t)));

            var url = Couriers.TrackingUrl(sh.Courier);
            var number = Couriers.RequiresTracking(sh.Courier) ? sh.TrackingNumber : null;
            var trackable = !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(number);

            // The renderer does plain {{token}} substitution with no conditionals, so the whole
            // tracking section is composed here and passed as ONE token. For a courier with no
            // tracking it is an empty string and the section simply is not there — no stray
            // "Tracking Number:" label, no dead link.
            var trackingBlock = trackable
                ? $"Tracking Number: {number}\nTrack your shipment: {url}"
                : string.Empty;

            var tokens = new Dictionary<string, string>
            {
                ["name"] = order.StudentName ?? "",
                ["order_number"] = order.OrderNumber,
                ["product_title"] = products,
                ["courier"] = sh.Courier ?? "",
                ["tracking_number"] = number ?? "",
                ["tracking_url"] = url ?? "",
                ["tracking_block"] = trackingBlock,
            };

            await _notify.SendAsync("order_dispatched",
                new NotificationRecipient(Email: order.StudentEmail, Phone: order.StudentPhone),
                tokens);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Dispatch notification failed OrderNumber={OrderNumber} — dispatch itself is unaffected.", order.OrderNumber);
        }
    }

    public async Task<(bool ok, string? error)> MarkDeliveredAsync(Guid orderId)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) return (false, "Order not found.");
        var sh = await GetOrCreateShipmentAsync(orderId);
        sh.Status = ShipmentStatus.Delivered;
        sh.DeliveredAt = DateTime.UtcNow;
        await EnsureShipmentItemsAsync(sh);
        if (sh.DispatchedAt == null) sh.DispatchedAt = DateTime.UtcNow;
        order.Status = OrderStatus.Delivered;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<ReturnItem>> ReturnsAsync()
    {
        var rows = await _db.ReturnRequests.Include(r => r.Order)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();
        return rows.Select(r => new ReturnItem(
            r.Id, r.OrderNumber, r.Order != null ? r.Order.StudentName : "—", r.Reason, r.RefundAmount,
            r.Status, r.CreatedAt, r.ResolvedAt)).ToList();
    }

    public async Task<(bool ok, string? error)> CreateReturnAsync(CreateReturnRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Reason)) return (false, "A reason is required.");
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == req.OrderNumber.Trim());
        if (order == null) return (false, "Order not found.");
        if (await _db.ReturnRequests.AnyAsync(r => r.OrderId == order.Id && r.Status == "Requested"))
            return (false, "An open return already exists for this order.");
        var refund = req.RefundAmount <= 0 ? order.TotalAmount : Math.Min(req.RefundAmount, order.TotalAmount);

        _db.ReturnRequests.Add(new ReturnRequest
        {
            OrderId = order.Id, OrderNumber = order.OrderNumber, Reason = req.Reason.Trim(),
            RefundAmount = refund, Status = "Requested"
        });
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ResolveReturnAsync(Guid id, bool approve)
    {
        var r = await _db.ReturnRequests.Include(x => x.Order).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return (false, "Return request not found.");
        if (r.Status != "Requested") return (false, "This return has already been resolved.");

        r.ResolvedAt = DateTime.UtcNow;
        if (approve)
        {
            r.Status = "Refunded";
            if (r.Order != null)
            {
                r.Order.PaymentStatus = PaymentStatus.Refunded;
                r.Order.Status = OrderStatus.Refunded;
                r.Order.CancelledAt = DateTime.UtcNow;
            }
        }
        else r.Status = "Rejected";
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private async Task<Shipment> GetOrCreateShipmentAsync(Guid orderId)
    {
        var sh = await _db.Shipments.FirstOrDefaultAsync(s => s.OrderId == orderId);
        if (sh == null) { sh = new Shipment { OrderId = orderId, Status = ShipmentStatus.Pending }; _db.Shipments.Add(sh); }
        return sh;
    }

    /// <summary>
    /// Populates a shipment's line items from the order the first time it is dispatched, so the
    /// shipping report can name what was in the box and in what quantity.
    ///
    /// <para>The dispatch screen has no per-line UI — it dispatches a whole order — so the lines
    /// are mirrored from the order rather than entered. Runs once: a shipment that already has
    /// items keeps them, which leaves room for the lines to be edited later without this
    /// overwriting the correction.</para>
    /// </summary>
    private async Task EnsureShipmentItemsAsync(Shipment sh)
    {
        if (sh.Items.Count > 0) return;
        if (sh.Id != Guid.Empty && await _db.Set<ShipmentItem>().AnyAsync(i => i.ShipmentId == sh.Id)) return;

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == sh.OrderId)
            .Select(i => new { i.Id, i.ProductId, i.ProductTitle, i.Quantity })
            .ToListAsync();

        foreach (var i in items)
            sh.Items.Add(new ShipmentItem
            {
                OrderItemId = i.Id,
                ProductId = i.ProductId,
                ProductTitle = i.ProductTitle,
                Quantity = i.Quantity
            });
    }
}
