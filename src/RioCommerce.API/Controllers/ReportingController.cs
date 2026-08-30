using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

/// <summary>
/// The Order Management &amp; Reporting module's HTTP surface: run any of the eight reports, and
/// download any of them as Excel or CSV.
///
/// <para>Auth accepts BOTH cookie and JWT bearer, matching <c>ReportsExportController</c>. The
/// cookie is what makes the download links work — the report page uses plain <c>&lt;a href&gt;</c>
/// anchors, which carry only the auth cookie — while bearer keeps the endpoints usable from an API
/// client. Role checks are identical either way (§36.20).</para>
/// </summary>
[ApiController]
[Route("api/admin/reporting")]
public class ReportingController : ControllerBase
{
    private const string BothSchemes =
        CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme;
    private const string AdminRoles = "super_admin,admin,operations,backend_user";
    private const string FranchiseRoles = "franchise_admin";

    private readonly IReportingService _reports;
    private readonly IReportExportService _export;
    private readonly IFranchisePortalService _portal;

    public ReportingController(
        IReportingService reports, IReportExportService export, IFranchisePortalService portal)
    {
        _reports = reports;
        _export = export;
        _portal = portal;
    }

    // ────────── Admin ──────────

    [HttpGet("{slug}")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = AdminRoles)]
    public async Task<ActionResult<ApiResponse<ReportTable>>> Run(
        string slug, [FromQuery] ReportQuery query, CancellationToken ct)
    {
        if (Resolve(slug) is not { } type) return BadRequest(UnknownSlug(slug));
        // A caller must never be able to widen scope through the query string; admin scope is
        // global, so any inbound value is discarded rather than trusted.
        query.FranchiseScopeId = null;
        return Ok(ApiResponse<ReportTable>.Ok(await _reports.RunAsync(type, query, ct)));
    }

    [HttpGet("{slug}/export.{format}")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = AdminRoles)]
    public async Task<IActionResult> Export(
        string slug, string format, [FromQuery] ReportQuery query, CancellationToken ct)
    {
        if (Resolve(slug) is not { } type) return BadRequest(UnknownSlug(slug));
        if (!IsSupportedFormat(format)) return BadRequest("Unsupported format. Use xlsx or csv.");

        query.FranchiseScopeId = null;
        var (bytes, filename, mime) = await _export.ExportAsync(type, query, format, ct);
        return File(bytes, mime, filename);
    }

    [HttpGet("options")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = AdminRoles)]
    public async Task<ActionResult<ApiResponse<ReportFilterOptions>>> Options([FromQuery] ReportQuery? query, CancellationToken ct)
        // The query is optional: without it the endpoint answers exactly as it always did, so any
        // existing caller keeps working. With it, the catalog pickers cascade.
        => Ok(ApiResponse<ReportFilterOptions>.Ok(await _reports.GetFilterOptionsAsync(query, ct)));

    // ────────── Franchisee ──────────
    //
    // A franchisee sees only their own franchise, and the scope is resolved server-side from their
    // user id rather than read from the request. Faculty-facing reports are not exposed here at
    // all: teacher remuneration is not a franchisee's business.

    [HttpGet("franchise/{slug}")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = FranchiseRoles)]
    public async Task<ActionResult<ApiResponse<ReportTable>>> RunScoped(
        string slug, [FromQuery] ReportQuery query, CancellationToken ct)
    {
        if (Resolve(slug) is not { } type) return BadRequest(UnknownSlug(slug));
        if (!FranchiseVisible(type)) return Forbid();

        var scope = await ResolveScopeAsync();
        if (scope == null) return Unauthorized();

        query.FranchiseScopeId = scope;
        query.FranchiseIds.Clear();
        return Ok(ApiResponse<ReportTable>.Ok(await _reports.RunAsync(type, query, ct)));
    }

    [HttpGet("franchise/{slug}/export.{format}")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = FranchiseRoles)]
    public async Task<IActionResult> ExportScoped(
        string slug, string format, [FromQuery] ReportQuery query, CancellationToken ct)
    {
        if (Resolve(slug) is not { } type) return BadRequest(UnknownSlug(slug));
        if (!FranchiseVisible(type)) return Forbid();
        if (!IsSupportedFormat(format)) return BadRequest("Unsupported format. Use xlsx or csv.");

        var scope = await ResolveScopeAsync();
        if (scope == null) return Unauthorized();

        query.FranchiseScopeId = scope;
        query.FranchiseIds.Clear();
        var (bytes, filename, mime) = await _export.ExportAsync(type, query, format, ct);
        return File(bytes, mime, filename);
    }

    // ────────── helpers ──────────

    private static ReportType? Resolve(string slug) => ReportCatalog.FromSlug(slug);

    private static bool IsSupportedFormat(string format) =>
        format.Equals("xlsx", StringComparison.OrdinalIgnoreCase)
        || format.Equals("csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>Which reports a franchise-portal user may run. Faculty-Wise and Teacher Settlement
    /// are excluded outright — they are the institute's payroll.</summary>
    private static bool FranchiseVisible(ReportType type) => type
        is ReportType.Sales
        or ReportType.Gst
        or ReportType.Franchisee
        or ReportType.ProductSubject
        or ReportType.FranchiseeReInvoice
        or ReportType.Shipping;

    private static string UnknownSlug(string slug) =>
        $"Unknown report '{slug}'. Use {ReportCatalog.Slugs}.";

    private async Task<Guid?> ResolveScopeAsync()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)) return null;
        return await _portal.ResolveFranchiseIdAsync(uid);
    }
}
