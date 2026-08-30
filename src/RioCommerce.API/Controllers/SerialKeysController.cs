using System.Security.Claims;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RioCommerce.API.Controllers;

/// <summary>
/// Public + admin surface for the serial-key feature.
///
/// Admin routes (JWT bearer, role-gated — same scheme as every other *Admin controller):
///   POST /api/admin/serial-keys/regenerate/{recordId}
///   POST /api/admin/serial-keys/revoke/{recordId}
///   GET  /api/admin/serial-keys                — list with filters
///   GET  /api/admin/serial-keys/{recordId}     — full detail incl. req/resp bodies
///
/// Public routes (no auth — these are validation-grade reads, not key creation):
///   GET  /api/serial-keys/validate/{key}       — local DB lookup
///   POST /api/serial-keys/activate/{key}       — call provider's activate endpoint
///   GET  /api/serial-keys/status/{key}         — provider status or local fallback
/// </summary>
[ApiController]
public class SerialKeysController : ControllerBase
{
    private readonly ISerialKeyService _svc;

    public SerialKeysController(ISerialKeyService svc) { _svc = svc; }

    // ── Admin ───────────────────────────────────────────────────────────────

    [HttpGet("api/admin/serial-keys")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations,support")]
    public async Task<ActionResult<List<SerialKeyListItem>>> List(
        [FromQuery] RioCommerce.Core.Enums.SerialKeyStatus? status,
        [FromQuery] string? provider,
        [FromQuery] Guid? orderId,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var filter = new SerialKeyListFilter
        {
            Status = status,
            ProviderKey = provider,
            OrderId = orderId,
            Query = q,
            Page = page,
            PageSize = pageSize,
        };
        return await _svc.ListAsync(filter, ct);
    }

    [HttpGet("api/admin/serial-keys/{recordId:guid}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations,support")]
    public async Task<ActionResult<SerialKeyDetail>> Get(Guid recordId, CancellationToken ct)
        => (await _svc.GetAsync(recordId, ct)) is { } d ? d : NotFound();

    [HttpPost("api/admin/serial-keys/regenerate/{recordId:guid}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
    public async Task<IActionResult> Regenerate(Guid recordId, CancellationToken ct)
    {
        var actor = ActorId();
        var ok = await _svc.RegenerateAsync(recordId, actor, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    [HttpPost("api/admin/serial-keys/revoke/{recordId:guid}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin")]
    public async Task<IActionResult> Revoke(Guid recordId, [FromBody] RevokeBody body, CancellationToken ct)
    {
        var actor = ActorId();
        var ok = await _svc.RevokeAsync(recordId, body?.Reason ?? "", actor, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    public record RevokeBody(string Reason);

    // ── Public-ish ──────────────────────────────────────────────────────────
    // These don't need auth — they're read/activate against a key the caller already holds.
    // Rate-limit at infra level if abuse becomes a concern.

    [HttpGet("api/serial-keys/validate/{key}")]
    public async Task<IActionResult> Validate(string key, CancellationToken ct)
    {
        var d = await _svc.ValidateAsync(key, ct);
        if (d == null) return NotFound(new { valid = false });
        return Ok(new
        {
            valid = true,
            d.Status,
            d.OrderNumber,
            d.ProductTitle,
            d.GeneratedAt,
            d.ActivatedAt,
        });
    }

    [HttpPost("api/serial-keys/activate/{key}")]
    public async Task<IActionResult> Activate(string key, CancellationToken ct)
    {
        var result = await _svc.ActivateAsync(key, ct);
        return result.Success
            ? Ok(new { ok = true, externalReference = result.ExternalReference })
            : BadRequest(new { ok = false, code = result.ErrorCode, message = result.ErrorMessage });
    }

    [HttpGet("api/serial-keys/status/{key}")]
    public async Task<IActionResult> Status(string key, CancellationToken ct)
    {
        var result = await _svc.GetStatusAsync(key, ct);
        return result.Success
            ? Ok(new { ok = true, status = result.Status })
            : NotFound(new { ok = false, code = result.ErrorCode, message = result.ErrorMessage });
    }

    private Guid? ActorId()
        => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;
}
