using RioCommerce.Core.DTOs.Recommendations;
namespace RioCommerce.Core.Interfaces;

public interface IProductRecommendationAdminService
{
    /// <summary>All recommendation rows for the given source product, including soft-inactive ones (admin sees everything).</summary>
    Task<List<ProductRecommendationItem>> ListAsync(Guid productId);
    /// <summary>Replaces the recommendation set for the source product. Rows missing from the payload are soft-deleted.</summary>
    Task<(bool ok, string? error)> SaveAsync(Guid productId, List<ProductRecommendationItem> items, Guid? actorId);
    /// <summary>Autocomplete search — used by the admin to find candidate products. Excludes the source itself.</summary>
    Task<List<RecommendationSearchHit>> SearchAsync(string query, Guid excludeProductId, int max = 12);
    /// <summary>Bulk load of every Active product (excluding the source) for the client-side picker. Cap at 1000 so we never accidentally ship a 50k row payload.</summary>
    Task<List<RecommendationSearchHit>> ListAllActiveAsync(Guid excludeProductId, int max = 1000);
}

public interface IProductRecommendationService
{
    /// <summary>Storefront fetch — active rows only, ordered by Priority desc then DisplayOrder, capped to <paramref name="max"/>.</summary>
    Task<List<RecommendationCard>> GetForProductAsync(Guid productId, int max = 8);
    /// <summary>Aggregated recommendations across multiple source products (used by the Cart "Complete Your Preparation" block). De-duplicated.</summary>
    Task<List<RecommendationCard>> GetForProductsAsync(IEnumerable<Guid> productIds, int max = 4);
}
