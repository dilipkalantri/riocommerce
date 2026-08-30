using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Recommendations;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/products")]
public class ProductRecommendationsController : ControllerBase
{
    private readonly IProductRecommendationService _svc;
    public ProductRecommendationsController(IProductRecommendationService svc) => _svc = svc;

    /// <summary>Active recommendations for the source product, server-filtered to published targets only.</summary>
    [HttpGet("{productId:guid}/recommendations")]
    public async Task<ActionResult<ApiResponse<List<RecommendationCard>>>> Get(Guid productId, [FromQuery] int max = 8)
        => Ok(ApiResponse<List<RecommendationCard>>.Ok(await _svc.GetForProductAsync(productId, max)));

    /// <summary>Aggregated recommendations across multiple source products — used by the Cart "Complete Your Preparation" block.</summary>
    [HttpPost("recommendations/batch")]
    public async Task<ActionResult<ApiResponse<List<RecommendationCard>>>> Batch([FromBody] BatchRecommendationRequest req)
        => Ok(ApiResponse<List<RecommendationCard>>.Ok(await _svc.GetForProductsAsync(req.ProductIds ?? new(), req.Max <= 0 ? 4 : req.Max)));

    public record BatchRecommendationRequest(List<Guid>? ProductIds, int Max = 4);
}
