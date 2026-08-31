namespace RioCommerce.Core.Entities;

/// <summary>
/// A student's membership of a school.
///
/// Deliberately separate from <see cref="SchoolUser"/>: that table models school STAFF
/// (Principal / Coordinator). Keeping students out of it is what guarantees an enrolled
/// student can never be mistaken for — or promoted to — a school principal.
///
/// The linked <see cref="User"/> always carries the "student" role; membership here grants
/// no school-management rights of any kind.
/// </summary>
public class SchoolStudent : BaseEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>The student's own user account (role = "student").</summary>
    public Guid UserId { get; set; }

    public string? StudentClass { get; set; }
    public string? Section { get; set; }
    public string? RollNumber { get; set; }
    public Guid? AcademicYearId { get; set; }
    public bool IsActive { get; set; } = true;

    public School School { get; set; } = null!;
    public User User { get; set; } = null!;
}
