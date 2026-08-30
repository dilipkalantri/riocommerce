namespace RioCommerce.Core.Enums;

public enum ApprovalStatus { Pending, Approved, Rejected, ChangesRequested, Escalated, Cancelled }

public enum ApprovalType
{
    RefundApproval,
    ManualPaymentApproval,
    FacultyPayoutApproval,
    FranchisePayoutApproval,
    AffiliatePayoutApproval,
    VendorPayoutApproval,
    EnrollmentOverride,
    DiscountApproval
}
