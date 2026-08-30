using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class WishlistController : ControllerBase
{
    private readonly IWishlistService _wishlist;
    public WishlistController(IWishlistService wishlist) => _wishlist = wishlist;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProductListItem>>>> Get()
        => Ok(ApiResponse<List<ProductListItem>>.Ok(await _wishlist.GetAsync(UserId)));

    [HttpGet("count")]
    public async Task<ActionResult<ApiResponse<int>>> Count()
        => Ok(ApiResponse<int>.Ok(await _wishlist.CountAsync(UserId)));

    [HttpPost("toggle/{productId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Toggle(Guid productId)
        => Ok(ApiResponse<bool>.Ok(await _wishlist.ToggleAsync(UserId, productId), "Updated"));

    [HttpDelete("{productId:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Remove(Guid productId)
    {
        await _wishlist.RemoveAsync(UserId, productId);
        return Ok(ApiResponse<string>.Ok("ok", "Removed"));
    }
}
