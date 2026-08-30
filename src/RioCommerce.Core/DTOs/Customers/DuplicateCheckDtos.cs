namespace RioCommerce.Core.DTOs.Customers;

/// <summary>
/// Outcome of a duplicate-customer probe. Centralised so EVERY entry point — public registration,
/// /account/register form post, admin Create Customer modal, Create Order flow, future mobile app —
/// reads the same shape and surfaces consistent errors.
/// </summary>
public record DuplicateCheckResult(
    bool HasDuplicate,
    DuplicateField Field,                    // None, Email, Phone, Both
    DuplicateCustomerSummary? Existing       // populated when HasDuplicate is true
)
{
    public static DuplicateCheckResult None() => new(false, DuplicateField.None, null);
    /// <summary>Camel-cased label that matches the API response contract ("Email" / "Phone" / "Both").</summary>
    public string FieldName => Field switch
    {
        DuplicateField.Email => "Email",
        DuplicateField.Phone => "Phone",
        DuplicateField.Both  => "Both",
        _                    => "None"
    };
}

public enum DuplicateField { None = 0, Email = 1, Phone = 2, Both = 3 }

/// <summary>Snapshot of an existing customer shown on the "Customer Already Exists" admin modal.</summary>
public record DuplicateCustomerSummary(
    Guid Id, string FullName, string? Email, string? Phone, DateTime CreatedAt);

/// <summary>Reply shape for the inline AJAX availability probes
/// (<c>GET /api/auth/check-email</c>, <c>GET /api/auth/check-phone</c>).
/// Public-friendly — never leaks names / ids of existing customers.</summary>
public record FieldAvailability(bool Available, string? Message);

/// <summary>Structured 409 body returned by every CreateCustomer endpoint.</summary>
public record DuplicateApiResponse(
    bool Success,                            // always false
    string Message,
    string DuplicateField,                   // "Email" | "Phone" | "Both"
    DuplicateCustomerSummary? Existing);
