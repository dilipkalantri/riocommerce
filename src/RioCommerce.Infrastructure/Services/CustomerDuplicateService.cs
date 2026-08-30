using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Centralised duplicate-customer probe. Every CreateCustomer entry point on the platform
/// reads from here so the validation, the error shape, and the audit log all line up.
/// Normalisation rules:
///   • Email is compared case-insensitively (DB column stored as-is)
///   • Phone is compared exactly after trim — callers are expected to send the canonical form
/// The Users table already has unique indexes on Email and Phone (filtered NOT NULL), so a race
/// that slips past this check still gets stopped by Postgres — callers should still wrap
/// SaveChangesAsync in try/catch for DbUpdateException to surface a clean message in that case.
/// </summary>
public class CustomerDuplicateService : ICustomerDuplicateService
{
    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    public CustomerDuplicateService(RioCommerceDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    public async Task<DuplicateCheckResult> CheckAsync(string? email, string? phone, Guid? excludeUserId = null)
    {
        var e = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var p = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (e == null && p == null) return DuplicateCheckResult.None();

        var eLower = e?.ToLowerInvariant();
        // One round-trip — pick up everything that matches either field, then decide which side hit.
        var hits = await _db.Users.Where(u =>
                (u.Id != excludeUserId) &&
                ((e != null && u.Email != null && u.Email.ToLower() == eLower) ||
                 (p != null && u.Phone != null && u.Phone == p)))
            .Select(u => new { u.Id, u.FullName, u.Email, u.Phone, u.CreatedAt })
            .Take(2)
            .ToListAsync();

        if (hits.Count == 0) return DuplicateCheckResult.None();

        var emailHit = e != null && hits.Any(h => h.Email != null && string.Equals(h.Email, e, StringComparison.OrdinalIgnoreCase));
        var phoneHit = p != null && hits.Any(h => h.Phone == p);
        var field = (emailHit, phoneHit) switch
        {
            (true, true)   => DuplicateField.Both,
            (true, false)  => DuplicateField.Email,
            (false, true)  => DuplicateField.Phone,
            _              => DuplicateField.None
        };
        if (field == DuplicateField.None) return DuplicateCheckResult.None();

        // Prefer the row that matches the most fields, then the oldest (longest-standing customer).
        var existing = hits
            .OrderByDescending(h => (h.Email != null && string.Equals(h.Email, e, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                                  + (h.Phone == p ? 1 : 0))
            .ThenBy(h => h.CreatedAt)
            .First();
        return new DuplicateCheckResult(true, field,
            new DuplicateCustomerSummary(existing.Id, existing.FullName, existing.Email, existing.Phone, existing.CreatedAt));
    }

    public async Task<FieldAvailability> CheckEmailAvailableAsync(string? email, Guid? excludeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(email)) return new FieldAvailability(true, null);
        var e = email.Trim().ToLowerInvariant();
        var taken = await _db.Users.AnyAsync(u => u.Id != excludeUserId && u.Email != null && u.Email.ToLower() == e);
        return taken
            ? new FieldAvailability(false, "This email is already registered.")
            : new FieldAvailability(true, "Available");
    }

    public async Task<FieldAvailability> CheckPhoneAvailableAsync(string? phone, Guid? excludeUserId = null)
    {
        if (string.IsNullOrWhiteSpace(phone)) return new FieldAvailability(true, null);
        var p = phone.Trim();
        var taken = await _db.Users.AnyAsync(u => u.Id != excludeUserId && u.Phone == p);
        return taken
            ? new FieldAvailability(false, "This mobile number is already registered.")
            : new FieldAvailability(true, "Available");
    }

    public async Task LogDuplicateAttemptAsync(string? email, string? phone, string source,
        Guid? attemptedByUserId, string? attemptedByName, string? ipAddress, string field)
    {
        // Best-effort — audit failures must never block the user-facing response.
        try
        {
            var details = System.Text.Json.JsonSerializer.Serialize(new
            {
                email,
                phone,
                source,                          // "Admin" / "Website" / "Checkout" / "API"
                ip = ipAddress,
                field,                           // "Email" / "Phone" / "Both"
                at = DateTime.UtcNow
            });
            await _audit.LogAsync(attemptedByUserId, attemptedByName ?? "anonymous",
                "DuplicateCustomerBlocked", "User", null, details);
        }
        catch { /* swallow — duplicate prevention takes priority over telemetry */ }
    }
}
