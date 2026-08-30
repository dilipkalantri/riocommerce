using RioCommerce.Core.DTOs.Checkout;
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
public class CheckoutController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    public CheckoutController(ICheckoutService checkout) => _checkout = checkout;

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<CheckoutSummary>>> Summary([FromQuery] string? coupon = null, [FromQuery] string? affiliate = null)
        => Ok(ApiResponse<CheckoutSummary>.Ok(await _checkout.GetSummaryAsync(UserId, coupon, affiliate)));

    [HttpPost("place")]
    public async Task<ActionResult<ApiResponse<PlaceOrderResult>>> Place([FromBody] CheckoutRequest request)
    {
        try
        {
            return Ok(ApiResponse<PlaceOrderResult>.Ok(await _checkout.PlaceOrderAsync(UserId, request), "Order placed"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<PlaceOrderResult>.Fail(ex.Message));
        }
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<ApiResponse<OrderReceipt>>> Confirm([FromBody] PaymentCallback callback)
    {
        var receipt = await _checkout.ConfirmPaymentAsync(callback);
        return receipt == null
            ? NotFound(ApiResponse<OrderReceipt>.Fail("Order not found"))
            : Ok(ApiResponse<OrderReceipt>.Ok(receipt));
    }

    [HttpGet("receipt/{orderNumber}")]
    public async Task<ActionResult<ApiResponse<OrderReceipt>>> Receipt(string orderNumber)
    {
        var receipt = await _checkout.GetReceiptAsync(UserId, orderNumber);
        return receipt == null
            ? NotFound(ApiResponse<OrderReceipt>.Fail("Order not found"))
            : Ok(ApiResponse<OrderReceipt>.Ok(receipt));
    }
}
