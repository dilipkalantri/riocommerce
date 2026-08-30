using RioCommerce.Core.DTOs.Attributes;
using RioCommerce.Core.DTOs.Cart;
namespace RioCommerce.Core.Interfaces;

public interface ICartService
{
    Task<CartView> GetAsync(Guid userId, string? couponCode = null);
    /// <summary>Adds a product to the cart. Returns (false, error) if the product is already in the cart.</summary>
    Task<(bool ok, string? error)> AddAsync(Guid userId, Guid productId, Guid? modeId, IReadOnlyList<AttributeSelection>? selections = null, IReadOnlyList<Guid>? selectedOptionIds = null);
    Task RemoveAsync(Guid userId, Guid cartItemId);
    /// <summary>Sets the quantity on an existing cart line. Clamped to [1, 10]; line is auto-removed if newQuantity ≤ 0.</summary>
    Task<(bool ok, string? error)> UpdateQuantityAsync(Guid userId, Guid cartItemId, int newQuantity);
    Task<int> CountAsync(Guid userId);
    /// <summary>Creates a pending order from the cart, clears the cart, returns the order number.</summary>
    Task<string> CheckoutAsync(Guid userId, string? couponCode);
}
