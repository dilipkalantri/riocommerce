namespace RioCommerce.Core.DTOs.School;

public record SchoolLookupResult(
    Guid Id,
    string UdiseCode,
    string Name,
    string? Address,
    string SchoolType,
    int LowestClass,
    int HighestClass,
    string? CityOrVillage,
    string? TalukaName,
    string? DistrictName,
    string? StateName,
    bool AlreadyRegistered);

public class SchoolPrincipalRegisterRequest
{
    public string UdiseCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class SchoolVerifyOtpRequest
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public class SchoolResendOtpRequest
{
    public string Email { get; set; } = string.Empty;
}

public record SchoolPortalDashboard(
    string SchoolName,
    string UdiseCode,
    string? DistrictName,
    string UserRole,
    int StudentCount,
    int CoordinatorCount,
    int EnrollmentCount);

public class SchoolCoordinatorCreateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Standard { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
}

public record SchoolCoordinatorScope(int LowestClass, int HighestClass, List<string> Mediums);

public record SchoolCoordinatorListItem(
    Guid Id,
    Guid UserId,
    string FullName,
    string? Email,
    string? Phone,
    string? Standard,
    string? Medium,
    bool IsActive,
    DateTime CreatedAt);
