using RioCommerce.Core.DTOs.Sharing;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

/// <summary>
/// Excel I/O endpoints for the two sharing tables. Used by the /admin/revenue-sharing page —
/// download to get current rules or a blank template, upload to bulk-apply changes.
///
/// Auth: accepts BOTH cookie and JWT bearer. Cookie is required because the admin Razor pages
/// use plain <c>&lt;a href download&gt;</c> links — those only carry cookies. Bearer is kept for
/// any future API consumers (mobile, integrations).
/// </summary>
[ApiController]
[Route("api/admin/sharing")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme,
    Roles = "super_admin,admin,backend_user")]
public class SharingImportExportController : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ISharingImportExportService _svc;
    public SharingImportExportController(ISharingImportExportService svc) { _svc = svc; }

    // ── Franchise commissions ─────────────────────────────────────────────────

    [HttpGet("franchise/export")]
    public async Task<IActionResult> ExportFranchise(CancellationToken ct)
        => File(await _svc.ExportFranchiseCommissionsAsync(ct), XlsxMime,
                $"FranchiseCommissions_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");

    [HttpGet("franchise/template")]
    public async Task<IActionResult> TemplateFranchise(CancellationToken ct)
        => File(await _svc.GetFranchiseCommissionTemplateAsync(ct), XlsxMime,
                "FranchiseCommissions_Template.xlsx");

    [HttpPost("franchise/import")]
    [RequestSizeLimit(10 * 1024 * 1024)]   // 10MB cap — comfortably more than any sane sharing sheet
    public async Task<ActionResult<SharingImportResult>> ImportFranchise([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var (actorId, actorName) = ActorFromClaims();
        var result = await _svc.ImportFranchiseCommissionsAsync(ms.ToArray(), actorId, actorName, ct);
        return Ok(result);
    }

    // ── Faculty shares ────────────────────────────────────────────────────────

    [HttpGet("faculty/export")]
    public async Task<IActionResult> ExportFaculty(CancellationToken ct)
        => File(await _svc.ExportFacultySharesAsync(ct), XlsxMime,
                $"FacultyShares_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");

    [HttpGet("faculty/template")]
    public async Task<IActionResult> TemplateFaculty(CancellationToken ct)
        => File(await _svc.GetFacultyShareTemplateAsync(ct), XlsxMime,
                "FacultyShares_Template.xlsx");

    [HttpPost("faculty/import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<SharingImportResult>> ImportFaculty([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var (actorId, actorName) = ActorFromClaims();
        var result = await _svc.ImportFacultySharesAsync(ms.ToArray(), actorId, actorName, ct);
        return Ok(result);
    }

    private (Guid? id, string? name) ActorFromClaims()
    {
        Guid? id = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;
        var name = User.Identity?.Name;
        return (id, name);
    }
}
