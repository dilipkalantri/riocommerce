using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/faculty-sharing")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
public class FacultySharingController : ControllerBase
{
    private readonly IFacultySharingService _svc;
    public FacultySharingController(IFacultySharingService svc) => _svc = svc;

    private Guid ActorId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

    // ── Rules ───────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SharingRuleRow>>>> List()
        => Ok(ApiResponse<List<SharingRuleRow>>.Ok(await _svc.ListRulesAsync()));

    [HttpGet("options")]
    public async Task<ActionResult<ApiResponse<SharingOptions>>> Options()
        => Ok(ApiResponse<SharingOptions>.Ok(await _svc.OptionsAsync()));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<string>>> Save([FromBody] SharingRuleEdit rule)
    {
        // The combined-share cap is enforced in the service, so this endpoint cannot be used to slip
        // an over-allocated product past the UI.
        var (ok, err) = await _svc.SaveRuleAsync(rule);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", "Rule saved"))
            : BadRequest(ApiResponse<string>.Fail(err!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> Delete(Guid id)
    {
        await _svc.DeleteRuleAsync(id);
        return Ok(ApiResponse<string>.Ok("ok", "Rule deleted"));
    }

    // ── Product faculty share (multi-faculty) ───────────────────────────────
    [HttpGet("products")]
    public async Task<ActionResult<ApiResponse<List<ProductFacultyShareSummary>>>> Products(
        [FromQuery] bool onlyWithShare = false, CancellationToken ct = default)
        => Ok(ApiResponse<List<ProductFacultyShareSummary>>.Ok(await _svc.ListProductSharesAsync(onlyWithShare, ct)));

    [HttpGet("products/{productId:guid}")]
    public async Task<ActionResult<ApiResponse<ProductFacultyShareEditModel>>> Product(
        Guid productId, CancellationToken ct = default)
    {
        var model = await _svc.GetProductShareEditModelAsync(productId, ct);
        return model == null
            ? NotFound(ApiResponse<ProductFacultyShareEditModel>.Fail("Product not found"))
            : Ok(ApiResponse<ProductFacultyShareEditModel>.Ok(model));
    }

    /// <summary>Recalculates without saving — lets a client preview the combined allocation.</summary>
    [HttpPost("products/{productId:guid}/preview")]
    public async Task<ActionResult<ApiResponse<ProductFacultyShareEditModel>>> Preview(
        Guid productId, [FromBody] ProductFacultyShareEditModel model, CancellationToken ct = default)
    {
        var result = await _svc.PreviewProductSharesAsync(productId, model, ct);
        return result == null
            ? NotFound(ApiResponse<ProductFacultyShareEditModel>.Fail("Product not found"))
            : Ok(ApiResponse<ProductFacultyShareEditModel>.Ok(result));
    }

    [HttpPost("products/{productId:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> SaveProduct(
        Guid productId, [FromBody] ProductFacultyShareEditModel model, CancellationToken ct = default)
    {
        var (ok, err) = await _svc.SaveProductSharesAsync(productId, model, ActorId(), ct);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", "Faculty shares saved"))
            : BadRequest(ApiResponse<string>.Fail(err!));
    }

    // ── Bulk assign ─────────────────────────────────────────────────────────
    /// <summary>
    /// Dry run: returns the projected per-product allocation without writing. Always worth calling
    /// first — a rate applied to several faculty on one product adds up, so the combined effect is not
    /// obvious from the request alone.
    /// </summary>
    [HttpPost("bulk-assign/preview")]
    public async Task<ActionResult<ApiResponse<FacultyBulkAssignResult>>> BulkAssignPreview(
        [FromBody] BulkAssignFacultySharesRequest req, CancellationToken ct = default)
    {
        var result = await _svc.BulkAssignSharesAsync(req, ActorId(), dryRun: true, ct);
        return Ok(ApiResponse<FacultyBulkAssignResult>.Ok(result));
    }

    [HttpPost("bulk-assign")]
    public async Task<ActionResult<ApiResponse<FacultyBulkAssignResult>>> BulkAssign(
        [FromBody] BulkAssignFacultySharesRequest req, CancellationToken ct = default)
    {
        var result = await _svc.BulkAssignSharesAsync(req, ActorId(), dryRun: false, ct);
        return result.Ok
            ? Ok(ApiResponse<FacultyBulkAssignResult>.Ok(result, $"Applied to {result.PairsApplied} pair(s)"))
            : BadRequest(ApiResponse<FacultyBulkAssignResult>.Fail(result.Error!));
    }

    // ── Assignment management ───────────────────────────────────────────────
    [HttpGet("assignments")]
    public async Task<ActionResult<ApiResponse<List<FacultyAssignmentRow>>>> Assignments(CancellationToken ct = default)
        => Ok(ApiResponse<List<FacultyAssignmentRow>>.Ok(await _svc.ListAssignmentsAsync(ct)));

    [HttpPost("assignments/update")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateAssignment(
        [FromBody] UpdateFacultyAssignmentRequest req, CancellationToken ct = default)
    {
        var (ok, err) = await _svc.UpdateAssignmentAsync(req, ActorId(), ct);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", "Rate updated"))
            : BadRequest(ApiResponse<string>.Fail(err!));
    }

    [HttpPost("assignments/{productId:guid}/{facultyId:guid}/toggle")]
    public async Task<ActionResult<ApiResponse<string>>> ToggleAssignment(
        Guid productId, Guid facultyId, [FromQuery] bool active, CancellationToken ct = default)
    {
        var (ok, err) = await _svc.ToggleAssignmentAsync(productId, facultyId, active, ActorId(), ct);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", active ? "Assignment enabled" : "Assignment disabled"))
            : BadRequest(ApiResponse<string>.Fail(err!));
    }

    [HttpDelete("assignments/{productId:guid}/{facultyId:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveAssignment(
        Guid productId, Guid facultyId, CancellationToken ct = default)
    {
        var (ok, err) = await _svc.RemoveAssignmentAsync(productId, facultyId, ActorId(), ct);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", "Assignment removed"))
            : BadRequest(ApiResponse<string>.Fail(err!));
    }

    // ── Earnings & payout ───────────────────────────────────────────────────
    [HttpGet("payout")]
    public async Task<ActionResult<ApiResponse<List<FacultyPayout>>>> Payout(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        // Defaults to the current month so the existing no-argument callers keep working.
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fromUtc = from is { } f ? DateTime.SpecifyKind(f.Date, DateTimeKind.Utc) : monthStart;
        var toUtc = to is { } t ? DateTime.SpecifyKind(t.Date.AddDays(1), DateTimeKind.Utc) : monthStart.AddMonths(1);
        return Ok(ApiResponse<List<FacultyPayout>>.Ok(await _svc.PayoutAsync(fromUtc, toUtc)));
    }

    [HttpGet("{facultyId:guid}/earnings")]
    public async Task<ActionResult<ApiResponse<List<FacultyEarningRow>>>> Earnings(
        Guid facultyId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int take = 200, CancellationToken ct = default)
        => Ok(ApiResponse<List<FacultyEarningRow>>.Ok(
            await _svc.GetEarningsAsync(facultyId, from, to, take, ct)));

    // ── Settings ────────────────────────────────────────────────────────────
    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<FacultySettings>>> GetSettings(CancellationToken ct = default)
        => Ok(ApiResponse<FacultySettings>.Ok(await _svc.GetSettingsAsync(ct)));

    [HttpPost("settings")]
    public async Task<ActionResult<ApiResponse<string>>> SaveSettings(
        [FromBody] FacultySettings settings, CancellationToken ct = default)
    {
        await _svc.SaveSettingsAsync(settings, ActorId(), ct);
        return Ok(ApiResponse<string>.Ok("ok", "Settings saved"));
    }
}
