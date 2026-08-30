using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Enrollment : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? OrderItemId { get; set; }
    public LectureMode? Mode { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal ProgressPct { get; set; }
    public User User { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
