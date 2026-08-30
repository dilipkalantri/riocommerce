using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

/// <summary>
/// PDF download endpoint for admin-side invoice viewing. The Razor admin pages call
/// <c>/api/admin/invoices/{id}/pdf</c> to fetch a generated PDF stream. All other invoice
/// operations (list, get, generate, cancel) go through <see cref="IInvoiceService"/> directly
/// from the Razor server-side components — no REST surface needed.
///
/// Auth: accepts BOTH cookie and JWT bearer so plain <c>&lt;a href download&gt;</c> links from
/// the admin pages work (cookie-only) AND token-based callers (mobile / integrations) work.
/// </summary>
[ApiController]
[Route("api/admin/invoices")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme,
    Roles = "super_admin,admin,operations,support,backend_user,accounts")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _svc;
    public InvoicesController(IInvoiceService svc) { _svc = svc; }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var result = await _svc.RenderPdfAsync(id, ct);
        if (result == null) return NotFound(new { error = "Invoice not found." });
        // inline so it previews in-browser; downloadable via the right-click menu
        // or the explicit "Download" link in the admin UI which appends ?download=1.
        var asAttachment = Request.Query.ContainsKey("download");
        return File(result.Value.bytes, "application/pdf",
            asAttachment ? result.Value.filename : null);
    }
}
