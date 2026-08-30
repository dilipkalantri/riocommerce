using System.Security.Claims;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/payouts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
public class PayoutsAdminController : ControllerBase
{
    private readonly ISettlementService _settlements;
    private readonly IPayoutCalculationService _calc;
    public PayoutsAdminController(ISettlementService settlements, IPayoutCalculationService calc)
    {
        _settlements = settlements; _calc = calc;
    }

    private Guid? ActorId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : null;
    private string ActorName => User.Identity?.Name ?? "api";

    [HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<PayoutDashboardDto>>> Dashboard()
        => Ok(ApiResponse<PayoutDashboardDto>.Ok(await _settlements.GetDashboardAsync()));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<object>>> ListPayouts([FromQuery] PayoutType? type, [FromQuery] PayoutStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (rows, total) = await _settlements.ListPayoutsAsync(type, status, page, pageSize);
        return Ok(ApiResponse<object>.Ok(new { rows, total }));
    }

    [HttpGet("preview")]
    public async Task<ActionResult<ApiResponse<List<PayoutPreviewDto>>>> Preview([FromQuery] PayoutType type, [FromQuery] DateTime from, [FromQuery] DateTime to)
        => Ok(ApiResponse<List<PayoutPreviewDto>>.Ok(await _calc.PreviewAsync(type, from, to)));

    [HttpGet("batches")]
    public async Task<ActionResult<ApiResponse<List<SettlementBatchListItem>>>> Batches([FromQuery] SettlementStatus? status)
        => Ok(ApiResponse<List<SettlementBatchListItem>>.Ok(await _settlements.ListBatchesAsync(status)));

    [HttpGet("batches/{id:guid}")]
    public async Task<ActionResult<ApiResponse<SettlementBatchDetailDto>>> Batch(Guid id)
    {
        var d = await _settlements.GetBatchAsync(id);
        return d == null ? NotFound(ApiResponse<SettlementBatchDetailDto>.Fail("Not found")) : Ok(ApiResponse<SettlementBatchDetailDto>.Ok(d));
    }

    [HttpPost("batches/generate")]
    public async Task<ActionResult<ApiResponse<Guid?>>> Generate([FromBody] GenerateBatchRequest req)
    {
        var (ok, error, id) = await _settlements.GenerateBatchAsync(req.BeneficiaryType, req.FromUtc, req.ToUtc, ActorId, ActorName);
        return ok ? Ok(ApiResponse<Guid?>.Ok(id, "Batch generated")) : BadRequest(ApiResponse<Guid?>.Fail(error!));
    }

    [HttpPost("batches/{id:guid}/submit")]
    public async Task<ActionResult<ApiResponse<string>>> Submit(Guid id) => Wrap(await _settlements.SubmitForApprovalAsync(id, ActorId, ActorName), "Submitted");

    [HttpPost("batches/{id:guid}/approve")]
    public async Task<ActionResult<ApiResponse<string>>> Approve(Guid id, [FromBody] NoteBody? body) => Wrap(await _settlements.ApproveBatchAsync(id, body?.Notes, ActorId, ActorName), "Approved");

    [HttpPost("batches/{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<string>>> Reject(Guid id, [FromBody] NoteBody? body) => Wrap(await _settlements.RejectBatchAsync(id, body?.Notes, ActorId, ActorName), "Rejected");

    [HttpPost("batches/{id:guid}/mark-paid")]
    public async Task<ActionResult<ApiResponse<string>>> MarkPaid(Guid id, [FromBody] MarkPaidRequest req) => Wrap(await _settlements.MarkPaidAsync(id, req, ActorId, ActorName), "Marked paid");

    [HttpPost("batches/{id:guid}/recalculate")]
    public async Task<ActionResult<ApiResponse<string>>> Recalculate(Guid id) => Wrap(await _settlements.RecalculateBatchAsync(id, ActorId, ActorName), "Recalculated");

    [HttpPost("batches/{id:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<string>>> Cancel(Guid id, [FromBody] NoteBody? body) => Wrap(await _settlements.CancelBatchAsync(id, body?.Notes, ActorId, ActorName), "Cancelled");

    [HttpPost("batches/{id:guid}/adjustments")]
    public async Task<ActionResult<ApiResponse<string>>> AddAdjustment(Guid id, [FromBody] SettlementAdjustmentRequest req) => Wrap(await _settlements.AddAdjustmentAsync(id, req, ActorId, ActorName), "Adjustment added");

    [HttpPost("approvals/{approvalRequestId:guid}/resolve")]
    public async Task<ActionResult<ApiResponse<string>>> ResolveApproval(Guid approvalRequestId, [FromQuery] bool approve, [FromBody] NoteBody? body)
        => Wrap(await _settlements.ResolveBatchApprovalAsync(approvalRequestId, approve, body?.Notes, ActorId, ActorName), approve ? "Approved" : "Rejected");

    [HttpGet("batches/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id)
    {
        var file = await _settlements.ExportBatchCsvAsync(id);
        if (file == null) return NotFound();
        return File(System.Text.Encoding.UTF8.GetBytes(file.Value.csv), "text/csv", file.Value.fileName);
    }

    private ActionResult<ApiResponse<string>> Wrap((bool ok, string? error) r, string okMsg)
        => r.ok ? Ok(ApiResponse<string>.Ok("ok", okMsg)) : BadRequest(ApiResponse<string>.Fail(r.error!));

    public record NoteBody(string? Notes);
}
