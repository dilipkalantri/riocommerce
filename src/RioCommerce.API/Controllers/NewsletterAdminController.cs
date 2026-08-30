using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/newsletter")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class NewsletterAdminController : ControllerBase
{
    private readonly INewsletterService _newsletter;
    public NewsletterAdminController(INewsletterService newsletter) => _newsletter = newsletter;

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<NewsletterStats>>> Stats()
        => Ok(ApiResponse<NewsletterStats>.Ok(await _newsletter.StatsAsync()));

    [HttpGet("subscribers")]
    public async Task<ActionResult<ApiResponse<List<NewsletterSubscriberItem>>>> Subscribers()
        => Ok(ApiResponse<List<NewsletterSubscriberItem>>.Ok(await _newsletter.ListSubscribersAsync()));

    [HttpPost("subscribers/{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> ToggleSubscriber(Guid id)
    {
        await _newsletter.ToggleSubscriberAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("subscribers/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveSubscriber(Guid id)
    {
        await _newsletter.RemoveSubscriberAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Removed"));
    }

    [HttpGet("campaigns")]
    public async Task<ActionResult<ApiResponse<List<CampaignItem>>>> Campaigns()
        => Ok(ApiResponse<List<CampaignItem>>.Ok(await _newsletter.ListCampaignsAsync()));

    [HttpGet("campaigns/{id:guid}")]
    public async Task<ActionResult<ApiResponse<CampaignEditModel>>> GetCampaign(Guid id)
    {
        var m = await _newsletter.GetCampaignAsync(id);
        return m == null ? NotFound(ApiResponse<CampaignEditModel>.Fail("Campaign not found")) : Ok(ApiResponse<CampaignEditModel>.Ok(m));
    }

    [HttpPost("campaigns")]
    public async Task<ActionResult<ApiResponse<Guid>>> SaveCampaign([FromBody] CampaignEditModel model)
    {
        var (ok, error, id) = await _newsletter.SaveCampaignAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpDelete("campaigns/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteCampaign(Guid id)
    {
        var (ok, error) = await _newsletter.DeleteCampaignAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("campaigns/{id:guid}/send")]
    public async Task<ActionResult<ApiResponse<int>>> SendCampaign(Guid id)
    {
        var (ok, error, recipients) = await _newsletter.SendCampaignAsync(id);
        return ok ? Ok(ApiResponse<int>.Ok(recipients, $"Sent to {recipients} subscriber(s)")) : BadRequest(ApiResponse<int>.Fail(error!));
    }
}
