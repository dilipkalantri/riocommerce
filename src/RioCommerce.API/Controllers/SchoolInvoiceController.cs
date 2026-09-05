using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.API.Controllers;

/// <summary>
/// School-portal invoice download.
///
/// Separate from the admin/customer invoice endpoints on purpose: this one authorises through the
/// SCHOOL relationship (invoice → StudentUserId → roster → caller's school) rather than by order
/// ownership, because the buyer on a school order is the principal while the invoice is raised
/// against a student. Admin and customer invoice access are untouched.
/// </summary>
[ApiController]
[Route("school/invoice")]
// PRINCIPAL ONLY. Invoices are the payment side of the portal, which a coordinator has no part in.
// The service-level check inside RenderInvoicePdfAsync stays as the second gate — it would already
// return null for a coordinator, because a coordinator can raise no enrolment and so owns no
// invoice — but the role gate stops the request before it reaches the database at all.
[Authorize(Roles = "school_principal")]
public class SchoolInvoiceController : ControllerBase
{
    private readonly ISchoolEnrollmentService _enrollment;
    private readonly ILogger<SchoolInvoiceController> _log;

    public SchoolInvoiceController(ISchoolEnrollmentService enrollment, ILogger<SchoolInvoiceController> log)
    {
        _enrollment = enrollment;
        _log = log;
    }

    /// <summary>
    /// Streams a student invoice. Without <c>?download=1</c> it renders INLINE so "View Invoice"
    /// opens the PDF in a tab; with it, the browser is told to save the file.
    /// </summary>
    [HttpGet("{invoiceId:guid}/pdf")]
    public async Task<IActionResult> Download(Guid invoiceId, [FromQuery] bool download, CancellationToken ct)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId)) return Unauthorized();

        // The service performs the authorisation. It returns null for "not yours" AND for
        // "no such invoice", so a principal cannot probe which invoice ids exist.
        var pdf = await _enrollment.RenderInvoicePdfAsync(userId, invoiceId, ct);
        if (pdf is null) return NotFound();

        // File(..., fileName) always sets an attachment disposition, so inline is set explicitly.
        if (download) return File(pdf.Value.bytes, "application/pdf", pdf.Value.filename);

        Response.Headers.ContentDisposition = $"inline; filename=\"{pdf.Value.filename}\"";
        return File(pdf.Value.bytes, "application/pdf");
    }
}
