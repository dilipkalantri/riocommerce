using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/integrations")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
public class IntegrationsAdminController : ControllerBase
{
    private readonly IIntegrationSettingsService _svc;
    public IntegrationsAdminController(IIntegrationSettingsService svc) => _svc = svc;

    [HttpGet("smtp")]
    public async Task<ActionResult<ApiResponse<SmtpSettings>>> GetSmtp()
        => Ok(ApiResponse<SmtpSettings>.Ok(await _svc.GetSmtpAsync()));

    [HttpPost("smtp")]
    public async Task<ActionResult<ApiResponse<string>>> SaveSmtp([FromBody] SmtpSettings s)
    {
        await _svc.SaveSmtpAsync(s);
        return Ok(ApiResponse<string>.Ok("ok", "Email (SMTP) settings saved"));
    }

    [HttpGet("sms")]
    public async Task<ActionResult<ApiResponse<SmsSettings>>> GetSms()
        => Ok(ApiResponse<SmsSettings>.Ok(await _svc.GetSmsAsync()));

    [HttpPost("sms")]
    public async Task<ActionResult<ApiResponse<string>>> SaveSms([FromBody] SmsSettings s)
    {
        await _svc.SaveSmsAsync(s);
        return Ok(ApiResponse<string>.Ok("ok", "SMS gateway settings saved"));
    }

    [HttpGet("razorpay")]
    public async Task<ActionResult<ApiResponse<RazorpaySettings>>> GetRazorpay()
        => Ok(ApiResponse<RazorpaySettings>.Ok(await _svc.GetRazorpayAsync()));

    [HttpPost("razorpay")]
    public async Task<ActionResult<ApiResponse<string>>> SaveRazorpay([FromBody] RazorpaySettings s)
    {
        await _svc.SaveRazorpayAsync(s);
        return Ok(ApiResponse<string>.Ok("ok", "Razorpay settings saved"));
    }

    [HttpGet("easebuzz")]
    public async Task<ActionResult<ApiResponse<EasebuzzSettings>>> GetEasebuzz()
        => Ok(ApiResponse<EasebuzzSettings>.Ok(await _svc.GetEasebuzzAsync()));

    [HttpPost("easebuzz")]
    public async Task<ActionResult<ApiResponse<string>>> SaveEasebuzz([FromBody] EasebuzzSettings s)
    {
        await _svc.SaveEasebuzzAsync(s);
        return Ok(ApiResponse<string>.Ok("ok", "Easebuzz settings saved"));
    }
}
