namespace RioCommerce.Core.DTOs.SerialKeys;

/// <summary>
/// Provider response, normalised. Providers <b>never throw</b> out of their public surface —
/// any HTTP failure / parse error becomes a non-success result. That keeps the orchestrator's
/// retry logic simple: one branch for ok, one for not-ok.
/// </summary>
public class GenerateKeyResult
{
    public bool Success { get; init; }
    public string? SerialKey { get; init; }
    public string? ExternalReference { get; init; }

    /// <summary>True when the provider both created and activated the key. False when it was create-only.</summary>
    public bool Activated { get; init; }

    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>The literal JSON body we sent — captured here so the caller can persist it for audit.</summary>
    public string? RawRequest { get; init; }
    /// <summary>The literal JSON body we got back — captured here so the caller can persist it for audit.</summary>
    public string? RawResponse { get; init; }

    // ── Registration-style outcome (Superclass) — null for key-issuing providers ──────────
    /// <summary>Subscription/access expiry the provider computed or returned (Superclass validity).
    /// Persisted to <c>SerialKeyRecord.ExpiresAt</c>.</summary>
    public DateTime? ExpiresAt { get; init; }
    /// <summary>Short subscription status text (e.g. "active", "already_provisioned"). Persisted to
    /// <c>SerialKeyRecord.SubscriptionStatus</c>.</summary>
    public string? SubscriptionStatus { get; init; }
    /// <summary>Provider-side course/combo reference actually assigned. Persisted to
    /// <c>SerialKeyRecord.ProviderCourseRef</c>.</summary>
    public string? ProviderCourseRef { get; init; }

    public static GenerateKeyResult Ok(string key, string? externalRef, bool activated, string raw)
        => new() { Success = true, SerialKey = key, ExternalReference = externalRef, Activated = activated, RawResponse = raw };

    /// <summary>Success for a registration-style (keyless) provider: no <c>SerialKey</c>, but a
    /// student/external reference plus subscription/expiry data. <c>Activated</c> is true because
    /// these providers provision access in the same call.</summary>
    public static GenerateKeyResult OkRegistration(
        string? externalRef, string? subscriptionStatus, DateTime? expiresAt, string? courseRef,
        string? rawRequest, string? rawResponse)
        => new()
        {
            Success = true,
            Activated = true,
            ExternalReference = externalRef,
            SubscriptionStatus = subscriptionStatus,
            ExpiresAt = expiresAt,
            ProviderCourseRef = courseRef,
            RawRequest = rawRequest,
            RawResponse = rawResponse,
        };

    public static GenerateKeyResult Fail(string code, string message, string? raw = null)
        => new() { Success = false, ErrorCode = code, ErrorMessage = message, RawResponse = raw };
}

public class ActivateKeyResult
{
    public bool Success { get; init; }
    public string? ExternalReference { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawResponse { get; init; }

    public static ActivateKeyResult Ok(string? externalRef, string raw)
        => new() { Success = true, ExternalReference = externalRef, RawResponse = raw };
    public static ActivateKeyResult Fail(string code, string message, string? raw = null)
        => new() { Success = false, ErrorCode = code, ErrorMessage = message, RawResponse = raw };
}

public class KeyStatusResult
{
    public bool Success { get; init; }
    public string? Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static KeyStatusResult Ok(string status) => new() { Success = true, Status = status };
    public static KeyStatusResult Fail(string code, string message)
        => new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
