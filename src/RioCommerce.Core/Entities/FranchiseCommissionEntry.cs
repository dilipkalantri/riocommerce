using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// Earned-commission ledger: one row per order item that produced commission for a franchise.
// Captures the rule snapshot (Type+Value) at the moment of the order so historical totals are stable.
public class FranchiseCommissionEntry : BaseEntity
{
    public Guid FranchiseId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    /// <summary>The pre-GST (taxable) value the commission was computed against. Gross-inclusive
    /// on rows written before the commission-GST split; the reverse-calculated base after it.</summary>
    public decimal BaseAmount { get; set; }
    public CommissionType Type { get; set; }
    public decimal Value { get; set; }
    /// <summary>The bare commission — taxable value of the franchisee's supply, excluding any GST
    /// charged on it. Historical earnings totals sum this column, so it stays the earning figure.</summary>
    public decimal CommissionAmount { get; set; }
    /// <summary>GST on the commission; zero for franchisees without a GSTIN.</summary>
    public decimal GstOnCommission { get; set; }
    /// <summary>What the franchisee is actually paid = <see cref="CommissionAmount"/> + <see cref="GstOnCommission"/>.</summary>
    public decimal TotalPayout { get; set; }
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

    public Franchise Franchise { get; set; } = null!;
}
