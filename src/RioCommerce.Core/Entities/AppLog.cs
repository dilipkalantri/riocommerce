using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

/// <summary>
/// One row per logged event. Modeled on nopCommerce's <c>Log</c> table — small enough to
/// stay fast, rich enough to answer "what happened to order X" or "show me yesterday's errors".
///
/// Rows are inserted from anywhere a feature wants to leave a breadcrumb. Reads go through
/// the admin /admin/logs page or direct SQL during incident response. A future retention
/// task can purge rows older than N days; we don't auto-purge today so nothing is silently lost.
/// </summary>
public class AppLog : BaseEntity
{
    /// <summary>Severity (Information / Warning / Error / Critical / etc).</summary>
    public AppLogLevel Level { get; set; }

    /// <summary>Logical area that produced the event — "SerialKey", "Checkout", "Auth", "Payment".
    /// Free-form but should be drawn from a small known set for filterability.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional short machine-readable code — "serial_key.enqueued", "rio.signup.failed".
    /// Lets the admin UI group by event class without parsing the message text.</summary>
    public string? EventCode { get; set; }

    /// <summary>Human-readable single-line summary — what shows in the list view.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Full exception details including stack trace, if applicable.</summary>
    public string? Exception { get; set; }

    /// <summary>Arbitrary structured context as JSON — request payload, response, IDs, etc.
    /// Stored as jsonb so admins can query specific fields in PostgreSQL when needed.</summary>
    public string? Properties { get; set; }

    /// <summary>Who triggered the event, if known (admin actor / logged-in customer).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Order that the event relates to, if any. Enables fast "logs for order X" filtering.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Generic foreign-key tag (e.g. "Product", "Tenant") + value when not tied to an order.</summary>
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>HTTP path that produced the event, if it was a web request.</summary>
    public string? RequestPath { get; set; }

    /// <summary>Server hostname / instance identifier — useful once there's more than one running.</summary>
    public string? Source { get; set; }
}
