using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Franchise;

// Public registration form payload — no auth required.
public class FranchiseRegistrationRequest
{
    public string FranchiseeName { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string ContactPhone { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? AddressLine { get; set; }
    public string State { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? PinCode { get; set; }
    public string? DocumentUrls { get; set; }
    /// <summary>Chosen verification channel for the OTP: "email" (default) or "sms".</summary>
    public string? VerifyChannel { get; set; } = "email";
}

public class FranchiseVerifyRequest
{
    public string Target { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

// Compact row for the admin "Franchises" list, filterable by Status.
public class FranchiseApplicationRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public FranchiseStatus Status { get; set; }
    public bool IsActive { get; set; }
    public DateTime RegisteredAt { get; set; }
    public decimal WalletBalance { get; set; }
}

// Full detail for the admin franchise page (also used by approve/reject screen).
public class FranchiseAdminDetail
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string Code { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? PinCode { get; set; }
    public string? AddressLine { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? DocumentUrls { get; set; }
    public FranchiseStatus Status { get; set; }
    public bool IsActive { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalRemarks { get; set; }
    public string? RejectionRemarks { get; set; }
    public decimal WalletBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public Guid? AdminUserId { get; set; }
}

public class FranchiseApprovalRequest
{
    public Guid FranchiseId { get; set; }
    public string? Code { get; set; }              // Admin-assigned short code (e.g. PUN, MUM); auto-derived if blank.
    /// <summary>
    /// Defaults to ZERO — a new franchisee gets no credit line unless an admin deliberately grants
    /// one on the approval form. This used to default to ₹50,000, which meant every approval handed
    /// out a credit line by accident rather than by decision.
    /// </summary>
    public decimal CreditLimit { get; set; } = 0m;
    public decimal? OpeningBalance { get; set; }    // Optional initial wallet credit.
    public string? Remarks { get; set; }

    /// <summary>How the admin resolved an existing-account conflict. Left at <c>None</c> the approval
    /// refuses to run when a conflict exists, so a clash can never be resolved by accident — the
    /// screen has to surface it and the admin has to pick.</summary>
    public FranchiseAccountResolution AccountResolution { get; set; } = FranchiseAccountResolution.None;

    /// <summary>The existing user the admin chose to reuse. Required for both reuse resolutions, and
    /// checked against the conflict actually found so a stale screen cannot point at the wrong user.</summary>
    public Guid? ExistingUserId { get; set; }

    /// <summary>Explicit tick on the destructive path. Without it, ReplaceExistingContact is refused.</summary>
    public bool ConfirmContactReplacement { get; set; }

    /// <summary>Which contact fields the admin actually ticked to overwrite. Only these are changed —
    /// never "all contact details" as a block.</summary>
    public bool ReplaceEmail { get; set; }
    public bool ReplacePhone { get; set; }
}

public class FranchiseRejectionRequest
{
    public Guid FranchiseId { get; set; }
    public string Remarks { get; set; } = string.Empty;
}

// ─── Approval conflict detection ────────────────────────────────────────────
// Approving a franchise provisions a login, and users are unique on Email and, separately, on Phone.
// When the applicant already has an account — very common, because franchisees usually bought
// something as a student first — the approval must stop and let the admin decide, never guess. These
// types carry everything the warning modal needs so the decision is made with the facts visible.

/// <summary>What kind of clash was found, if any. Drives which modal the approval screen shows.</summary>
public enum FranchiseApprovalConflictKind
{
    /// <summary>No existing account or franchise stands in the way — approval can proceed.</summary>
    None = 0,

    /// <summary>Exactly one existing user holds the phone and/or the email. Resolvable by the admin.</summary>
    ExistingAccount = 1,

    /// <summary>The phone belongs to one user and the email to a DIFFERENT one. Higher risk: choosing
    /// either would silently pick a winner, so automatic approval is blocked entirely.</summary>
    MultipleAccounts = 2,

    /// <summary>Another franchise already uses this phone/email. Approving would create a duplicate.</summary>
    DuplicateFranchise = 3,
}

/// <summary>An existing account the admin is being asked to reuse — shown as-is in the modal so the
/// decision is made against real data, not a summary.</summary>
public class ExistingAccountInfo
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Every active role, so "Student" vs "Franchise Admin" is obvious before reusing it.</summary>
    public List<string> Roles { get; set; } = new();
    /// <summary>Friendly single-word account type derived from <see cref="Roles"/>.</summary>
    public string AccountType { get; set; } = "Other";
    /// <summary>True when this user matched on phone; both can be true for the same user.</summary>
    public bool MatchedOnPhone { get; set; }
    public bool MatchedOnEmail { get; set; }
}

/// <summary>An existing franchise already holding this contact information.</summary>
public class ExistingFranchiseInfo
{
    public Guid FranchiseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>Pending ones can simply be approved instead — no duplicate needed.</summary>
    public bool IsPending { get; set; }
}

/// <summary>Result of the pre-approval check. Purely a read — nothing is written to decide this.</summary>
public class FranchiseApprovalConflict
{
    public FranchiseApprovalConflictKind Kind { get; set; } = FranchiseApprovalConflictKind.None;
    public bool HasConflict => Kind != FranchiseApprovalConflictKind.None;
    /// <summary>True when no admin choice can unblock this — the data itself must change first.</summary>
    public bool BlocksApproval => Kind is FranchiseApprovalConflictKind.MultipleAccounts
                                        or FranchiseApprovalConflictKind.DuplicateFranchise;

    /// <summary>The user holding the phone (null when free).</summary>
    public ExistingAccountInfo? PhoneOwner { get; set; }
    /// <summary>The user holding the email (null when free). Same object as
    /// <see cref="PhoneOwner"/> when one user holds both.</summary>
    public ExistingAccountInfo? EmailOwner { get; set; }
    /// <summary>Set only for <see cref="FranchiseApprovalConflictKind.DuplicateFranchise"/>.</summary>
    public ExistingFranchiseInfo? DuplicateFranchise { get; set; }

    /// <summary>The franchise being approved, echoed for the side-by-side comparison.</summary>
    public string FranchiseName { get; set; } = string.Empty;
    public string? FranchiseEmail { get; set; }
    public string? FranchisePhone { get; set; }

    public string? Message { get; set; }
}

/// <summary>How the admin chose to resolve an <see cref="FranchiseApprovalConflictKind.ExistingAccount"/>.</summary>
public enum FranchiseAccountResolution
{
    /// <summary>No conflict expected. Approval fails if one is found — the admin must see it first.</summary>
    None = 0,

    /// <summary>Reuse the existing user: add franchise_admin, link the franchise. Password, email and
    /// phone are left exactly as they are, so the person's existing login keeps working.</summary>
    UseExistingAccount = 1,

    /// <summary>Reuse the existing user AND overwrite the contact fields the admin ticked. Destructive
    /// to identity, so it requires <see cref="ConfirmContactReplacement"/>.</summary>
    ReplaceExistingContact = 2,
}

/// <summary>
/// The franchise's own particulars, editable by an admin after approval.
///
/// <para>Deliberately narrow. It carries no money and no lifecycle: wallet balance, credit limit,
/// commissions, status, active flag, the short Code and the login email are all absent, so this
/// screen cannot alter what a franchisee owes, earns, or signs in with. Those each have their own
/// flow — changing the login email in particular has to go through the account-conflict checks on
/// approval, not a profile form.</para>
/// </summary>
public class FranchiseProfileEdit
{
    public Guid FranchiseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public string? AddressLine { get; set; }
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? PinCode { get; set; }
    /// <summary>15-character GSTIN. Blank clears it — a franchisee who deregisters raises a bill of
    /// supply instead of a tax invoice, so being able to remove it matters as much as adding it.</summary>
    public string? Gstin { get; set; }
    /// <summary>10-character PAN.</summary>
    public string? Pan { get; set; }
}
