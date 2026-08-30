namespace RioCommerce.Core.Entities;
public class FranchiseLedgerEntry : BaseEntity
{
    public Guid FranchiseId { get; set; }
    public bool IsCredit { get; set; }            // true = top-up, false = debit (order)
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? Description { get; set; }
    public Guid? OrderId { get; set; }
    public Franchise Franchise { get; set; } = null!;
}
