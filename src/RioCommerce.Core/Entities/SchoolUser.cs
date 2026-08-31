using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

public class SchoolUser : BaseEntity
{
    public Guid SchoolId { get; set; }
    public Guid UserId { get; set; }
    public SchoolUserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    public School School { get; set; } = null!;
    public User User { get; set; } = null!;
}
