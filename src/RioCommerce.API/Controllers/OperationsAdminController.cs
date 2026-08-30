using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/operations")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class OperationsAdminController : ControllerBase
{
    private readonly IOperationsService _ops;
    public OperationsAdminController(IOperationsService ops) => _ops = ops;

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<OperationsStats>>> Stats()
        => Ok(ApiResponse<OperationsStats>.Ok(await _ops.StatsAsync()));

    [HttpGet("dispatch")]
    public async Task<ActionResult<ApiResponse<List<DispatchRow>>>> Dispatch([FromQuery] string? status = null)
        => Ok(ApiResponse<List<DispatchRow>>.Ok(await _ops.DispatchBoardAsync(status)));

    [HttpPost("dispatch/shipment")]
    public async Task<ActionResult<ApiResponse<string>>> SaveShipment([FromBody] ShipmentSaveRequest request)
    {
        var (ok, error) = await _ops.SaveShipmentAsync(request);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Saved")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("dispatch/{orderId:guid}/dispatched")]
    public async Task<ActionResult<ApiResponse<string>>> MarkDispatched(Guid orderId)
    {
        var (ok, error) = await _ops.MarkDispatchedAsync(orderId);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Marked dispatched")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("dispatch/{orderId:guid}/delivered")]
    public async Task<ActionResult<ApiResponse<string>>> MarkDelivered(Guid orderId)
    {
        var (ok, error) = await _ops.MarkDeliveredAsync(orderId);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Marked delivered")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("returns")]
    public async Task<ActionResult<ApiResponse<List<ReturnItem>>>> Returns()
        => Ok(ApiResponse<List<ReturnItem>>.Ok(await _ops.ReturnsAsync()));

    [HttpPost("returns")]
    public async Task<ActionResult<ApiResponse<string>>> CreateReturn([FromBody] CreateReturnRequest request)
    {
        var (ok, error) = await _ops.CreateReturnAsync(request);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Return logged")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("returns/{id:guid}/resolve")]
    public async Task<ActionResult<ApiResponse<string>>> ResolveReturn(Guid id, [FromQuery] bool approve)
    {
        var (ok, error) = await _ops.ResolveReturnAsync(id, approve);
        return ok ? Ok(ApiResponse<string>.Ok("ok", approve ? "Refunded" : "Rejected")) : BadRequest(ApiResponse<string>.Fail(error!));
    }
}
