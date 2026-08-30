namespace RioCommerce.Infrastructure.Services.SerialKeys.Providers;

/// <summary>
/// Minimal provider-agnostic view over any provider's <c>ConfigJson</c> that only reads the
/// mode whitelist. Every provider config that wants mode-filtering includes an <c>EnabledModeIds</c>
/// array of <c>ProductMode.Id</c> Guids (RioPlay and Valence both do); the orchestrator deserialises
/// into THIS shape to decide whether to skip an order item, so the filter works uniformly across
/// providers without per-vendor branching.
///
/// <para>Keying on the ProductMode Guid (not the <c>LectureMode</c> enum) lets two product modes that
/// share an enum value — e.g. "Recorded + Softcopy" and "Recorded + Hardcopy", both LivePlusRecorded —
/// be whitelisted independently.</para>
/// </summary>
public sealed class ModeFilterConfig
{
    public List<System.Guid>? EnabledModeIds { get; set; }
}
