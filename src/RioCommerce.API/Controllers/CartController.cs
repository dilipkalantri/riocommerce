using RioCommerce.Core.DTOs.Cart;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CartController : ControllerBase
{
    private readonly ICartService _cart;
    public CartController(ICartService cart) => _cart = cart;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<ApiResponse<CartView>>> Get([FromQuery] string? coupon = null)
        => Ok(ApiResponse<CartView>.Ok(await _cart.GetAsync(UserId, coupon)));

    [HttpPost("items")]
    public async Task<ActionResult<ApiResponse<CartView>>> Add([FromBody] AddToCartRequest request)
    {
        var (ok, error) = await _cart.AddAsync(UserId, request.ProductId, request.ModeId);
        if (!ok) return BadRequest(ApiResponse<CartView>.Fail(error ?? "Could not add to cart."));
        return Ok(ApiResponse<CartView>.Ok(await _cart.GetAsync(UserId), "Added to cart"));
    }

    [HttpDelete("items/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CartView>>> Remove(Guid id)
    {
        await _cart.RemoveAsync(UserId, id);
        return Ok(ApiResponse<CartView>.Ok(await _cart.GetAsync(UserId), "Removed"));
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<ApiResponse<string>>> Checkout([FromQuery] string? coupon = null)
    {
        try
        {
            var orderNo = await _cart.CheckoutAsync(UserId, coupon);
            return Ok(ApiResponse<string>.Ok(orderNo, "Order placed"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }
}
