namespace RioCommerce.Core.Entities;

/// <summary>
/// Global (institute-wide) configuration for the Superclass LMS registration integration. Unlike
/// RioPlay (per-tenant) and Valence (per-product), Superclass has a single set of credentials and
/// defaults used for every request, so this is a SINGLETON: exactly one active row.
///
/// The API key is stored Data-Protection-encrypted at rest via <c>SuperclassSettingsService</c> —
/// never read <see cref="ApiKeyEncrypted"/> directly and never return it to the UI or logs.
/// </summary>
public class SuperclassSettings : BaseEntity
{
    /// <summary>Base URL of the Superclass API, no trailing slash, e.g. <c>https://lms.superclassapp.in</c>.
    /// The provider appends <c>/api/register</c>.</summary>
    public string ApiBaseUrl { get; set; } = "https://lms.superclassapp.in";

    /// <summary>Data-Protection-encrypted API authentication key. Sent as a Bearer token
    /// (and X-API-Key) header. Use <c>ISuperclassSettingsService.ResolveAsync</c> to read the plaintext.</summary>
    public string ApiKeyEncrypted { get; set; } = string.Empty;

    /// <summary>Superclass Class ID sent as <c>class_id</c> on every request. Institute default: 318.
    /// A product may override this in its own config.</summary>
    public int DefaultClassId { get; set; } = 318;

    /// <summary>Deployment environment label — "Sandbox" or "Production". Informational routing hint;
    /// the actual endpoint is <see cref="ApiBaseUrl"/>.</summary>
    public string Environment { get; set; } = "Production";

    /// <summary>When true, the Superclass HTTP logging handler records full (credential-redacted)
    /// request/response bodies to app_logs for diagnostics. The per-record audit payloads are always
    /// stored regardless of this flag.</summary>
    public bool LoggingEnabled { get; set; } = true;

    /// <summary>Maximum dispatch attempts before a Superclass record is dead-lettered. Caps the shared
    /// exponential-backoff retry loop for this provider.</summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>Only the active row is used at call time. Kept as a flag (rather than deleting) so
    /// credentials survive a temporary disable.</summary>
    public bool IsActive { get; set; } = true;
}
