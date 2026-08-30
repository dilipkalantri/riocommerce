using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Admin;

// ─────────────── Subjects ───────────────
// The list the Subject dropdown on the product editor is built from. Until this screen existed the
// eight seeded rows could only be changed with hand-written SQL against the database.

/// <param name="UsedByProductCount">
/// How many courses point at this subject. Drives the delete guard — Product.SubjectId is a foreign
/// key, so a subject in use cannot simply be removed.
/// </param>
public record SubjectAdminItem(
    Guid Id, string Name, string Slug, CourseLevel? Level,
    int DisplayOrder, bool IsActive, int UsedByProductCount);

public class SubjectEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional — derived from the name when left blank. Internal only: subjects are a
    /// query-string filter (<c>?subject={id}</c>), never a public URL, so this is not registered
    /// with the SEO slug registry the way a category slug is.</summary>
    public string? Slug { get; set; }
    public CourseLevel? Level { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
