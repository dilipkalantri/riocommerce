using RioCommerce.Core.DTOs.SerialKeys;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// One implementation per external serial-key vendor (RioPlay, Valence, …). Adding a 4th
/// provider is: implement this interface + register the typed HttpClient + register
/// the instance as <c>ISerialKeyProvider</c> in DI. Nothing else changes.
///
/// <para>
/// Implementations MUST swallow all HTTP / parsing exceptions and return a non-success
/// <see cref="GenerateKeyResult"/> instead. Throwing breaks the orchestrator's batched
/// processing loop and propagates noise into the scheduled-task runner.
/// </para>
/// </summary>
public interface ISerialKeyProvider
{
    /// <summary>Stable discriminator — matched case-insensitively against
    /// <c>SerialKeyRecord.ProviderKey</c> and <c>ProductSerialKeyConfig.ProviderKey</c>.
    /// Lowercase by convention.</summary>
    string Key { get; }

    /// <summary>Human label for the admin dropdown.</summary>
    string DisplayName { get; }

    /// <summary>True for key-issuing vendors (RioPlay, Valence) whose success MUST carry a serial key.
    /// False for registration-style vendors (Superclass) that provision access directly and return a
    /// student/subscription reference instead of a key. The orchestrator uses this to decide whether a
    /// successful result without a <c>SerialKey</c> is valid (keyless success) and whether to send our
    /// own "your key is ready" customer notification (skipped for keyless providers, which notify the
    /// student themselves). Default-implemented as <c>true</c> so existing providers need no change.</summary>
    bool IssuesSerialKey => true;

    /// <summary>True if this provider exposes the activate-after-create flow that the
    /// product-level <c>AutoActivate=false</c> + explicit <c>/activate</c> route relies on.
    /// Rio = true, Valence = TBD.</summary>
    bool SupportsActivate { get; }

    /// <summary>True if this provider has a status / lookup endpoint we can call.
    /// Rio's PDF doesn't document one; Valence might. When false, <c>GetStatusAsync</c>
    /// returns a NotSupported failure and the route falls back to our local record.</summary>
    bool SupportsStatusLookup { get; }

    Task<GenerateKeyResult> GenerateAsync(GenerateKeyRequest request, CancellationToken ct);

    Task<ActivateKeyResult> ActivateAsync(string serialKey, string? tenantRef, CancellationToken ct);

    Task<KeyStatusResult> GetStatusAsync(string serialKey, string? tenantRef, CancellationToken ct);
}
