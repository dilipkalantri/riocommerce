using RioCommerce.Core.DTOs.School;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// School Portal student management.
///
/// SECURITY CONTRACT — every method takes the AUTHENTICATED user's id and derives the school
/// from it via SchoolUsers. No method accepts a SchoolId from the caller, so a principal
/// cannot reach another school's students by editing a request. A user with no active
/// Principal/Coordinator row resolves to no school and therefore sees nothing.
/// </summary>
public interface ISchoolStudentService
{
    /// <summary>The school the given user administers, or null if they administer none.</summary>
    Task<Guid?> ResolveSchoolIdAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>Students of the acting user's own school. Empty when they administer none.</summary>
    Task<List<SchoolStudentListItem>> ListAsync(Guid actingUserId, string? search = null, CancellationToken ct = default);

    /// <summary>
    /// Adds a student to the acting user's school. If an account already exists for the given
    /// email/phone it is LINKED rather than duplicated; otherwise a new account is created.
    /// Either way the account is granted the "student" role only.
    /// </summary>
    Task<AddSchoolStudentResult> AddAsync(Guid actingUserId, AddSchoolStudentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Adds many students in one submit. Each row runs through the same AddAsync path, so
    /// row-level rules (link vs create, duplicate email/phone in this school) still apply.
    /// A failing row is reported and skipped — the whole submit does not roll back.
    /// </summary>
    Task<BulkAddSchoolStudentResult> AddManyAsync(Guid actingUserId, BulkAddSchoolStudentRequest request, CancellationToken ct = default);

    /// <summary>Removes a student from the acting user's school. Scoped to that school.</summary>
    Task<(bool ok, string? error)> RemoveAsync(Guid actingUserId, Guid schoolStudentId, CancellationToken ct = default);

    /// <summary>Live student count for the dashboard tile.</summary>
    Task<int> CountAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// True when the user is linked to a registered school in ANY capacity — a student on the
    /// roll, or a staff member (Principal / Coordinator). Drives the two-tier product pricing:
    /// eligible users see and pay <c>Product.SchoolStudentPrice</c> when it is set and lower.
    /// The check is server-authoritative; the client never asserts it.
    /// </summary>
    Task<bool> IsSchoolLinkedAsync(Guid userId, CancellationToken ct = default);
}
