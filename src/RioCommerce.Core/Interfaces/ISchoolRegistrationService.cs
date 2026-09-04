using RioCommerce.Core.DTOs.School;

namespace RioCommerce.Core.Interfaces;

public interface ISchoolRegistrationService
{
    Task<SchoolLookupResult?> LookupByUdiseAsync(string udiseCode);
    Task<(bool ok, string? error)> RegisterPrincipalAsync(SchoolPrincipalRegisterRequest request);
    Task<(bool ok, string? error, Guid? userId)> VerifyOtpAsync(string email, string code);
    Task<(bool ok, string? error)> ResendOtpAsync(string email);
    Task<SchoolPortalDashboard?> GetDashboardAsync(Guid userId);

    Task<List<SchoolCoordinatorListItem>> ListCoordinatorsAsync(Guid principalUserId);
    Task<SchoolCoordinatorScope?> GetCoordinatorScopeAsync(Guid principalUserId);
    Task<(bool ok, string? error)> AddCoordinatorAsync(Guid principalUserId, SchoolCoordinatorCreateRequest request);
    Task<(bool ok, string? error)> ToggleCoordinatorAsync(Guid principalUserId, Guid coordinatorUserId);
}
