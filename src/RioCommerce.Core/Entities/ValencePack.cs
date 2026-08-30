namespace RioCommerce.Core.Entities;

/// <summary>
/// A Valence (Edubees) "pack" — a purchasable course/bundle exposed by the LMS's
/// <c>get_packs</c> endpoint. Synced into this local table (manual refresh from the admin
/// serial-key settings page) so the product config screen can offer a dropdown of packs
/// instead of asking an admin to remember numeric ids.
///
/// <para>The Valence pack id is an integer and is what gets sent as the <c>course</c> field
/// on <c>register_student_with_course</c>. We keep it as the natural key in
/// <see cref="ExternalId"/> (the BaseEntity <see cref="BaseEntity.Id"/> remains our own Guid PK
/// so the table follows house conventions and FKs stay uniform).</para>
/// </summary>
public class ValencePack : BaseEntity
{
    /// <summary>Valence's own integer pack id (from <c>get_packs</c> → <c>data[].id</c>).
    /// Unique; used to upsert on re-sync and sent as the <c>course</c> value at registration.</summary>
    public int ExternalId { get; set; }

    /// <summary>Display name (<c>data[].pack_name</c>).</summary>
    public string PackName { get; set; } = string.Empty;

    /// <summary>Optional comma/space-separated tags (<c>data[].tags</c>) — shown as a hint in the picker.</summary>
    public string? Tags { get; set; }

    /// <summary>Set false when a pack disappears from a later sync, so stale packs can be hidden
    /// without losing history. Re-appearing packs are reactivated.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Timestamp of the last sync that saw this pack.</summary>
    public DateTime LastSyncedAt { get; set; }
}
