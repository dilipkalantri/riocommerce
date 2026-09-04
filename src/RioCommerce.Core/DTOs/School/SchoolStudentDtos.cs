namespace RioCommerce.Core.DTOs.School;

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
    DateTime CreatedAt);

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
