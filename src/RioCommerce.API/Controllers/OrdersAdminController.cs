using System.Security.Claims;
using System.Text;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/admin/orders")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class OrdersAdminController : ControllerBase
{
    private readonly IOrderAdminService _svc;
    private readonly IRefundService _refunds;
    private readonly IPaymentTransactionService _txns;
    private readonly INoteAttachmentService _attachments;
    private readonly IAuditService _audit;
    public OrdersAdminController(IOrderAdminService svc, IRefundService refunds, IPaymentTransactionService txns, INoteAttachmentService attachments, IAuditService audit)
    {
        _svc = svc; _refunds = refunds; _txns = txns; _attachments = attachments; _audit = audit;
    }

    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : Guid.Empty;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "admin";
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<OrderListItem>>>> List([FromQuery] OrderFilter filter)
        => Ok(ApiResponse<PagedResult<OrderListItem>>.Ok(await _svc.ListAsync(filter)));

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<OrderStats>>> Stats()
        => Ok(ApiResponse<OrderStats>.Ok(await _svc.StatsAsync()));

    /// <summary>Live financial aggregate over the filter — drives the summary bar.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<OrderFinanceSummary>>> Summary([FromQuery] OrderFilter filter)
        => Ok(ApiResponse<OrderFinanceSummary>.Ok(await _svc.SummaryAsync(filter)));

    [HttpGet("filter-meta")]
    public async Task<ActionResult<ApiResponse<OrderFilterMeta>>> FilterMeta()
        => Ok(ApiResponse<OrderFilterMeta>.Ok(await _svc.GetFilterMetaAsync()));

    [HttpPost("bulk-status")]
    public async Task<ActionResult<ApiResponse<int>>> BulkStatus([FromQuery] OrderStatus status, [FromBody] List<Guid> ids)
    {
        // ActorId is passed so the service can apply its super-admin gate on cancellation — this
        // endpoint is open to admin and operations, who may do everything here EXCEPT cancel.
        var (updated, error) = await _svc.BulkUpdateStatusAsync(ids, status, ActorId, ActorName);
        return error == null
            ? Ok(ApiResponse<int>.Ok(updated, "Updated"))
            : StatusCode(403, ApiResponse<int>.Fail(error));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] OrderFilter filter)
    {
        var csv = await _svc.ExportCsvAsync(filter);
        // Audit-log every export — captures user, time, IP, and the filters applied.
        await _audit.LogAsync(ActorId, ActorName, "OrdersExported", "Order", null,
            System.Text.Json.JsonSerializer.Serialize(new { ip = Ip, format = "csv", filter }));
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"orders-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<OrderDetail>>> Get(Guid id)
    {
        var d = await _svc.GetDetailAsync(id);
        return d == null ? NotFound(ApiResponse<OrderDetail>.Fail("Not found")) : Ok(ApiResponse<OrderDetail>.Ok(d));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<string>>> Create([FromBody] CreateOrderRequest request)
    {
        try { return Ok(ApiResponse<string>.Ok(await _svc.CreateAsync(request), "Order created")); }
        catch (InvalidOperationException ex) { return BadRequest(ApiResponse<string>.Fail(ex.Message)); }
    }

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<ApiResponse<string>>> SetStatus(Guid id, [FromQuery] OrderStatus status)
    {
        var (ok, error) = await _svc.UpdateStatusAsync(id, status, ActorId, ActorName);
        return ok
            ? Ok(ApiResponse<string>.Ok("ok", "Status updated"))
            : StatusCode(403, ApiResponse<string>.Fail(error!));
    }

    // ── Order item management ──
    [HttpGet("product-search")]
    public async Task<ActionResult<ApiResponse<List<ProductPickItem>>>> ProductSearch([FromQuery] string? q)
        => Ok(ApiResponse<List<ProductPickItem>>.Ok(await _svc.SearchProductsAsync(q)));

    /// <summary>Lightweight Active-product picker for the order-list Product filter dropdown.
    /// GET /api/admin/orders/products-for-filter?q=foo&amp;take=50 — returns up to <c>take</c> rows of
    /// <c>{ id, name }</c> sorted alphabetically. ORDER BY Title; supports search-while-typing.</summary>
    [HttpGet("products-for-filter")]
    public async Task<ActionResult<ApiResponse<List<RioCommerce.Core.DTOs.Meta.IdName>>>> ProductsForFilter(
        [FromQuery] string? q = null, [FromQuery] int take = 50)
        => Ok(ApiResponse<List<RioCommerce.Core.DTOs.Meta.IdName>>.Ok(await _svc.ListProductsForFilterAsync(q, take)));

    [HttpPut("{id:guid}/items")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateItem(Guid id, [FromBody] UpdateOrderItemRequest req)
    {
        var (ok, error) = await _svc.UpdateItemAsync(id, req, null, "api");
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Item updated")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<ApiResponse<string>>> AddItem(Guid id, [FromBody] AddOrderItemRequest req)
    {
        var (ok, error) = await _svc.AddItemAsync(id, req, null, "api");
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Item added")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveItem(Guid id, Guid itemId)
    {
        var (ok, error) = await _svc.RemoveItemAsync(id, itemId, null, "api");
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Item removed")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    // ── Payments & refunds ──
    [HttpGet("{id:guid}/transactions")]
    public async Task<ActionResult<ApiResponse<List<PaymentTransactionDto>>>> Transactions(Guid id)
        => Ok(ApiResponse<List<PaymentTransactionDto>>.Ok(await _txns.ListAsync(id)));

    [HttpPost("{id:guid}/manual-payment")]
    public async Task<ActionResult<ApiResponse<string>>> ManualPayment(Guid id, [FromBody] ManualPaymentRequest req)
    {
        var (ok, error) = await _txns.AddManualPaymentAsync(id, req, null, "api");
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Payment recorded")) : BadRequest(ApiResponse<string>.Fail(error!));
    }

    [HttpGet("{id:guid}/refund-summary")]
    public async Task<ActionResult<ApiResponse<RefundSummaryDto>>> RefundSummary(Guid id)
        => Ok(ApiResponse<RefundSummaryDto>.Ok(await _refunds.GetSummaryAsync(id)));

    [HttpPost("{id:guid}/refund")]
    public async Task<ActionResult<ApiResponse<Guid?>>> Refund(Guid id, [FromBody] RefundRequest req)
    {
        var (ok, error, refundId, pending) = await _refunds.CreateRefundAsync(id, req, null, "api");
        return ok
            ? Ok(ApiResponse<Guid?>.Ok(refundId, pending ? "Refund submitted for approval" : "Refund processed"))
            : BadRequest(ApiResponse<Guid?>.Fail(error!));
    }

    // ── Attachments ──
    [HttpGet("{id:guid}/attachments")]
    public async Task<ActionResult<ApiResponse<List<NoteAttachmentDto>>>> Attachments(Guid id)
        => Ok(ApiResponse<List<NoteAttachmentDto>>.Ok(await _attachments.ListForOrderAsync(id)));

    [HttpPost("{id:guid}/notes/{noteId:guid}/attachments")]
    public async Task<ActionResult<ApiResponse<Guid?>>> UploadAttachment(Guid id, Guid noteId, IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(ApiResponse<Guid?>.Fail("No file provided."));
        await using var s = file.OpenReadStream();
        var (ok, error, attId) = await _attachments.AttachAsync(id, noteId, file.FileName, file.ContentType, file.Length, s, null, "api");
        return ok ? Ok(ApiResponse<Guid?>.Ok(attId, "Uploaded")) : BadRequest(ApiResponse<Guid?>.Fail(error!));
    }

    [HttpDelete("attachments/{attachmentId:guid}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteAttachment(Guid attachmentId)
    {
        var ok = await _attachments.DeleteAsync(attachmentId, null, "api");
        return ok ? Ok(ApiResponse<string>.Ok("ok", "Deleted")) : NotFound(ApiResponse<string>.Fail("Not found"));
    }
}
