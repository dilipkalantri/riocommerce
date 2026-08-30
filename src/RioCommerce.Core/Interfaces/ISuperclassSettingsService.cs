using RioCommerce.Core.DTOs.SerialKeys;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Thin CRUD over the singleton <see cref="RioCommerce.Core.Entities.SuperclassSettings"/> row with the
/// Data-Protection encrypt/decrypt of the API key hidden inside. Callers never touch the encrypted
/// column directly. Mirrors <see cref="IRioPlayTenantService"/>'s secret-handling contract.
/// </summary>
public interface ISuperclassSettingsService
{
    /// <summary>Returns the current settings for the admin edit form, or a blank model with sensible
    /// defaults (Base URL, Class ID 318) if none saved yet. The API key is intentionally NOT returned.</summary>
    Task<SuperclassSettingsEdit> GetAsync(CancellationToken ct = default);

    /// <summary>Upserts the singleton settings row. The API key is encrypted; a blank key on edit
    /// keeps the existing one.</summary>
    Task<(bool ok, string? error)> SaveAsync(SuperclassSettingsEdit model, CancellationToken ct = default);

    /// <summary>Resolves the active settings with the decrypted API key, ready for a provider call.
    /// Returns null when no active row / no key is configured. Not persisted, not logged.</summary>
    Task<SuperclassCredentials?> ResolveAsync(CancellationToken ct = default);

    /// <summary>Diagnostic: fire a minimal request at the configured endpoint and return a
    /// human-readable round-trip summary (status, response body). Never throws.</summary>
    Task<SuperclassConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>Decrypted Superclass credential + config bundle handed to the provider for a call.
/// Not persisted, not logged.</summary>
public record SuperclassCredentials(
    string ApiBaseUrl,
    string ApiKey,
    int DefaultClassId,
    string Environment,
    bool LoggingEnabled,
    int MaxRetryAttempts);

/// <summary>Result of the admin "Test connection" button.</summary>
public class SuperclassConnectionTestResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? Error { get; set; }
}
