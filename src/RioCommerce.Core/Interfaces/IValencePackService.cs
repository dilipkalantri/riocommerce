using RioCommerce.Core.DTOs.SerialKeys;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Manages the local cache of Valence (Edubees) packs. Packs are pulled from Valence's
/// <c>get_packs</c> endpoint on an admin-triggered manual refresh and upserted into the
/// <c>valence_packs</c> table; the product serial-key config screen lists them so an admin
/// picks a pack rather than typing a numeric course id.
/// </summary>
public interface IValencePackService
{
    /// <summary>All packs, active first, newest sync first. Used by the settings page and the
    /// product config picker.</summary>
    Task<List<ValencePackItem>> ListAsync(bool activeOnly = true, CancellationToken ct = default);

    /// <summary>
    /// Pull packs from Valence and upsert into the local table. Requires the Valence base URL +
    /// path segment — taken from the supplied config, or (when null) from any active Valence
    /// product config already saved. Packs no longer returned are marked inactive, not deleted.
    /// </summary>
    Task<ValencePackSyncResult> SyncAsync(string? baseUrl, string? pathSegment, CancellationToken ct = default);

    /// <summary>
    /// Registers (maps) one of our products to a Valence pack via <c>save_product_pack</c>. This is
    /// a prerequisite for key generation — Valence rejects <c>register_student_with_course</c> with
    /// "No pack found for this course" until the product↔pack mapping exists on its side. Sends our
    /// product id as <c>course_id</c> and the pack's numeric id as <c>pack_id</c>. Treats Valence's
    /// "This combination already exists" as success (idempotent).
    /// </summary>
    /// <summary>
    /// Registers <c>course_id → pack_id</c> on Valence (<c>save_product_pack</c>).
    ///
    /// <para><paramref name="courseId"/> is a STRING, not the raw product id: a combo gives each of
    /// its packs the course of the product that OWNS that pack, so each one can issue its own key.
    /// Whatever is passed here must be byte-identical to the <c>course</c> the registration call
    /// later sends, or Valence will not find the mapping.</para>
    /// </summary>
    Task<(bool ok, string? error)> MapProductToPackAsync(
        string baseUrl, string pathSegment, string courseId, string productName, int packExternalId,
        CancellationToken ct = default);
}
