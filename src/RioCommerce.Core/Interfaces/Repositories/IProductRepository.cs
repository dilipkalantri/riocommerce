using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
namespace RioCommerce.Core.Interfaces.Repositories;
public interface IProductRepository : IGenericRepository<Product>
{
    Task<Product?> GetBySlugAsync(string slug);
    Task<PagedResult<ProductListItem>> GetFilteredAsync(ProductFilterRequest filter);
    Task<ProductDetailResponse?> GetDetailBySlugAsync(string slug);
    Task<List<ProductListItem>> GetFeaturedAsync(int count = 8);
    Task<List<ProductSuggestion>> SuggestAsync(string q, int take = 6);
}
