namespace RioCommerce.Core.Enums;

/// <summary>
/// Lifecycle of a single <see cref="RioCommerce.Core.Entities.SerialKeyRecord"/>.
/// </summary>
public enum SerialKeyStatus
{
    /// <summary>Inserted by the order-paid hook; the retry task hasn't picked it up yet.</summary>
    Pending = 0,
    /// <summary>Provider HTTP call in flight (set briefly during dispatch, cleared on result).</summary>
    Generating = 1,
    /// <summary>Provider returned a key. For auto-activate products this transitions to <see cref="Activated"/> in the same call.</summary>
    Generated = 2,
    /// <summary>Customer / admin explicitly called the provider's activation endpoint and it succeeded.</summary>
    Activated = 3,
    /// <summary>Last attempt failed; will be retried by <c>SerialKeyRetryTask</c> until <c>AttemptCount</c> hits the cap.</summary>
    Failed = 4,
    /// <summary>Hit the retry cap. Surfaces as an <c>AdminNotification</c>; only "Regenerate" from the admin UI revives it.</summary>
    DeadLettered = 5,
    /// <summary>Manually invalidated by an admin (refund, abuse, etc.).</summary>
    Revoked = 6,
}
