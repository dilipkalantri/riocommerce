using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Franchise : BaseEntity
{
    public string Name { get; set; } = string.Empty;            // Franchisee owner name
    public string Code { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? BusinessName { get; set; }                    // Business / organization name
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }                    // Login email assigned to the linked user
    public string? Gstin { get; set; }                           // Optional, 15 chars
    public string? Pan { get; set; }                             // Optional, 10 chars
    public string? AddressLine { get; set; }
    public string? State { get; set; }
    public string? PinCode { get; set; }
    public string? DocumentUrls { get; set; }                    // Comma-separated URLs (optional supporting docs)

    public decimal WalletBalance { get; set; }
    public decimal CreditLimit { get; set; }                     // wallet may go negative up to -CreditLimit

    // Lifecycle: Pending (just registered) → Approved (live, login provisioned) | Rejected.
    // IsActive is the post-approval suspend/resume toggle, independent of Status.
    public FranchiseStatus Status { get; set; } = FranchiseStatus.Approved;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedById { get; set; }
    public string? ApprovalRemarks { get; set; }
    public string? RejectionRemarks { get; set; }

    public Guid? AdminUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public User? AdminUser { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
