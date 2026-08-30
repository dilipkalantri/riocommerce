namespace RioCommerce.Core.Entities;
public class UserRole : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? FranchiseId { get; set; }
    public bool IsActive { get; set; } = true;
    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public Franchise? Franchise { get; set; }
}
