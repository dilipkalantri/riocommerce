using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/franchise")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "franchise_admin")]
public class FranchisePortalController : ControllerBase
{
    private readonly IFranchisePortalService _svc;
    private readonly IFranchiseService _franchise;
    private readonly IReceiptService _receipts;
    public FranchisePortalController(IFranchisePortalService svc, IFranchiseService franchise, IReceiptService receipts)
    {
        _svc = svc;
        _franchise = franchise;
        _receipts = receipts;
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("dashboard")]
    public async Task<ActionResult<ApiResponse<FranchisePortalDashboard>>> Dashboard()
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<FranchisePortalDashboard>.Fail("Your account isn't linked to a franchise."));
        var d = await _svc.GetDashboardAsync(fid.Value);
        return Ok(ApiResponse<FranchisePortalDashboard>.Ok(d!));
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<ApiResponse<List<FranchiseCatalogItem>>>> Catalog()
        => Ok(ApiResponse<List<FranchiseCatalogItem>>.Ok(await _svc.CatalogAsync()));

    /// <summary>
    /// Franchisee product-share listing. Returns, per active product: ProductPrice, EffectivePrice,
    /// IsSpecialPriceActive, ShareType, ShareValue, CalculatedFranchiseAmount and ShareSource.
    /// Reflects Bulk Assign overrides and the current date live (no cached share values). Pass
    /// onlyWithShare=true to hide products the franchisee earns nothing on.
    /// </summary>
    [HttpGet("products")]
    public async Task<ActionResult<ApiResponse<List<FranchiseProductShareRow>>>> Products([FromQuery] bool onlyWithShare = false)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<List<FranchiseProductShareRow>>.Fail("Your account isn't linked to a franchise."));
        return Ok(ApiResponse<List<FranchiseProductShareRow>>.Ok(await _svc.ProductSharesAsync(fid.Value, onlyWithShare)));
    }

    [HttpGet("orders")]
    public async Task<ActionResult<ApiResponse<List<FranchiseOrderRow>>>> Orders()
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<List<FranchiseOrderRow>>.Fail("Your account isn't linked to a franchise."));
        return Ok(ApiResponse<List<FranchiseOrderRow>>.Ok(await _svc.MyOrdersAsync(fid.Value)));
    }

    [HttpGet("ledger")]
    public async Task<ActionResult<ApiResponse<List<FranchiseLedgerItem>>>> Ledger()
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<List<FranchiseLedgerItem>>.Fail("Your account isn't linked to a franchise."));
        return Ok(ApiResponse<List<FranchiseLedgerItem>>.Ok(await _svc.LedgerAsync(fid.Value)));
    }

    [HttpPost("order")]
    public async Task<ActionResult<ApiResponse<string>>> PlaceOrder([FromBody] FranchiseOrderRequest request)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<string>.Fail("Your account isn't linked to a franchise."));
        var (ok, error, orderNumber) = await _svc.PlaceOrderAsync(fid.Value, request);
        return ok ? Ok(ApiResponse<string>.Ok(orderNumber!, "Order placed")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    /// <summary>Create a Pending order + Razorpay intent for online payment. Returns the popup handoff.</summary>
    [HttpPost("order/gateway")]
    public async Task<ActionResult<ApiResponse<FranchiseGatewayHandoff>>> CreateGatewayOrder([FromBody] FranchiseOrderRequest request)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<FranchiseGatewayHandoff>.Fail("Your account isn't linked to a franchise."));
        var (ok, error, handoff) = await _svc.CreateGatewayOrderAsync(fid.Value, request);
        return ok ? Ok(ApiResponse<FranchiseGatewayHandoff>.Ok(handoff!)) : BadRequest(ApiResponse<FranchiseGatewayHandoff>.Fail(error!));
    }

    public class FranchiseConfirmRequest
    {
        public string OrderNumber { get; set; } = "";
        public string razorpay_order_id { get; set; } = "";
        public string razorpay_payment_id { get; set; } = "";
        public string razorpay_signature { get; set; } = "";
    }

    /// <summary>Franchise financial bifurcation for one of the franchisee's OWN orders.</summary>
    [HttpGet("orders/{orderId:guid}/bifurcation")]
    public async Task<ActionResult<ApiResponse<RioCommerce.Core.DTOs.Orders.FranchiseBifurcation>>> Bifurcation(Guid orderId)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<RioCommerce.Core.DTOs.Orders.FranchiseBifurcation>.Fail("Your account isn't linked to a franchise."));
        if (!await _svc.OwnsOrderAsync(fid.Value, orderId))
            return NotFound(ApiResponse<RioCommerce.Core.DTOs.Orders.FranchiseBifurcation>.Fail("Order not found."));
        var b = await _receipts.GetBifurcationAsync(orderId);
        return b == null
            ? NotFound(ApiResponse<RioCommerce.Core.DTOs.Orders.FranchiseBifurcation>.Fail("Order not found."))
            : Ok(ApiResponse<RioCommerce.Core.DTOs.Orders.FranchiseBifurcation>.Ok(b));
    }

    /// <summary>Verify the Razorpay signature and finalise a gateway-paid franchise order.</summary>
    [HttpPost("order/gateway/confirm")]
    public async Task<ActionResult<ApiResponse<string>>> ConfirmGatewayOrder([FromBody] FranchiseConfirmRequest req)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<string>.Fail("Your account isn't linked to a franchise."));
        var (ok, error, orderNumber) = await _svc.ConfirmGatewayOrderAsync(
            fid.Value, req.OrderNumber, req.razorpay_order_id, req.razorpay_payment_id, req.razorpay_signature);
        return ok ? Ok(ApiResponse<string>.Ok(orderNumber!, "Payment confirmed")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    // Franchisee-scoped earnings (read-only). All queries are scoped server-side to the caller's franchise.
    [HttpGet("earnings")]
    public async Task<ActionResult<ApiResponse<PagedResult<FranchiseEarningRow>>>> Earnings(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<PagedResult<FranchiseEarningRow>>.Fail("Your account isn't linked to a franchise."));
        return Ok(ApiResponse<PagedResult<FranchiseEarningRow>>.Ok(await _franchise.GetEarningsAsync(fid.Value, from, to, page, pageSize)));
    }

    [HttpGet("earnings/summary")]
    public async Task<ActionResult<ApiResponse<FranchiseEarningsSummary>>> EarningsSummary()
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<FranchiseEarningsSummary>.Fail("Your account isn't linked to a franchise."));
        return Ok(ApiResponse<FranchiseEarningsSummary>.Ok(await _franchise.GetEarningsSummaryAsync(fid.Value)));
    }

    // ─────────── Phase 3 — Wallet self-service recharge + statement ───────────

    [HttpPost("wallet/recharge/initiate")]
    public async Task<ActionResult<ApiResponse<RechargeIntent>>> InitiateRecharge([FromBody] InitiateRechargeRequest request)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<RechargeIntent>.Fail("Your account isn't linked to a franchise."));
        var (ok, error, intent) = await _franchise.InitiateRechargeAsync(fid.Value, request.Amount);
        return ok ? Ok(ApiResponse<RechargeIntent>.Ok(intent!, "Recharge initiated"))
                  : BadRequest(ApiResponse<RechargeIntent>.Fail(error!));
    }

    [HttpPost("wallet/recharge/confirm")]
    public async Task<ActionResult<ApiResponse<decimal>>> ConfirmRecharge([FromBody] ConfirmRechargeRequest request)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<decimal>.Fail("Your account isn't linked to a franchise."));
        var (ok, error, balance) = await _franchise.ConfirmRechargeAsync(fid.Value, request);
        return ok ? Ok(ApiResponse<decimal>.Ok(balance, "Wallet credited"))
                  : BadRequest(ApiResponse<decimal>.Fail(error!));
    }

    [HttpGet("wallet/recharges")]
    public async Task<ActionResult<ApiResponse<List<WalletRechargeRow>>>> Recharges([FromQuery] int take = 50)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return BadRequest(ApiResponse<List<WalletRechargeRow>>.Fail("Your account isn't linked to a franchise."));
        return Ok(ApiResponse<List<WalletRechargeRow>>.Ok(await _franchise.ListRechargesAsync(fid.Value, take)));
    }

    [HttpGet("wallet/statement.csv")]
    public async Task<IActionResult> StatementCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var fid = await _svc.ResolveFranchiseIdAsync(UserId);
        if (fid == null) return Unauthorized();
        var csv = await _franchise.GetStatementCsvAsync(fid.Value, from, to);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"wallet-statement-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
