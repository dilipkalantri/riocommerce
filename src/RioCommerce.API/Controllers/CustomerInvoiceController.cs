using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

// Thin passthrough: lets a logged-in customer/student view or download THEIR own invoice PDF.
// Ownership is enforced via CheckoutService.GetReceiptAsync(userId, orderNumber) — only
// the order's owner gets a non-null receipt. The PDF itself comes from the SAME
// IInvoiceService.RenderPdfAsync the School/Coordinator and Admin invoice views use — one
// invoice template and one PDF renderer for every buyer type. No second invoice
// template/generator is introduced here; this controller only resolves which existing
// Invoice row belongs to this order and hands it to the shared renderer.
[ApiController]
[Authorize] // cookie scheme (default) — matches the public Blazor pages
public class CustomerInvoiceController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IInvoiceService _invoices;
    public CustomerInvoiceController(ICheckoutService checkout, IInvoiceService invoices)
    {
        _checkout = checkout;
        _invoices = invoices;
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

        var invoice = await _invoices.GetByOrderAsync(receipt.Id);
        if (invoice == null) return NotFound();

        var res = await _invoices.RenderPdfAsync(invoice.Id);
        if (res == null) return NotFound();

        // "View Invoice" opens this inline in a new tab; "Download Invoice" (?download=1) forces
        // the browser's save dialog. Same bytes either way — just the disposition differs.
        //
        // Presence check, not a bound bool: [ApiController]'s automatic model validation rejects
        // any value bool.TryParse doesn't accept, including "1" — which every ?download= link in
        // this codebase actually sends. This is the SAME pattern InvoicesController (admin) already
        // uses for exactly this reason: Request.Query.ContainsKey("download") accepts "1", "true",
        // or even an empty value, and can never itself return a 400.
        var asAttachment = Request.Query.ContainsKey("download");
        if (!asAttachment)
            return File(res.Value.bytes, "application/pdf");
        return File(res.Value.bytes, "application/pdf", res.Value.filename);
    }
}
