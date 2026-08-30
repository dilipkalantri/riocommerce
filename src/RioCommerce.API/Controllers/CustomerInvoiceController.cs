using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

// Thin passthrough: lets a logged-in customer download THEIR own invoice PDF.
// Ownership is enforced via CheckoutService.GetReceiptAsync(userId, orderNumber) — only
// the order's owner gets a non-null receipt. The PDF itself comes from the existing
// IExportService.InvoicePdfAsync — no new invoice generation logic is added.
[ApiController]
[Authorize] // cookie scheme (default) — matches the public Blazor pages
public class CustomerInvoiceController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IExportService _export;
    public CustomerInvoiceController(ICheckoutService checkout, IExportService export)
    {
        _checkout = checkout;
        _export = export;
    }

    [HttpGet("api/me/orders/{orderNumber}/invoice.pdf")]
    public async Task<IActionResult> Download(string orderNumber)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var receipt = await _checkout.GetReceiptAsync(userId, orderNumber);
        if (receipt == null) return NotFound();

        // Policy: a customer/student only ever gets a RECEIPT. For franchise-placed orders the tax
        // invoice is the franchisee's document and must never be issued to the customer.
        if (receipt.IsFranchiseOrder)
            return Forbid();

        var res = await _export.InvoicePdfAsync(receipt.Id, franchiseScopeId: null);
        return res == null ? NotFound() : File(res.Value.bytes, "application/pdf", res.Value.filename);
    }
}
