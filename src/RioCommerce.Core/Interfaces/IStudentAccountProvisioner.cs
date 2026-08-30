using RioCommerce.Core.DTOs.Customers;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Resolves the customer account behind an order that somebody else placed on the student's behalf
/// (franchise portal today; the admin counter can adopt it unchanged).
///
/// <para><b>Phone is the identity.</b> It is mandatory on those forms and carries the platform's
/// unique index, so it is the only field matched on. Email is deliberately NOT matched: siblings and
/// parents share one address, and matching on it would hand one person's course access to another.</para>
///
/// <para><b>An existing account is never modified.</b> Not the name, not the email, not the password,
/// wallet, roles or verification state. A franchisee typing a nickname into their order form must not
/// be able to rewrite a real customer's profile. All that happens is <c>Order.UserId</c> is set.</para>
/// </summary>
public interface IStudentAccountProvisioner
{
    /// <summary>
    /// Returns the account for this student, creating one when the phone is new. Never throws: a
    /// provisioning failure comes back as <see cref="StudentAccountOutcome.Failed"/> with a null id so
    /// the order it belongs to can still complete. Safe under concurrency — a race that loses to the
    /// unique phone index is recovered by re-reading, never by creating a second account.
    /// </summary>
    Task<StudentAccountResult> ResolveOrCreateAsync(StudentAccountRequest request, CancellationToken ct = default);

    /// <summary>
    /// READ-ONLY half of <see cref="ResolveOrCreateAsync"/>: who owns this phone right now, and is the
    /// phone usable as an identity at all. Exists so a dry run can report exactly what the live path
    /// would decide without a second copy of the matching rules — the two must never drift apart.
    /// </summary>
    Task<StudentPhoneMatch> FindCustomerByPhoneAsync(string? phone, CancellationToken ct = default);

    /// <summary>
    /// READ-ONLY. Reports what a backfill would do to existing orders that have student details but no
    /// <c>UserId</c>. Issues SELECTs only — no INSERT, UPDATE or DELETE, and nothing is left tracked.
    /// </summary>
    /// <param name="source">Restrict to one order source (e.g. Franchisee), or null for all.</param>
    Task<StudentLinkDryRunReport> BackfillDryRunAsync(Enums.OrderSource? source = null, CancellationToken ct = default);
}
