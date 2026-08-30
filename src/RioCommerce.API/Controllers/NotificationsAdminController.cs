using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/notifications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class NotificationsAdminController : ControllerBase
{
    private readonly INotificationService _notify;
    public NotificationsAdminController(INotificationService notify) => _notify = notify;

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<NotificationStats>>> Stats()
        => Ok(ApiResponse<NotificationStats>.Ok(await _notify.StatsAsync()));

    [HttpGet("templates")]
    public async Task<ActionResult<ApiResponse<List<MessageTemplateItem>>>> Templates()
        => Ok(ApiResponse<List<MessageTemplateItem>>.Ok(await _notify.ListTemplatesAsync()));

    [HttpGet("templates/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MessageTemplateEditModel>>> Template(Guid id)
    {
        var m = await _notify.GetTemplateAsync(id);
        return m == null ? NotFound(ApiResponse<MessageTemplateEditModel>.Fail("Template not found")) : Ok(ApiResponse<MessageTemplateEditModel>.Ok(m));
    }

    [HttpPost("templates")]
    public async Task<ActionResult<ApiResponse<Guid>>> SaveTemplate([FromBody] MessageTemplateEditModel model)
    {
        var (ok, error, id) = await _notify.SaveTemplateAsync(model);
        return ok ? Ok(ApiResponse<Guid>.Ok(id, "Saved")) : BadRequest(ApiResponse<Guid>.Fail(error!));
    }

    [HttpPost("templates/{id:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> Toggle(Guid id)
    {
        await _notify.ToggleTemplateAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Updated"));
    }

    [HttpDelete("templates/{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        var (ok, error) = await _notify.DeleteTemplateAsync(id);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("logs")]
    public async Task<ActionResult<ApiResponse<List<NotificationLogItem>>>> Logs([FromQuery] int take = 50)
        => Ok(ApiResponse<List<NotificationLogItem>>.Ok(await _notify.LogsAsync(take)));

    [HttpPost("test-send")]
    public async Task<ActionResult<ApiResponse<string>>> TestSend([FromBody] TestSendRequest request)
    {
        var tokens = new Dictionary<string, string>
        {
            ["name"] = "Test Student", ["order_number"] = "RIO-TEST", ["total"] = "2,500", ["items"] = "1",
            ["amount"] = "1000", ["balance"] = "10,000", ["status"] = "Confirmed", ["franchise"] = "Test Centre"
        };
        // Treat the Recipient as email-or-phone — the fan-out picks per channel.
        var to = request.Recipient.Contains('@')
            ? new RioCommerce.Core.Interfaces.NotificationRecipient(Email: request.Recipient)
            : new RioCommerce.Core.Interfaces.NotificationRecipient(Phone: request.Recipient);
        var (ok, error, sent) = await _notify.SendAsync(request.Key, to, tokens);
        return ok ? Ok(ApiResponse<string>.Ok($"sent={sent}", $"Fan-out: {sent} channel(s) dispatched")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
