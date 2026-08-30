using RioCommerce.Core.Entities;
namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Transactional notifications. Phase 9 ships a logging stub; Phase 14 replaces it with a real
/// multi-channel engine (email + WhatsApp/SMS) behind the same interface.
/// </summary>
public interface INotificationSender
{
    Task SendOrderConfirmationAsync(Order order);
}
