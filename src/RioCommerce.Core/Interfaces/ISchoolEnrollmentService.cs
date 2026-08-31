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
    /// Active, in the catalogue's own display order. Empty when the catalogue has none.
    /// </summary>
    Task<List<SchoolEnrollmentProduct>> ListProductsAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates ONE unpaid order for the chosen course covering the chosen students.
    ///
    /// Rejects the whole request — writing nothing — if the caller administers no school, the
    /// product is not Active, or any student is not on the caller's own roll. Payment is handled
    /// separately by Accounts; no gateway is contacted.
    /// </summary>
    Task<PlaceSchoolEnrollmentResult> PlaceAsync(
        Guid actingUserId, PlaceSchoolEnrollmentRequest request, CancellationToken ct = default);
}
