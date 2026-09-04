using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

public class SchoolUser : BaseEntity
{
    public Guid SchoolId { get; set; }
    public Guid UserId { get; set; }
    public SchoolUserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    // Coordinator scope. Both are free-text so a school can say "8", "8-10", "All",
    // "English", "Marathi", etc., without a lookup table. Left null on Principal rows.
    public string? Standard { get; set; }
    public string? Medium { get; set; }

    public School School { get; set; } = null!;
    public User User { get; set; } = null!;
}
