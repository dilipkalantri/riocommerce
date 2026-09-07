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

    /// <summary>
    /// The full access scope for this user: their school PLUS the added-by restriction that applies
    /// to a Coordinator. Null when they administer no school.
    ///
    /// Every query in the school module builds its predicate from this, which is what keeps
    /// "a Coordinator sees only their own students" enforced in the database rather than the UI.
    /// The role is read from the SchoolUsers table, never from a claim the client could influence.
    /// </summary>
    Task<SchoolAccessScope?> ResolveScopeAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// Students of the acting user's own school. Empty when they administer none.
    /// <paramref name="addedByUserId"/> narrows the roster to students added by that staff
    /// member — used by the principal's "Added by" filter. Coordinators see only their own
    /// students regardless of this argument (the scope helper enforces it).
    /// </summary>
    Task<List<SchoolStudentListItem>> ListAsync(Guid actingUserId, string? search = null, Guid? addedByUserId = null, CancellationToken ct = default);

    /// <summary>
    /// The active staff members (Principal + Coordinators) of the acting user's school. Powers the
    /// principal's "Added by" filter on the Students page — so returns the whole staff list, not
    /// just people who happen to have created a student row.
    /// </summary>
    Task<List<SchoolCoordinatorPick>> ListSchoolStaffAsync(Guid actingUserId, CancellationToken ct = default);

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

    /// <summary>
    /// The blank .xlsx template for the bulk import — header row plus one example line. Generated,
    /// not shipped as a file, so the columns can never drift from what <see cref="ParseImportAsync"/>
    /// reads.
    /// </summary>
    byte[] BuildImportTemplate();

    /// <summary>
    /// Parses and validates a whole workbook WITHOUT writing anything, so the caller can show the
    /// writer exactly what will happen before committing. Duplicate detection covers both repeats
    /// inside the file and students already on the acting user's school roll.
    /// </summary>
    Task<SchoolStudentImportPreview> ParseImportAsync(Guid actingUserId, Stream xlsx, CancellationToken ct = default);

    /// <summary>
    /// Commits a previewed import. Only rows the SERVER judged valid are created, each through the
    /// existing <see cref="AddAsync"/> path — so school scoping, duplicate rules and the
    /// AddedByUserId stamp are identical to adding a student by hand, and none of them can be
    /// influenced by the spreadsheet.
    /// </summary>
    Task<SchoolStudentImportResult> ImportAsync(
        Guid actingUserId, IReadOnlyList<SchoolStudentImportRow> rows, CancellationToken ct = default);

    /// <summary>Live student count for the dashboard tile — the whole school.</summary>
    Task<int> CountAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// How many of the school's active students THIS user added. Powers the coordinator
    /// dashboard, which shows only the caller's own contribution rather than school-wide figures.
    /// Students added before 0050 have no recorded creator and are counted for nobody.
    /// </summary>
    Task<int> CountAddedByAsync(Guid actingUserId, CancellationToken ct = default);

    /// <summary>
    /// True when the user is linked to a registered school in ANY capacity — a student on the
    /// roll, or a staff member (Principal / Coordinator). Drives the two-tier product pricing:
    /// eligible users see and pay <c>Product.SchoolStudentPrice</c> when it is set and lower.
    /// The check is server-authoritative; the client never asserts it.
    /// </summary>
    Task<bool> IsSchoolLinkedAsync(Guid userId, CancellationToken ct = default);
}
