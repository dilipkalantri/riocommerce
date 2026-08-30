using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace RioCommerce.API.Controllers;

/// <summary>
/// REST surface for the admin "Smart Customer Search" feature. Powers both the live-suggestion
/// dropdown on the Customers page and any third-party integration that needs the same lookup.
/// Authenticated admin/staff only.
/// </summary>
[ApiController]
[Route("api/admin/customers")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "super_admin,admin,operations")]
public class AdminCustomersSearchController : ControllerBase
{
    private readonly IAdminUserService _users;
    private readonly ICustomerDuplicateService _dupes;
    public AdminCustomersSearchController(IAdminUserService users, ICustomerDuplicateService dupes)
    { _users = users; _dupes = dupes; }

    private Guid ActorId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : Guid.Empty;
    private string ActorName => User.FindFirstValue(ClaimTypes.Name) ?? "admin";
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>GET /api/admin/customers/search?q=sunil&amp;take=10 — live suggestions for the
    /// auto-complete. Returns an empty list for queries shorter than 2 characters.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<CustomerSuggestion>>>> Search(
        [FromQuery] string q = "", [FromQuery] int take = 10)
        => Ok(ApiResponse<List<CustomerSuggestion>>.Ok(await _users.SearchCustomersAsync(q, take)));

    /// <summary>GET /api/admin/customers/check-duplicate?email=...&amp;phone=...&amp;excludeId=...
    /// Admin-side duplicate probe used by Create Order / Create Customer flows BEFORE the user
    /// commits the form, so the modal can render the existing customer card preemptively.</summary>
    [HttpGet("check-duplicate")]
    public async Task<ActionResult<ApiResponse<DuplicateCheckResult>>> CheckDuplicate(
        [FromQuery] string? email = null, [FromQuery] string? phone = null, [FromQuery] Guid? excludeId = null)
        => Ok(ApiResponse<DuplicateCheckResult>.Ok(await _dupes.CheckAsync(email, phone, excludeId)));

    /// <summary>POST /api/admin/customers — create a customer from the "➕ Create New Customer"
    /// dropdown CTA. Returns <b>409 Conflict</b> with <see cref="DuplicateApiResponse"/> when the
    /// email or phone is already taken — including the existing customer's identity for the modal.</summary>
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] CustomerCreateRequest req)
    {
        var (ok, error, id, duplicate) = await _users.CreateCustomerAsync(req, ActorId, ActorName, Ip);
        if (ok) return Ok(ApiResponse<Guid>.Ok(id, "Customer created"));
        if (duplicate is { HasDuplicate: true })
            return Conflict(new DuplicateApiResponse(false, error ?? "Customer already exists.",
                duplicate.FieldName, duplicate.Existing));
        return BadRequest(ApiResponse<Guid>.Fail(error!));
    }
}
