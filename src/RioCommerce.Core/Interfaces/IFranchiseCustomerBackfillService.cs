using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// One-off repair for orders that were written before the order paths linked a customer account:
/// they carry the student's name, phone and email but <c>Order.UserId</c> is null, so the student has
/// no login, no order history and no course access.
///
/// <para>Deliberately NOT reachable over HTTP. It is registered in DI and driven the way
/// <see cref="ILegacyMigrationService"/> is — from a super-admin-only page — so a stray request can
/// never start it.</para>
///
/// <para><b>Scope is exactly two columns:</b> a customer account per student, and
/// <c>Order.UserId</c> on their orders. It does not touch payments, invoices, serial keys, enrollments,
/// course access, products, faculty, franchises or any existing customer's profile, and it sends no
/// email of any kind. Course access is verified separately, afterwards.</para>
/// </summary>
public interface IFranchiseCustomerBackfillService
{
    /// <summary>
    /// Links every unlinked order in scope to a customer, creating accounts only for phone numbers
    /// nobody holds yet.
    /// </summary>
    /// <param name="dryRun">
    /// REQUIRED, and deliberately has no default — nobody should be able to start a write by forgetting
    /// an argument. When true the pass issues SELECTs only: no INSERT, UPDATE or DELETE, and the
    /// returned report says precisely what a live pass would do.
    /// </param>
    /// <param name="source">Restrict to one order source (defaults to franchise orders).</param>
    Task<StudentLinkBackfillReport> RunAsync(bool dryRun, OrderSource? source = OrderSource.Franchisee, CancellationToken ct = default);
}
