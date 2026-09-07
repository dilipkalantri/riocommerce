using RioCommerce.Core.DTOs.School;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// School Portal course enrolment.
///
/// This is an ORCHESTRATOR, not a new enrolment engine: it reads the existing product catalogue,
/// reuses <see cref="ISchoolStudentService.ResolveSchoolIdAsync"/> for school scoping, and writes
/// an ordinary <c>Order</c> through the same entities checkout uses. Course access is still
/// granted by the existing admin activation path (<c>OrderAdminService.ActivateEnrollmentAsync</c>),
/// which is what creates the <c>Enrollment</c> rows — never this service.
///
/// SECURITY CONTRACT — identical to ISchoolStudentService: every method takes the AUTHENTICATED
/// user's id and derives the school from it. No method accepts a SchoolId from the caller.
/// </summary>
public interface ISchoolEnrollmentService
{
    /// <summary>
    /// Courses a principal may enrol students on: every product the catalogue currently marks
    /// Active, in the catalogue's own display order, priced for the CALLER'S school (district
    /// affects the school-tier price on some courses). Empty when the catalogue has none.
    /// </summary>
    Task<List<SchoolEnrollmentProduct>> ListProductsAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Creates ONE unpaid order for the chosen course covering the chosen students.
    ///
    /// Rejects the whole request — writing nothing — if the caller administers no school, the
    /// product is not Active, or any student is not on the caller's own roll. Payment is handled
    /// separately by Accounts; no gateway is contacted.
    /// </summary>
    Task<PlaceSchoolEnrollmentResult> PlaceAsync(
        Guid actingUserId, PlaceSchoolEnrollmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// True when this order number is a school-enrolment order (it has a roster). Used to route the
    /// payment result into the School portal instead of the storefront checkout pages. Takes no
    /// user: it answers a property of the order, and reveals nothing but its kind.
    /// </summary>
    Task<bool> IsSchoolOrderAsync(string orderNumber, CancellationToken ct = default);

    /// <summary>Enrolment orders placed by the caller's school, newest first.</summary>
    Task<List<SchoolEnrollmentOrderRow>> ListOrdersAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Per-student payment history for the caller's school, newest first. One row per student per
    /// enrolment, carrying the order's persisted payment status and the invoice when one exists.
    /// Scoped by the roster's SchoolId — another school's rows are never selected.
    /// </summary>
    Task<List<SchoolStudentPaymentRow>> ListStudentPaymentsAsync(
        Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Dashboard headline figures for the caller's school. Every number is counted from persisted
    /// orders and the school roll — nothing is inferred from the client.
    /// </summary>
    Task<SchoolPaymentSummary> GetPaymentSummaryAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// School-paid status per student for the caller's school, keyed by student user id.
    ///
    /// <para>A student appears only if the school has actually raised an enrolment order for them.
    /// Students absent from the result have no school-paid obligation at all — which is the correct
    /// answer for someone who merely named this school when self-registering.</para>
    /// </summary>
    Task<Dictionary<Guid, SchoolStudentPaymentStatus>> GetStudentPaymentStatusesAsync(
        Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Student invoices belonging to the caller's school, optionally narrowed to one order.
    /// Scoped by the roster, so another school's invoices are not merely hidden — they are
    /// never selected.
    /// </summary>
    Task<List<SchoolStudentInvoiceRow>> ListInvoicesAsync(
        Guid actingUserId, Guid? orderId = null, CancellationToken ct = default);

    /// <summary>
    /// Renders a student invoice PDF for a principal, or returns null when the invoice does not
    /// belong to a student on the caller's own roll. Null covers both "no such invoice" and
    /// "another school's invoice" so an id cannot be probed for existence.
    /// </summary>
    Task<(byte[] bytes, string filename)?> RenderInvoicePdfAsync(
        Guid actingUserId, Guid invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Per-coordinator enrolment activity for the principal's dashboard: students enrolled, orders
    /// placed, paid vs pending amounts, last activity — grouped over the requested time window.
    /// Includes every active Coordinator on the caller's school, so a coordinator with zero
    /// activity still appears (with zeros) rather than being invisible. Principal-only in intent;
    /// a coordinator caller resolves to their own school only and would see the same numbers
    /// scoped by CreatedById below anyway, but this is a dashboard method.
    /// </summary>
    Task<CoordinatorActivitySummary> GetCoordinatorActivityAsync(
        Guid actingUserId, CoordinatorActivityRange range, CancellationToken ct = default);

    /// <summary>
    /// The students a single coordinator enrolled in the given time window — powers the inline
    /// expander on the principal's Coordinator Activity card. Returns at most <paramref name="take"/>
    /// rows plus the untruncated total, so the UI can render "N more…" without a second query.
    /// School scope is enforced from <paramref name="actingUserId"/>; asking about a coordinator
    /// on another school returns an empty list, not a leak.
    /// </summary>
    Task<CoordinatorEnrolmentDetails> GetCoordinatorEnrolmentsAsync(
        Guid actingUserId, Guid coordinatorUserId, CoordinatorActivityRange range, int take = 5, CancellationToken ct = default);
}
