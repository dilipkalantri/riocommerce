using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.API.Controllers;

/// <summary>
/// Serves the blank .xlsx template for the School Portal student import.
///
/// A controller rather than a JS blob because the file is generated server-side from the SAME
/// column list the parser reads, so the template a writer downloads can never drift from what the
/// import accepts. Nothing is uploaded here — parsing happens in the Blazor component, which holds
/// the file in server memory and never round-trips it through the browser.
/// </summary>
[ApiController]
[Route("school/students")]
// Both roles: importing students is a coordinator's core job, and the principal has it too.
// Enrolment and invoices remain principal-only; this endpoint returns an empty template and
// touches no student data at all.
[Authorize(Roles = "school_principal,school_coordinator")]
public class SchoolStudentImportController : ControllerBase
{
    private readonly ISchoolStudentService _students;

    public SchoolStudentImportController(ISchoolStudentService students) => _students = students;

    [HttpGet("import-template.xlsx")]
    public async Task<IActionResult> Template(CancellationToken ct)
    {
        // Authenticated staff only, and still resolved to a school — an account with no school link
        // has no business collecting a roster template.
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(raw, out var userId)) return Unauthorized();
        if (await _students.ResolveSchoolIdAsync(userId, ct) is null) return Forbid();

        var bytes = _students.BuildImportTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Vijaypath_Student_Import_Sample.xlsx");
    }
}
