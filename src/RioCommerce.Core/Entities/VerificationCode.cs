using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// A one-time 6-digit verification code (OTP) sent during registration. Serves both customer
/// signup and franchisee application — <see cref="Purpose"/> distinguishes them so a verified
/// code routes to the right completion step.
///
/// <para>The code itself is stored hashed (never in clear) so a DB leak can't reveal live OTPs.
/// Verification re-hashes the submitted code and compares. Codes expire (<see cref="ExpiresAt"/>)
/// and lock out after too many wrong attempts (<see cref="AttemptCount"/>).</para>
/// </summary>
public class VerificationCode : BaseEntity
{
    /// <summary>What this code unlocks — customer signup vs franchisee application.</summary>
    public VerificationPurpose Purpose { get; set; }

    /// <summary>Channel the code was sent on.</summary>
    public VerificationChannel Channel { get; set; }

    /// <summary>Normalised destination: lowercased email or digits-only phone. The verify call
    /// matches on (Purpose, Target) so the same person can't be hijacked across purposes.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>BCrypt hash of the 6-digit code.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>The entity this verification gates: the User.Id (customer) or Franchise.Id
    /// (franchisee). Lets the verify step locate and activate the right record.</summary>
    public Guid? SubjectId { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Wrong-code attempts. Locked once it crosses the service's max.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Set when successfully verified — a code is single-use.</summary>
    public DateTime? ConsumedAt { get; set; }

    public bool IsConsumed => ConsumedAt.HasValue;
}
