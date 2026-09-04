using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    private readonly ISchoolStudentService _schoolStudents;
    public ProductsController(IUnitOfWork uow, ISchoolStudentService schoolStudents)
    {
        _uow = uow;
        _schoolStudents = schoolStudents;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductListItem>>>> GetAll([FromQuery] ProductFilterRequest filter)
    {
        var result = await _uow.Products.GetFilteredAsync(filter);
        await ApplySchoolPricingAsync(result.Items);
        return Ok(ApiResponse<PagedResult<ProductListItem>>.Ok(result));
    }

    [HttpGet("featured")]
    public async Task<ActionResult<ApiResponse<List<ProductListItem>>>> GetFeatured([FromQuery] int count = 8)
    {
        var items = await _uow.Products.GetFeaturedAsync(count);
        await ApplySchoolPricingAsync(items);
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
        // Detail page and its "Related Products" strip both need the school tier applied.
        await ApplySchoolPricingAsync(new[] { (ProductListItem)product }.Concat(product.RelatedProducts));
        return Ok(ApiResponse<ProductDetailResponse>.Ok(product));
    }

    // Server-authoritative school-tier stamp. Runs once per response: resolves the caller's
    // school-linked status from the auth cookie, then flips SchoolPriceApplied on every DTO
    // that has a set-and-lower SchoolStudentPrice. Anonymous callers and non-school users
    // skip the DB lookup entirely; the DTOs go back with SchoolPriceApplied = false, i.e.
    // the regular price. Never trusts a client hint.
    private async Task ApplySchoolPricingAsync(IEnumerable<ProductListItem> items)
    {
        var userId = ParseUserId(User);
        if (userId is null) return;
        if (!await _schoolStudents.IsSchoolLinkedAsync(userId.Value)) return;

        foreach (var it in items)
        {
            if (it.SchoolStudentPrice.HasValue && it.SchoolStudentPrice.Value > 0)
                it.SchoolPriceApplied = true;
        }
    }

    private static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var s = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(s, out var id) ? id : null;
    }
}
