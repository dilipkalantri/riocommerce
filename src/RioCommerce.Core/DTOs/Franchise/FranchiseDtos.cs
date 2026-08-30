using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Franchise;

public record FranchiseCard(
    Guid Id, string Name, string Code, string City, string? ContactPerson, string? ContactPhone,
    int OrdersMtd, decimal Revenue, int Pending, decimal Wallet, decimal CreditLimit);

public record FranchiseStats(int OrdersMtd, decimal Revenue, int ActiveFranchises, int PendingApproval);

/// <summary>Admin wallet view for one franchise: the live balance plus the ledger behind it — every
/// admin top-up / manual credit, every order deduction, every recharge. <see cref="TotalCredited"/>
/// and <see cref="TotalDebited"/> cover the whole filtered window, not just the rows returned in
/// <see cref="Entries"/>, so they stay meaningful when the list is capped.</summary>
public record FranchiseWalletStatement(
    Guid FranchiseId, string Name, string Code, string? BusinessName,
    decimal WalletBalance, decimal CreditLimit, decimal Available,
    decimal TotalCredited, decimal TotalDebited,
    int EntryCount, IReadOnlyList<FranchiseLedgerItem> Entries)
{
    /// <summary>True when the ledger was capped and older rows aren't in <see cref="Entries"/> —
    /// the CSV statement is the way to get the rest.</summary>
    public bool IsTruncated => EntryCount > Entries.Count;
}

public class FranchiseOrderRow
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string FranchiseName { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string StudentPhone { get; set; } = string.Empty;
    public string ProductSummary { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    /// <summary>Paid out of the franchise wallet, so no tax invoice exists for it — the wallet
    /// top-up that funded it carries one instead. Only a receipt is offered for these.</summary>
    public bool PaidFromWallet { get; set; }
}
