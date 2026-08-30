using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

/// <summary>Lets a logged-in customer download THEIR own order receipt (any status).
/// Ownership is enforced via CheckoutService.GetReceiptAsync — only the order owner gets one.</summary>
[ApiController]
[Authorize]
public class CustomerReceiptController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IReceiptService _receipts;
    public CustomerReceiptController(ICheckoutService checkout, IReceiptService receipts)
    {
        _checkout = checkout; _receipts = receipts;
    }

    [HttpGet("api/me/orders/{orderNumber}/receipt.pdf")]
    public async Task<IActionResult> Download(string orderNumber, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();
        var owned = await _checkout.GetReceiptAsync(userId, orderNumber);
        if (owned == null) return NotFound();

        var res = await _receipts.RenderPdfAsync(owned.Id, ct);
        if (res == null) return NotFound();
        var asAttachment = Request.Query.ContainsKey("download");
        return File(res.Value.bytes, "application/pdf", asAttachment ? res.Value.filename : null);
    }
}

/// <summary>Admin receipt download for ANY order (any status). Invoices remain paid-only and
/// are served by <see cref="InvoicesController"/>.</summary>
[ApiController]
[Route("api/admin/orders")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme,
    Roles = "super_admin,admin,operations,support,backend_user,accounts")]
public class AdminReceiptController : ControllerBase
{
    private readonly IReceiptService _receipts;
    public AdminReceiptController(IReceiptService receipts) { _receipts = receipts; }

    [HttpGet("{orderId:guid}/receipt.pdf")]
    public async Task<IActionResult> Receipt(Guid orderId, CancellationToken ct)
    {
        var res = await _receipts.RenderPdfAsync(orderId, ct);
        if (res == null) return NotFound(new { error = "Order not found." });
        var asAttachment = Request.Query.ContainsKey("download");
        return File(res.Value.bytes, "application/pdf", asAttachment ? res.Value.filename : null);
    }

    /// <summary>Franchise financial bifurcation for an order (admin/auditing view).</summary>
    [HttpGet("{orderId:guid}/bifurcation")]
    public async Task<ActionResult<ApiResponse<FranchiseBifurcation>>> Bifurcation(Guid orderId, CancellationToken ct)
    {
        var b = await _receipts.GetBifurcationAsync(orderId, ct);
        return b == null
            ? NotFound(ApiResponse<FranchiseBifurcation>.Fail("Order not found."))
            : Ok(ApiResponse<FranchiseBifurcation>.Ok(b));
    }
}

/// <summary>
/// Franchisee browser-download endpoints (receipt + tax invoice) for the franchisee's OWN orders.
/// Dual auth (cookie + JWT) so plain &lt;a href download&gt; links from the portal work. Ownership
/// is enforced via <see cref="IFranchisePortalService.OwnsOrderAsync"/>. The invoice is paid-only
/// (the invoice service refuses unpaid orders) and is raised in the franchisee's name with full
/// per-product bifurcation.
/// </summary>
[ApiController]
[Route("api/franchise/orders")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme,
    Roles = "franchise_admin")]
public class FranchiseDownloadController : ControllerBase
{
    private readonly IFranchisePortalService _portal;
    private readonly IReceiptService _receipts;
    private readonly IInvoiceService _invoices;
    public FranchiseDownloadController(IFranchisePortalService portal, IReceiptService receipts, IInvoiceService invoices)
    {
        _portal = portal; _receipts = receipts; _invoices = invoices;
    }

    private async Task<Guid?> ScopeAsync()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
            ? await _portal.ResolveFranchiseIdAsync(uid) : null;

    /// <summary>Customer receipt for one of the franchisee's own orders (any status).</summary>
    [HttpGet("{orderId:guid}/receipt.pdf")]
    public async Task<IActionResult> Receipt(Guid orderId, CancellationToken ct)
    {
        var fid = await ScopeAsync();
        if (fid == null) return Unauthorized();
        if (!await _portal.OwnsOrderAsync(fid.Value, orderId)) return NotFound();
        var res = await _receipts.RenderPdfAsync(orderId, ct);
        if (res == null) return NotFound();
        var asAttachment = Request.Query.ContainsKey("download");
        return File(res.Value.bytes, "application/pdf", asAttachment ? res.Value.filename : null);
    }

    /// <summary>Tax invoice (franchisee's name, paid-only, with bifurcation) for an own order.</summary>
    [HttpGet("{orderId:guid}/invoice.pdf")]
    public async Task<IActionResult> Invoice(Guid orderId, CancellationToken ct)
    {
        var fid = await ScopeAsync();
        if (fid == null) return Unauthorized();
        if (!await _portal.OwnsOrderAsync(fid.Value, orderId)) return NotFound();

        // Idempotent; only generates for paid orders.
        await _invoices.EnsureForOrderAsync(orderId, actorName: "franchise-portal", ct: ct);
        var inv = await _invoices.GetByOrderAsync(orderId, ct);
        if (inv == null) return NotFound(new { error = "Invoice is available only after payment is confirmed." });
        var res = await _invoices.RenderPdfAsync(inv.Id, ct);
        if (res == null) return NotFound();
        var asAttachment = Request.Query.ContainsKey("download");
        return File(res.Value.bytes, "application/pdf", asAttachment ? res.Value.filename : null);
    }
}
