using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Services.SerialKeys.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

/// <summary>
/// Admin CRUD for RioPlay tenants. Temporary surface until the Razor admin page lands —
/// lets you seed credentials and rotate secrets via Swagger.
/// </summary>
[ApiController]
[Route("api/admin/serial-tenants/rioplay")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme + "," + JwtBearerDefaults.AuthenticationScheme,
    Roles = "super_admin,admin")]
public class RioPlayTenantsAdminController : ControllerBase
{
    private readonly IRioPlayTenantService _svc;
    private readonly RioPlayApiClient _api;
    public RioPlayTenantsAdminController(IRioPlayTenantService svc, RioPlayApiClient api) { _svc = svc; _api = api; }

    /// <summary>List all configured Rio tenants. Secret is never returned — only HasSecret flag.</summary>
    [HttpGet]
    public async Task<ActionResult<List<RioPlayTenantItem>>> List(CancellationToken ct)
        => await _svc.ListAsync(ct);

    /// <summary>Get one tenant by id. Secret is never returned.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RioPlayTenantEdit>> Get(Guid id, CancellationToken ct)
        => (await _svc.GetAsync(id, ct)) is { } row ? row : NotFound();

    /// <summary>Create a new tenant. <c>Secret</c> is required on create — encrypted at rest.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RioPlayTenantEdit model, CancellationToken ct)
    {
        model.Id = null;
        var (ok, err, id) = await _svc.SaveAsync(model, ct);
        return ok ? Ok(new { ok = true, id }) : BadRequest(new { ok = false, error = err });
    }

    /// <summary>Update an existing tenant. Leave <c>Secret</c> null to keep the existing one; set to rotate.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] RioPlayTenantEdit model, CancellationToken ct)
    {
        model.Id = id;
        var (ok, err, _) = await _svc.SaveAsync(model, ct);
        return ok ? Ok(new { ok = true }) : BadRequest(new { ok = false, error = err });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await _svc.DeleteAsync(id, ct) ? Ok(new { ok = true }) : NotFound();

    /// <summary>
    /// Diagnostic: fire a real NopSignUp at Rio using the saved tenant's credentials and return
    /// the FULL raw round-trip (status, Server header, response headers, body). Use this to see
    /// exactly what Rio's edge returns from THIS server — compare against your working Postman call.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<RioDiagnosticResult>> Test(Guid id, CancellationToken ct)
    {
        var creds = await _svc.ResolveCredentialsAsync(id, ct);
        if (creds == null) return NotFound(new { error = "Tenant not found or no secret set." });
        var result = await _api.TestConnectionAsync(creds.BaseUrl, creds.TenantId, creds.Secret, ct);
        return Ok(result);
    }
}
