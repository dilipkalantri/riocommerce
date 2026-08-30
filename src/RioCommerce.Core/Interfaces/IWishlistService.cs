using RioCommerce.Core.DTOs.Products;
namespace RioCommerce.Core.Interfaces;

public interface IWishlistService
{
    /// <summary>Adds the product if absent, removes it if present. Returns the new state (true = in wishlist).</summary>
    Task<bool> ToggleAsync(Guid userId, Guid productId);
    Task RemoveAsync(Guid userId, Guid productId);
    Task<bool> IsInWishlistAsync(Guid userId, Guid productId);
    Task<int> CountAsync(Guid userId);
    Task<List<ProductListItem>> GetAsync(Guid userId);
}
