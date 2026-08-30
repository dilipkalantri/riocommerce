using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// Per-franchise commission rule for one product. Unique (FranchiseId, ProductId).
// When a franchise order is placed, the rule is resolved to a fixed ₹ or % of the line total
// and recorded as a FranchiseCommissionEntry. Missing rule → product's default franchise share (if enabled).
public class FranchiseCommission : BaseEntity
{
    public Guid FranchiseId { get; set; }
    public Guid ProductId { get; set; }
    public CommissionType Type { get; set; } = CommissionType.Percent;
    public decimal Value { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public Franchise Franchise { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
