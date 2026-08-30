using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// Franchisee-initiated wallet top-up tracked through the payment-gateway round-trip.
// Pending on Initiate → Success (credits the wallet + writes a FranchiseLedgerEntry) on Confirm.
public class WalletRecharge : BaseEntity
{
    public Guid FranchiseId { get; set; }
    public decimal Amount { get; set; }
    public WalletRechargeStatus Status { get; set; } = WalletRechargeStatus.Pending;

    public string GatewayName { get; set; } = string.Empty;
    public string GatewayOrderId { get; set; } = string.Empty;
    public string? GatewayPaymentId { get; set; }
    public string? GatewaySignature { get; set; }

    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public Guid? LedgerEntryId { get; set; }     // links to the FranchiseLedgerEntry credit row on success

    public Franchise Franchise { get; set; } = null!;
}
