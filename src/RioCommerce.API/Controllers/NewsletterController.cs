using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/newsletter")]
public class NewsletterController : ControllerBase
{
    private readonly INewsletterService _newsletter;
    public NewsletterController(INewsletterService newsletter) => _newsletter = newsletter;

    [HttpPost("subscribe")]
    public async Task<ActionResult<ApiResponse<string>>> Subscribe([FromBody] SubscribeRequest request)
    {
        var (ok, message) = await _newsletter.SubscribeAsync(request);
        return ok ? Ok(ApiResponse<string>.Ok("ok", message)) : BadRequest(ApiResponse<string>.Fail(message));
    }
}
