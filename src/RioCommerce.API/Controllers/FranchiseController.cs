using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/franchise")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class FranchiseController : ControllerBase
{
    private readonly IFranchiseService _svc;
    public FranchiseController(IFranchiseService svc) => _svc = svc;

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<FranchiseStats>>> Stats()
        => Ok(ApiResponse<FranchiseStats>.Ok(await _svc.StatsAsync()));

    [HttpGet("list")]
    public async Task<ActionResult<ApiResponse<List<FranchiseCard>>>> Franchises()
        => Ok(ApiResponse<List<FranchiseCard>>.Ok(await _svc.FranchisesAsync()));

    [HttpGet("orders")]
    public async Task<ActionResult<ApiResponse<PagedResult<FranchiseOrderRow>>>> Orders(
        [FromQuery] Guid? franchiseId, [FromQuery] OrderStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
        => Ok(ApiResponse<PagedResult<FranchiseOrderRow>>.Ok(await _svc.OrdersAsync(franchiseId, status, page, pageSize)));

    public record TopUpRequest(decimal Amount, string? Note);
    public record CreditLimitRequest(decimal Limit);

    [HttpPost("{franchiseId:guid}/topup")]
    public async Task<ActionResult<ApiResponse<string>>> TopUp(Guid franchiseId, [FromBody] TopUpRequest request)
    {
        var (ok, error) = await _svc.TopUpAsync(franchiseId, request.Amount, request.Note);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Wallet topped up")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("{franchiseId:guid}/credit-limit")]
    public async Task<ActionResult<ApiResponse<string>>> CreditLimit(Guid franchiseId, [FromBody] CreditLimitRequest request)
    {
        var (ok, error) = await _svc.SetCreditLimitAsync(franchiseId, request.Limit);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Credit limit updated")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    // ─────────── Phase 1 — Onboarding & lifecycle (admin-only writes) ───────────

    [HttpGet("applications")]
    public async Task<ActionResult<ApiResponse<PagedResult<FranchiseApplicationRow>>>> Applications(
        [FromQuery] FranchiseStatus? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        => Ok(ApiResponse<PagedResult<FranchiseApplicationRow>>.Ok(await _svc.ListApplicationsAsync(status, search, page, pageSize)));

    [HttpGet("{franchiseId:guid}/detail")]
    public async Task<ActionResult<ApiResponse<FranchiseAdminDetail>>> Detail(Guid franchiseId)
    {
        var d = await _svc.GetDetailAsync(franchiseId);
        return d == null ? NotFound(ApiResponse<FranchiseAdminDetail>.Fail("Not found")) : Ok(ApiResponse<FranchiseAdminDetail>.Ok(d));
    }

    [HttpPost("approve")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> Approve([FromBody] FranchiseApprovalRequest request)
    {
        var actor = GetUserId();
        var (ok, error) = await _svc.ApproveAsync(request, actor);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Franchise approved & login provisioned")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("reject")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> Reject([FromBody] FranchiseRejectionRequest request)
    {
        var actor = GetUserId();
        var (ok, error) = await _svc.RejectAsync(request, actor);
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Application rejected")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("{franchiseId:guid}/set-active")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> SetActive(Guid franchiseId, [FromQuery] bool active)
    {
        var (ok, error) = await _svc.SetActiveAsync(franchiseId, active, GetUserId());
        return ok ? Ok(ApiResponse<string>.Ok("ok", active ? "Franchise activated" : "Franchise deactivated")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("{franchiseId:guid}/reset-password")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> ResetPassword(Guid franchiseId)
    {
        var (ok, error) = await _svc.ResetPasswordAsync(franchiseId, GetUserId());
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Password reset & emailed to franchisee")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    private Guid GetUserId() =>
        Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

    // ─────────── Phase 2 — Commission management ───────────

    [HttpGet("{franchiseId:guid}/commissions")]
    public async Task<ActionResult<ApiResponse<List<FranchiseCommissionRow>>>> Commissions(Guid franchiseId)
        => Ok(ApiResponse<List<FranchiseCommissionRow>>.Ok(await _svc.ListCommissionsAsync(franchiseId)));

    [HttpPost("commission/set")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> SetCommission([FromBody] SetCommissionRequest request)
    {
        var (ok, err) = await _svc.SetCommissionAsync(request, GetUserId());
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Commission saved")) : BadRequest(ApiResponse<string>.Fail(err!));
    }

    [HttpPost("commission/bulk")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<int>>> BulkSetCommission([FromBody] BulkSetCommissionRequest request)
    {
        var (ok, err, n) = await _svc.BulkSetCommissionsAsync(request, GetUserId());
        return ok ? Ok(ApiResponse<int>.Ok(n, $"Applied to {n} product(s)")) : BadRequest(ApiResponse<int>.Fail(err!));
    }

    // Assign one product↔commission rule across many or all franchisees at once.
    [HttpPost("commission/bulk-across")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<int>>> BulkAssignAcross([FromBody] BulkAssignAcrossFranchiseesRequest request)
    {
        var (ok, err, n) = await _svc.BulkAssignAcrossFranchiseesAsync(request, GetUserId());
        return ok ? Ok(ApiResponse<int>.Ok(n, $"Applied to {n} franchise-product pair(s)")) : BadRequest(ApiResponse<int>.Fail(err!));
    }

    [HttpPost("{franchiseId:guid}/commission/{productId:guid}/clear")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> ClearCommission(Guid franchiseId, Guid productId)
    {
        var (ok, err) = await _svc.ClearCommissionAsync(franchiseId, productId, GetUserId());
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Commission rule cleared")) : BadRequest(ApiResponse<string>.Fail(err!));
    }

    [HttpGet("{franchiseId:guid}/earnings")]
    public async Task<ActionResult<ApiResponse<PagedResult<FranchiseEarningRow>>>> Earnings(
        Guid franchiseId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
        => Ok(ApiResponse<PagedResult<FranchiseEarningRow>>.Ok(await _svc.GetEarningsAsync(franchiseId, from, to, page, pageSize)));

    [HttpGet("{franchiseId:guid}/earnings/summary")]
    public async Task<ActionResult<ApiResponse<FranchiseEarningsSummary>>> EarningsSummary(Guid franchiseId)
        => Ok(ApiResponse<FranchiseEarningsSummary>.Ok(await _svc.GetEarningsSummaryAsync(franchiseId)));

    // ─────────── Phase 3 — Admin manual wallet adjustment + statement CSV ───────────

    [HttpPost("adjust")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<ActionResult<ApiResponse<string>>> AdjustWallet([FromBody] AdjustWalletRequest request)
    {
        var (ok, err) = await _svc.AdjustWalletAsync(request, GetUserId());
        return ok ? Ok(ApiResponse<string>.Ok("ok", request.IsCredit ? "Wallet credited" : "Wallet debited"))
                  : BadRequest(ApiResponse<string>.Fail(err!));
    }

    /// <summary>Wallet ledger for one franchise — every credit and debit with the running balance.</summary>
    [HttpGet("{franchiseId:guid}/wallet")]
    public async Task<ActionResult<ApiResponse<FranchiseWalletStatement>>> Wallet(
        Guid franchiseId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int take = 500)
    {
        var s = await _svc.WalletStatementAsync(franchiseId, from, to, take);
        return s == null
            ? NotFound(ApiResponse<FranchiseWalletStatement>.Fail("Franchise not found."))
            : Ok(ApiResponse<FranchiseWalletStatement>.Ok(s));
    }

    // Cookie scheme is accepted here on purpose: this is a browser download opened from an <a href>
    // on the admin page, which carries the admin's auth cookie and no bearer token.
    [HttpGet("{franchiseId:guid}/statement.csv")]
    [Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme,
        Roles = "super_admin,admin,operations")]
    public async Task<IActionResult> StatementCsv(Guid franchiseId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var csv = await _svc.GetStatementCsvAsync(franchiseId, from, to);
        // Name the file after the franchise code — "franchise-<guid>" tells an accountant nothing.
        var detail = await _svc.GetDetailAsync(franchiseId);
        var label = string.IsNullOrWhiteSpace(detail?.Code) ? franchiseId.ToString() : detail!.Code;
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
            $"wallet-statement-{label}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    /// <summary>
    /// READ-ONLY. Reports which existing franchise orders are missing a customer account link and what
    /// a backfill <i>would</i> do to each — link an existing customer, create a new one, or stop for a
    /// human. Writes nothing: no order, no customer and no link is changed by calling this.
    /// </summary>
    [HttpGet("student-link-dryrun")]
    public async Task<ActionResult<ApiResponse<StudentLinkDryRunReport>>> StudentLinkDryRun(
        [FromServices] IStudentAccountProvisioner students, [FromQuery] OrderSource? source = OrderSource.Franchisee)
        => Ok(ApiResponse<StudentLinkDryRunReport>.Ok(await students.BackfillDryRunAsync(source)));
}
