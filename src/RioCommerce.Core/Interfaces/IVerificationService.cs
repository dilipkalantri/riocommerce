using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Issues and checks one-time registration codes (OTP). Used by both customer signup and
/// franchisee applications. Sending and verifying are deliberately separate so the same
/// service serves any flow that needs "prove you own this email/phone first".
/// </summary>
public interface IVerificationService
{
    /// <summary>
    /// Generate a 6-digit code, store it hashed, and deliver it on the chosen channel.
    /// Replaces (consumes) any prior un-consumed code for the same (purpose, target).
    /// </summary>
    Task<VerificationSendResult> SendAsync(
        VerificationPurpose purpose,
        VerificationChannel channel,
        string target,
        Guid? subjectId,
        string recipientName,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a SINGLE 6-digit code to BOTH email and SMS at once. The same code satisfies either
    /// channel — verifying against the email or the phone both work. Used by registration where we
    /// want the customer to receive the code on every contact they gave. Succeeds if at least one
    /// channel delivers; reports which channels failed.
    /// </summary>
    Task<VerificationSendResult> SendBothAsync(
        VerificationPurpose purpose,
        string? email,
        string? phone,
        Guid? subjectId,
        string recipientName,
        CancellationToken ct = default);

    /// <summary>
    /// Check a submitted code for (purpose, target). On success marks the code consumed and
    /// returns the stored SubjectId so the caller can activate the right user/franchise.
    /// </summary>
    Task<VerificationCheckResult> VerifyAsync(
        VerificationPurpose purpose,
        string target,
        string code,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a code WITHOUT consuming it (does not increment attempts or mark used). Used by
    /// the forgot-password flow to surface an "incorrect OTP" error at the Verify step before the
    /// user reaches the new-password screen. The real consume happens later in VerifyAsync.
    /// </summary>
    Task<VerificationCheckResult> PeekAsync(
        VerificationPurpose purpose,
        string target,
        string code,
        CancellationToken ct = default);
}

public class VerificationSendResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>When the issued code expires — surfaced to the UI for a countdown.</summary>
    public DateTime? ExpiresAt { get; init; }

    public static VerificationSendResult Ok(DateTime expiresAt) => new() { Success = true, ExpiresAt = expiresAt };
    public static VerificationSendResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public class VerificationCheckResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    /// <summary>The User.Id / Franchise.Id this code gated — set only on success.</summary>
    public Guid? SubjectId { get; init; }

    public static VerificationCheckResult Ok(Guid? subjectId) => new() { Success = true, SubjectId = subjectId };
    public static VerificationCheckResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}
