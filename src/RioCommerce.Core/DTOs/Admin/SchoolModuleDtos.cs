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
