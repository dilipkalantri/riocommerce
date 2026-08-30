using RioCommerce.Core.DTOs.Orders;
namespace RioCommerce.Core.Interfaces;

// Unified payment ledger for an order (gateway captures + manual/offline entries + refunds).
public interface IPaymentTransactionService
{
    Task<List<PaymentTransactionDto>> ListAsync(Guid orderId);
    Task<(bool ok, string? error)> AddManualPaymentAsync(Guid orderId, ManualPaymentRequest req, Guid? actorId, string actorName);
}
