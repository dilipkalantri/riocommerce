namespace RioCommerce.Core.DTOs.Customers;

/// <summary>
/// Everything needed to resolve — or, failing that, create — the customer account behind an order
/// captured by staff or by a franchisee, where the student never logs in to place it themselves.
/// </summary>
/// <param name="FullName">The student's name, as captured on the order.</param>
/// <param name="Phone">The student's mobile. This is the IDENTITY key: the only field matched on.</param>
/// <param name="Email">Optional. Recorded on a NEW account when free, never matched on — families
/// share an address, so an email hit says nothing about who the person is.</param>
/// <param name="City">Optional, copied to a new account only.</param>
/// <param name="State">Optional, copied to a new account only.</param>
/// <param name="SourceNote">Human-readable provenance stamped into <c>User.AdminComment</c> on a
/// newly created account, e.g. "Created from Franchise Order FRN-1021".</param>
public record StudentAccountRequest(
    string FullName,
    string Phone,
    string? Email = null,
    string? City = null,
    string? State = null,
    string? SourceNote = null);

/// <summary>Outcome of <see cref="Core.Interfaces.IStudentAccountProvisioner.ResolveOrCreateAsync"/>.</summary>
/// <param name="UserId">The account the order should point at, or null when none could be resolved
/// (bad/missing phone, or provisioning failed — the caller carries on without a link).</param>
/// <param name="Outcome">Which branch was taken.</param>
/// <param name="Note">Diagnostic detail for logs — never shown to a franchisee or a student.</param>
public record StudentAccountResult(Guid? UserId, StudentAccountOutcome Outcome, string? Note = null)
{
    public bool Linked => UserId.HasValue;
    public static StudentAccountResult Skipped(string note) => new(null, StudentAccountOutcome.Skipped, note);
    public static StudentAccountResult Failed(string note) => new(null, StudentAccountOutcome.Failed, note);
}

public enum StudentAccountOutcome
{
    /// <summary>An account with this phone already existed and was linked, untouched.</summary>
    LinkedExisting = 0,
    /// <summary>No account had this phone, so a new student account was created and linked.</summary>
    CreatedNew = 1,
    /// <summary>Nothing usable to work with (no/short phone). No account, no error.</summary>
    Skipped = 2,
    /// <summary>Provisioning was attempted and failed. The caller must still complete its own work.</summary>
    Failed = 3
}

/// <summary>The read-only answer to "who owns this phone?".</summary>
/// <param name="PhoneUsable">False when there is no identity in the number at all (blank, too short).
/// Such a record can only be resolved by a human.</param>
/// <param name="UserId">The account holding that phone, or null when it is free.</param>
/// <param name="CanonicalPhone">The trimmed number the match was made on; null when unusable.</param>
public record StudentPhoneMatch(bool PhoneUsable, Guid? UserId, string? CanonicalPhone);

/// <summary>One row of the READ-ONLY backfill report: what <i>would</i> happen to an existing order
/// that carries student details but no <c>Order.UserId</c>. Produces no writes of any kind.</summary>
public record StudentLinkDryRunRow(
    Guid OrderId,
    string OrderNumber,
    string StudentName,
    string StudentPhone,
    string? StudentEmail,
    Guid? MatchedUserId,
    string? MatchedUserName,
    string? MatchedUserPhone,
    StudentLinkAction Action,
    string Reason);

public enum StudentLinkAction
{
    /// <summary>An account already exists for this phone — the order would simply point at it.</summary>
    LinkExisting = 0,
    /// <summary>No account exists for this phone — one would be created, then linked.</summary>
    CreateNew = 1,
    /// <summary>Nothing to do (already linked).</summary>
    NoAction = 2,
    /// <summary>A human has to look: unusable phone, or an ambiguous/near-miss match.</summary>
    ReviewRequired = 3
}

/// <summary>The whole dry run: every candidate row plus the totals to sanity-check against.</summary>
public record StudentLinkDryRunReport(
    int OrdersScanned,
    int DistinctStudents,
    int StudentsWithExistingAccount,
    int StudentsNeedingNewAccount,
    int OrdersAlreadyLinked,
    int OrdersNeedingReview,
    IReadOnlyList<StudentLinkDryRunRow> Rows,
    IReadOnlyList<StudentLinkDuplicateGroup> DuplicateStudents);

/// <summary>A student who appears on more than one order — the case that must resolve to ONE account.</summary>
public record StudentLinkDuplicateGroup(string Phone, string Names, IReadOnlyList<string> OrderNumbers);

// ── Backfill (dry run and live share one shape, so the two can be compared line by line) ──────────

/// <summary>What the backfill did — or, in dry run, would do — to one order.</summary>
/// <param name="OrderId">The order row.</param>
/// <param name="OrderNumber">Its human-facing number.</param>
/// <param name="StudentName">Student name as captured on the order.</param>
/// <param name="StudentPhone">Student phone as captured on the order — the identity key.</param>
/// <param name="StudentEmail">Student email as captured on the order. Never a match key.</param>
/// <param name="UserId">The account the order is (or would be) linked to; null when unresolved.</param>
/// <param name="Action">What happened / would happen.</param>
/// <param name="Reason">Why, in words a reviewer can check.</param>
/// <param name="ProposedAdminComment">For CreateNew only: the provenance line the new account carries.</param>
/// <param name="Error">Set when this order failed. Its neighbours are unaffected.</param>
public record StudentLinkBackfillRow(
    Guid OrderId,
    string OrderNumber,
    string StudentName,
    string StudentPhone,
    string? StudentEmail,
    Guid? UserId,
    StudentLinkAction Action,
    string Reason,
    string? ProposedAdminComment = null,
    string? Error = null);

/// <summary>
/// The result of one backfill pass. <paramref name="DryRun"/> is the flag the pass ran under, echoed
/// back so a report can never be mistaken for the other mode.
/// </summary>
/// <param name="LinkExisting">ORDERS pointed at an account that already existed before this pass.</param>
/// <param name="CreateNew">DISTINCT accounts created (or that would be) — students, not orders. An
/// order that triggers a creation is counted here exactly once.</param>
/// <param name="LinkedToSameStudent">ORDERS pointed at an account created EARLIER IN THIS SAME PASS —
/// a student's second and third orders. Tracked apart from <paramref name="LinkExisting"/> because it
/// is the number that proves the duplicate-student rule held: these orders created nothing.</param>
/// <param name="UsersCreated">Same figure as <paramref name="CreateNew"/>, named for the totals block.</param>
/// <param name="OrdersLinked">Orders whose UserId was (or would be) set — every action combined.</param>
public record StudentLinkBackfillReport(
    bool DryRun,
    int OrdersScanned,
    int LinkExisting,
    int CreateNew,
    int LinkedToSameStudent,
    int NoAction,
    int ReviewRequired,
    int Failed,
    int UsersCreated,
    int OrdersLinked,
    IReadOnlyList<StudentLinkBackfillRow> Rows);
