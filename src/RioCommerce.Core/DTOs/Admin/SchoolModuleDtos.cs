using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Admin;

// ── Academic Year ──
public record AcademicYearItem(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsCurrent, bool IsActive);

public class AcademicYearEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsActive { get; set; } = true;
}

// ── Geography ──
public record StateItem(Guid Id, string Name, string Code, bool IsActive, int DistrictCount);
public record DistrictItem(Guid Id, string Name, Guid StateId, string StateName, bool IsActive, int TalukaCount);
public record TalukaItem(Guid Id, string Name, Guid DistrictId, string DistrictName, bool IsActive);

public class DistrictEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid StateId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TalukaEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid DistrictId { get; set; }
    public bool IsActive { get; set; } = true;
}

// ── School ──
public record SchoolListItem(
    Guid Id, string UdiseCode, string Name, SchoolType SchoolType,
    int LowestClass, int HighestClass,
    string? CityOrVillage, string? TalukaName, string? DistrictName,
    bool IsActive, int UserCount);

/// <summary>
/// Narrow projection for the public school picker on /student/register.
/// SchoolListItem is deliberately NOT reused here: it joins Taluka + District and
/// counts SchoolUsers per row, which is wasted work for a type-ahead over a district
/// holding thousands of rows. This carries only what the option row renders, plus the
/// class range used to constrain the Class/Standard dropdown.
/// </summary>
public record SchoolOption(
    Guid Id, string Name, string UdiseCode, string? CityOrVillage,
    int LowestClass, int HighestClass);

// ── Education master ──
public record BoardItem(Guid Id, string Name);

/// <summary>
/// Outcome of the authoritative server-side check on a student's education selection.
/// The resolved NAMES come back so the caller stores exactly what the database says,
/// never what the browser claimed.
/// </summary>
public record EducationValidation(
    bool Ok,
    string? Error,
    Guid? SchoolId, string? SchoolName,
    Guid? BoardId, string? BoardName,
    string? StateName, string? DistrictName,
    /// <summary>Validated taluka. Set for both a picked school and an "Other" entry.</summary>
    Guid? TalukaId = null,
    string? TalukaName = null);

public class SchoolEditModel
{
    public Guid? Id { get; set; }
    public string UdiseCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public SchoolType SchoolType { get; set; }
    public int LowestClass { get; set; } = 1;
    public int HighestClass { get; set; } = 10;
    public string? CityOrVillage { get; set; }
    public Guid? TalukaId { get; set; }
    public Guid? DistrictId { get; set; }
    public Guid? StateId { get; set; }
    public string? PinCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public record SchoolImportResult(int Created, int Updated, int Errors, List<string> ErrorMessages);
