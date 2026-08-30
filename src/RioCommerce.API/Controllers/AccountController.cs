using RioCommerce.Core.DTOs.Account;
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
public class AccountController : ControllerBase
{
    private readonly IAccountService _account;
    private readonly ICheckoutService _checkout;
    public AccountController(IAccountService account, ICheckoutService checkout)
    {
        _account = account;
        _checkout = checkout;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<AccountDashboard>>> Dashboard()
        => Ok(ApiResponse<AccountDashboard>.Ok(await _account.GetDashboardAsync(UserId)));

    [HttpGet("courses")]
    public async Task<ActionResult<ApiResponse<List<MyCourseItem>>>> Courses()
        => Ok(ApiResponse<List<MyCourseItem>>.Ok(await _account.GetMyCoursesAsync(UserId)));

    [HttpGet("orders")]
    public async Task<ActionResult<ApiResponse<List<MyOrderItem>>>> Orders()
        => Ok(ApiResponse<List<MyOrderItem>>.Ok(await _account.GetMyOrdersAsync(UserId)));

    [HttpGet("orders/{orderNumber}")]
    public async Task<ActionResult<ApiResponse<OrderReceipt>>> OrderDetail(string orderNumber)
    {
        var receipt = await _checkout.GetReceiptAsync(UserId, orderNumber);
        return receipt == null
            ? NotFound(ApiResponse<OrderReceipt>.Fail("Order not found"))
            : Ok(ApiResponse<OrderReceipt>.Ok(receipt));
    }

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<ProfileView>>> Profile()
    {
        var p = await _account.GetProfileAsync(UserId);
        return p == null ? NotFound(ApiResponse<ProfileView>.Fail("User not found")) : Ok(ApiResponse<ProfileView>.Ok(p));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateProfile([FromBody] ProfileUpdate update)
    {
        var (ok, error) = await _account.UpdateProfileAsync(UserId, update);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Profile updated")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<string>>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var (ok, error) = await _account.ChangePasswordAsync(UserId, request);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Password changed")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("reorder/{orderNumber}")]
    public async Task<ActionResult<ApiResponse<ReorderResult>>> Reorder(string orderNumber)
    {
        try
        {
            return Ok(ApiResponse<ReorderResult>.Ok(await _account.ReorderAsync(UserId, orderNumber), "Added to cart"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ReorderResult>.Fail(ex.Message));
        }
    }
}
