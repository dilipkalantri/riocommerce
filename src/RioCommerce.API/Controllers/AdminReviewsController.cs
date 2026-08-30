using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Reviews;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/reviews")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class AdminReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;
    public AdminReviewsController(IReviewService reviews) => _reviews = reviews;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminReviewItem>>>> List(
        [FromQuery] ReviewStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<PagedResult<AdminReviewItem>>.Ok(await _reviews.AdminListAsync(status, page, pageSize)));

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<ReviewStats>>> Stats()
        => Ok(ApiResponse<ReviewStats>.Ok(await _reviews.AdminStatsAsync()));

    [HttpPost("{id:guid}/moderate")]
    public async Task<ActionResult<ApiResponse<string>>> Moderate(Guid id, [FromQuery] bool approve)
    {
        await _reviews.ModerateAsync(id, approve);
        return Ok(ApiResponse<string>.Ok("ok", approve ? "Approved" : "Rejected"));
    }
}
