using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    public ProductsController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductListItem>>>> GetAll([FromQuery] ProductFilterRequest filter)
    {
        var result = await _uow.Products.GetFilteredAsync(filter);
        return Ok(ApiResponse<PagedResult<ProductListItem>>.Ok(result));
    }

    [HttpGet("featured")]
    public async Task<ActionResult<ApiResponse<List<ProductListItem>>>> GetFeatured([FromQuery] int count = 8)
    {
        var items = await _uow.Products.GetFeaturedAsync(count);
        return Ok(ApiResponse<List<ProductListItem>>.Ok(items));
    }

    [HttpGet("suggest")]
    public async Task<ActionResult<ApiResponse<List<ProductSuggestion>>>> Suggest([FromQuery] string q, [FromQuery] int take = 6)
        => Ok(ApiResponse<List<ProductSuggestion>>.Ok(await _uow.Products.SuggestAsync(q, take)));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ApiResponse<ProductDetailResponse>>> GetBySlug(string slug)
    {
        var product = await _uow.Products.GetDetailBySlugAsync(slug);
        if (product == null) return NotFound(ApiResponse<ProductDetailResponse>.Fail("Product not found"));
        return Ok(ApiResponse<ProductDetailResponse>.Ok(product));
    }
}
