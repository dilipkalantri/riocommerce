using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

public record DispatchRow(
    Guid OrderId, string OrderNumber, DateTime CreatedAt, string StudentName, string StudentPhone, string? City,
    string ProductSummary, OrderStatus OrderStatus, string ShipmentStatus, string? Courier, string? TrackingNumber);

public class ShipmentSaveRequest
{
    public Guid OrderId { get; set; }
    public string? Courier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? Notes { get; set; }
}

public record ReturnItem(
    Guid Id, string OrderNumber, string StudentName, string Reason, decimal RefundAmount,
    string Status, DateTime CreatedAt, DateTime? ResolvedAt);

public class CreateReturnRequest
{
    public string OrderNumber { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
}

public record OperationsStats(int PendingDispatch, int Dispatched, int Delivered, int OpenReturns);
