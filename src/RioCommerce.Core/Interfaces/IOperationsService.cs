using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface IOperationsService
{
    Task<OperationsStats> StatsAsync();

    // Dispatch
    Task<List<DispatchRow>> DispatchBoardAsync(string? status);
    Task<(bool ok, string? error)> SaveShipmentAsync(ShipmentSaveRequest request);
    Task<(bool ok, string? error)> MarkDispatchedAsync(Guid orderId);
    Task<(bool ok, string? error)> MarkDeliveredAsync(Guid orderId);

    // Returns / refunds
    Task<List<ReturnItem>> ReturnsAsync();
    Task<(bool ok, string? error)> CreateReturnAsync(CreateReturnRequest request);
    /// <summary>Approve (refund the order) or reject a return request.</summary>
    Task<(bool ok, string? error)> ResolveReturnAsync(Guid id, bool approve);
}
