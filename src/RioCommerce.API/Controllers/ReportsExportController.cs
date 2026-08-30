using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

// One route surface for all 7 report types + invoice PDF.
// Admin = global scope (filter.FranchiseId honored); Franchisee = own franchise (scope server-resolved).
//
// Auth: accepts BOTH cookie and JWT bearer, same as SharingImportExportController. Cookie is
// required because the Reports & Exports pages download through plain <a href> links, which carry
// only the auth cookie; bearer stays for API consumers. Role checks and the franchise scope
// resolution are unchanged either way.
[ApiController]
public class ReportsExportController : ControllerBase
{
    private const string BothSchemes =
        CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme;

    private readonly IExportService _svc;
    private readonly IFranchisePortalService _portal;
    public ReportsExportController(IExportService svc, IFranchisePortalService portal)
    {
        _svc = svc;
        _portal = portal;
    }

    private Guid? UserIdOrNull => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    // ────────── Admin ──────────

    [HttpGet("api/admin/reports/{type}.xlsx")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = "super_admin,admin,operations")]
    public Task<IActionResult> AdminExcel(string type, [FromQuery] ReportFilter filter) => RenderAsync(type, filter, excel: true, scope: null);

    [HttpGet("api/admin/reports/{type}.pdf")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = "super_admin,admin,operations")]
    public Task<IActionResult> AdminPdf(string type, [FromQuery] ReportFilter filter) => RenderAsync(type, filter, excel: false, scope: null);

    [HttpGet("api/admin/orders/{orderId:guid}/invoice.pdf")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = "super_admin,admin,operations")]
    public async Task<IActionResult> AdminInvoice(Guid orderId)
    {
        var res = await _svc.InvoicePdfAsync(orderId, franchiseScopeId: null);
        return res == null ? NotFound() : File(res.Value.bytes, "application/pdf", res.Value.filename);
    }

    // ────────── Franchisee ──────────

    [HttpGet("api/franchise/reports/{type}.xlsx")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = "franchise_admin")]
    public async Task<IActionResult> FranchiseExcel(string type, [FromQuery] ReportFilter filter)
    {
        var fid = await ResolveScopeAsync();
        if (fid == null) return Unauthorized();
        return await RenderAsync(type, filter, excel: true, scope: fid);
    }

    [HttpGet("api/franchise/reports/{type}.pdf")]
    [Authorize(AuthenticationSchemes = BothSchemes, Roles = "franchise_admin")]
    public async Task<IActionResult> FranchisePdf(string type, [FromQuery] ReportFilter filter)
    {
        var fid = await ResolveScopeAsync();
        if (fid == null) return Unauthorized();
        return await RenderAsync(type, filter, excel: false, scope: fid);
    }

    // ────────── Shared dispatch ──────────

    private async Task<IActionResult> RenderAsync(string type, ReportFilter filter, bool excel, Guid? scope)
    {
        var (bytes, filename) = type.ToLowerInvariant() switch
        {
            "orders" => await _svc.OrdersAsync(filter, excel, scope),
            "commission" => await _svc.CommissionAsync(filter, excel, scope),
            "faculty-share" => await _svc.FacultyShareAsync(filter, excel, scope),
            "wallet" => await _svc.WalletAsync(filter, excel, scope),
            "customers" => await _svc.CustomersAsync(filter, excel, scope),
            "gst" => await _svc.GstAsync(filter, excel, scope),
            "b2b" => await _svc.B2bAsync(filter, excel, scope),
            "b2c" => await _svc.B2cAsync(filter, excel, scope),
            _ => (Array.Empty<byte>(), "")
        };
        if (bytes.Length == 0) return BadRequest($"Unknown report type '{type}'. Use orders|commission|faculty-share|wallet|customers|gst|b2b|b2c.");
        var mime = excel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf";
        return File(bytes, mime, filename);
    }

    private async Task<Guid?> ResolveScopeAsync()
    {
        var uid = UserIdOrNull;
        if (uid == null) return null;
        return await _portal.ResolveFranchiseIdAsync(uid.Value);
    }
}
