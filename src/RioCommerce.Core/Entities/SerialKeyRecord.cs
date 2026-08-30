using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// One row per order-item that needs an external serial key. Created by the order-paid hook
/// in <c>Pending</c> state with the request payload pre-built; the background
/// <c>SerialKeyRetryTask</c> drives it through Generating → Generated → (optionally) Activated,
/// or Failed → DeadLettered if the provider keeps rejecting it.
///
/// The full request and response bodies are persisted as <c>jsonb</c> for post-mortem debugging —
/// invaluable when the provider returns something odd and we need to reconstruct what we sent.
/// </summary>
public class SerialKeyRecord : BaseEntity
{
    // ── What we're generating a key for ─────────────────────────────────────
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    /// <summary>Nullable because guest checkout creates an order without a UserId.
    /// In that case the provider call uses the order's StudentName / Email / Phone instead.</summary>
    public Guid? UserId { get; set; }

    // ── Provider routing ────────────────────────────────────────────────────
    /// <summary>Matches <see cref="RioCommerce.Core.Interfaces.ISerialKeyProvider.Key"/>.
    /// Lowercase, deliberately a string rather than an enum so new providers don't churn the schema.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Provider-specific tenant reference. For RioPlay this is our <c>RioPlayTenant.Id</c>
    /// (Guid PK) as a string — NOT Rio's numeric TenantId. The provider resolves the row by this Id
    /// and reads Rio's numeric TenantId + secret from there at call time.</summary>
    public string? TenantRef { get; set; }

    // ── Result ──────────────────────────────────────────────────────────────
    /// <summary>The generated key. Unique when set (filtered unique index).</summary>
    public string? SerialKey { get; set; }

    /// <summary>Provider's own reference (Rio's <c>EntityId</c>; Superclass's <c>student_id</c>).
    /// Used by the activate / status routes and shown on the admin detail.</summary>
    public string? ExternalReference { get; set; }

    public SerialKeyStatus Status { get; set; } = SerialKeyStatus.Pending;

    // ── Registration-style outcome (Superclass) — null for key-issuing providers ──────────
    /// <summary>Access/subscription expiry from a registration-style provider. Null for RioPlay/Valence
    /// (whose validity lives inside the generated key on the vendor side).</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Short subscription status text returned/derived by a registration-style provider
    /// (e.g. "active", "already_provisioned"). Null for key-issuing providers.</summary>
    public string? SubscriptionStatus { get; set; }

    /// <summary>Provider-side course/combo reference actually assigned by a registration-style provider
    /// (Superclass course_id, optionally with combo). Null for key-issuing providers.</summary>
    public string? ProviderCourseRef { get; set; }

    // ── Bodies ──────────────────────────────────────────────────────────────
    /// <summary>JSON body sent to the provider. <c>jsonb</c> column for queryability.
    /// Built up-front by the order-paid hook so retries replay exactly the same request.</summary>
    public string RequestPayload { get; set; } = "{}";

    /// <summary>The ACTUAL parameters sent to the provider's API on the most recent attempt, as JSON —
    /// Rio's wrapped <c>{ "Entity": {...} }</c> body, or Valence's form fields (with the secret path
    /// segment masked). Distinct from <see cref="RequestPayload"/> (which is our internal, provider-agnostic
    /// request). Captured for parameter inspection/debugging. <c>jsonb</c>. Null until the first attempt.</summary>
    public string? ApiRequestPayload { get; set; }

    /// <summary>Last response body received (success or error). <c>jsonb</c>.</summary>
    public string? ResponsePayload { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    // ── Retry bookkeeping ───────────────────────────────────────────────────
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    /// <summary>When the retry task should pick this row up next. Set to <c>now</c> on insert.</summary>
    public DateTime NextRetryAt { get; set; } = DateTime.UtcNow;

    public DateTime? GeneratedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    /// <summary>Set the first time the customer "your key is ready" email/SMS is sent. Guards
    /// against duplicate notifications if the record is reprocessed by the retry task.</summary>
    public DateTime? NotifiedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    // ── Navigation ──────────────────────────────────────────────────────────
    public Order? Order { get; set; }
    public OrderItem? OrderItem { get; set; }
    public Product? Product { get; set; }
    public User? User { get; set; }
}
