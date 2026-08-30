using RioCommerce.Core.DTOs.Customers;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Single source of truth for "does this customer already exist?". Every entry point —
/// public registration, /account/register form post, admin Create Customer, admin Create Order
/// flow, future mobile app — funnels through here so the result is consistent and the audit
/// log is unified.
/// </summary>
public interface ICustomerDuplicateService
{
    /// <summary>Probe email + phone against the existing customer table. Pass <paramref name="excludeUserId"/>
    /// when the caller is editing a user and shouldn't collide with itself.
    /// Email comparison is case-insensitive; phone is exact match on the normalised value.</summary>
    Task<DuplicateCheckResult> CheckAsync(string? email, string? phone, Guid? excludeUserId = null);

    /// <summary>Light-weight check for the inline AJAX probe on the email field.</summary>
    Task<FieldAvailability> CheckEmailAvailableAsync(string? email, Guid? excludeUserId = null);

    /// <summary>Light-weight check for the inline AJAX probe on the phone field.</summary>
    Task<FieldAvailability> CheckPhoneAvailableAsync(string? phone, Guid? excludeUserId = null);

    /// <summary>Persist a blocked-duplicate-create attempt to the audit log. Captures source
    /// (Admin / Website / Checkout / API), IP, attempted email, attempted phone, and actor.</summary>
    Task LogDuplicateAttemptAsync(string? email, string? phone, string source,
        Guid? attemptedByUserId, string? attemptedByName, string? ipAddress, string field);
}
