namespace RioCommerce.Core.DTOs.School;

/// <summary>
/// What a signed-in staff member is allowed to reach in the School Portal — the ONE authority on
/// scope for every query in the module.
///
/// A Principal gets their whole school: <see cref="RestrictToAddedByUserId"/> is null.
/// A Coordinator gets ONLY the students they personally added, so it carries their own user id and
/// every query adds <c>AddedByUserId == RestrictToAddedByUserId</c> alongside the school predicate.
///
/// Two consequences are deliberate:
/// <list type="bullet">
/// <item>Students added before migration 0050 have <c>AddedByUserId = NULL</c> and therefore belong
/// to no coordinator. They stay visible to the Principal and to nobody else.</item>
/// <item>The restriction is a DATABASE predicate, never a post-filter and never a UI concern, so a
/// tampered id or a hand-made request cannot widen it.</item>
/// </list>
/// </summary>
public sealed record SchoolAccessScope(Guid SchoolId, Guid? RestrictToAddedByUserId)
{
    /// <summary>True for a Coordinator — the caller sees only their own students.</summary>
    public bool IsRestricted => RestrictToAddedByUserId is not null;
}

// ── Excel bulk import ─────────────────────────────────────────────────────────────────────────

/// <summary>Verdict for one spreadsheet row after validation.</summary>
public enum SchoolStudentImportStatus
{
    /// <summary>Passes every check and will be created on confirm.</summary>
    Valid,
    /// <summary>A field is missing or malformed. Never imported.</summary>
    Invalid,
    /// <summary>The same mobile or email appears on an earlier row of THIS file.</summary>
    DuplicateInFile,
    /// <summary>The student already exists on this school's roll. Never re-created.</summary>
    AlreadyExists,
}

/// <summary>
/// One parsed spreadsheet row, carrying the values AND the verdict, so the preview can show the
/// writer exactly what will happen before anything is written.
/// <para><c>ExcelRow</c> is the 1-based row number as it appears in Excel (header = 1), so an error
/// points at the row the writer is actually looking at.</para>
/// </summary>
public record SchoolStudentImportRow(
    int ExcelRow,
    string? FullName,
    string? Email,
    string? Phone,
    DateTime? DateOfBirth,
    string? Gender,
    string? StudentClass,
    SchoolStudentImportStatus Status,
    string? Error)
{
    public bool WillImport => Status == SchoolStudentImportStatus.Valid;
}

/// <summary>
/// The result of parsing a workbook. Nothing has been written when this is produced — it exists so
/// the whole file can be validated and shown before a single student is created.
/// </summary>
public record SchoolStudentImportPreview(
    bool Ok,
    string? Error,
    IReadOnlyList<SchoolStudentImportRow> Rows)
{
    public int TotalRows => Rows.Count;
    public int ValidRows => Rows.Count(r => r.Status == SchoolStudentImportStatus.Valid);
    public int InvalidRows => Rows.Count(r => r.Status == SchoolStudentImportStatus.Invalid);
    public int DuplicateRows => Rows.Count(r => r.Status is SchoolStudentImportStatus.DuplicateInFile
                                                        or SchoolStudentImportStatus.AlreadyExists);

    public static SchoolStudentImportPreview Fail(string error) =>
        new(false, error, Array.Empty<SchoolStudentImportRow>());
}

/// <summary>Outcome of committing a previewed import.</summary>
public record SchoolStudentImportResult(
    int Imported,
    int Linked,
    int Failed,
    IReadOnlyList<SchoolStudentImportRow> FailedRows);

/// <summary>One row of the School Portal students table.</summary>
public record SchoolStudentListItem(
    Guid Id,
    Guid UserId,
    string FullName,
    string? Email,
    string? Phone,
    string? StudentClass,
    string? Section,
    string? RollNumber,
    string SchoolName,
    bool IsActive,
    bool IsVerified,
    DateTime CreatedAt,
    Guid? AddedByUserId,
    string? AddedByName);

/// <summary>Pick for the principal's "Added by" filter on the Students page.</summary>
public record SchoolCoordinatorPick(Guid UserId, string FullName);

/// <summary>
/// Payload for a principal adding a student.
///
/// NOTE there is deliberately no SchoolId here. The school is resolved server-side from the
/// authenticated principal, so a crafted request cannot attach a student to another school.
/// </summary>
public class AddSchoolStudentRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? StudentClass { get; set; }
    public string? Section { get; set; }
    public string? RollNumber { get; set; }
}

/// <summary>Outcome of an add, so the UI can say whether an existing account was linked.</summary>
public record AddSchoolStudentResult(bool Ok, string? Error, Guid? UserId, bool LinkedExisting);

/// <summary>
/// Bulk add — several students in one submit. Each row is validated and inserted
/// independently, so a bad row never blocks a good one on the same submit.
/// </summary>
public class BulkAddSchoolStudentRequest
{
    public List<AddSchoolStudentRequest> Rows { get; set; } = new();
}

/// <summary>Per-row outcome so the UI can keep the failing rows in the grid.</summary>
public record BulkAddSchoolStudentRowResult(int Index, bool Ok, string? Error, bool LinkedExisting, string? FullName);

public record BulkAddSchoolStudentResult(
    int Created,
    int Linked,
    int Failed,
    List<BulkAddSchoolStudentRowResult> Rows);
