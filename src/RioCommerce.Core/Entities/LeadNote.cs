namespace RioCommerce.Core.Entities;

/// <summary>
/// A follow-up note on a lead — the conversation history behind the admin CRM view.
///
/// <para>Notes are append-only history: adding a note never mutates earlier ones, and the timeline
/// renders every note ever written. Editing rewrites only that note's <see cref="Body"/> and stamps
/// <c>UpdatedAt</c>, so the UI can show an "edited" marker.</para>
///
/// <para>Field names follow the existing <see cref="OrderNote"/> convention (Body / CreatedByUserId /
/// CreatedByName, with CreatedAt+UpdatedAt inherited from <see cref="BaseEntity"/>) rather than the
/// Note/CreatedOn/UpdatedOn shape, so both note timelines read the same way in code.</para>
/// </summary>
public class LeadNote : BaseEntity
{
    public Guid LeadId { get; set; }

    /// <summary>Free-text note body, e.g. "Called customer, interested in CA Foundation."</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Admin who wrote the note. Null only for system-generated entries.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Display name snapshot, kept stable even if the user is renamed or removed.</summary>
    public string CreatedByName { get; set; } = "system";

    public Lead Lead { get; set; } = null!;
}
