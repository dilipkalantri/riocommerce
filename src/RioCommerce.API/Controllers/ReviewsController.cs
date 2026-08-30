using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Reviews;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;
    public ReviewsController(IReviewService reviews) => _reviews = reviews;

    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    [HttpGet("product/{productId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ProductReviews>>> ForProduct(Guid productId)
        => Ok(ApiResponse<ProductReviews>.Ok(await _reviews.GetForProductAsync(productId, CurrentUserId)));

    [HttpPost]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ApiResponse<string>>> Submit([FromBody] SubmitReviewRequest request)
    {
        var (ok, error) = await _reviews.SubmitAsync(CurrentUserId!.Value, request);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", "Review submitted for approval"))
            : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
