using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

public interface IAcademicYearService
{
    Task<List<AcademicYearItem>> ListAsync();
    Task<AcademicYearEditModel?> GetAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveAsync(AcademicYearEditModel model);
    Task<(bool ok, string? error)> ToggleAsync(Guid id);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
    Task<AcademicYearItem?> GetCurrentAsync();
}
