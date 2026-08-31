using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

public interface ISchoolService
{
    Task<List<SchoolListItem>> ListAsync(string? search = null, Guid? districtId = null);
    Task<SchoolEditModel?> GetAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveAsync(SchoolEditModel model);
    Task<(bool ok, string? error)> ToggleAsync(Guid id);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
    Task<SchoolImportResult> ImportFromExcelAsync(Stream excelStream);
    Task<SchoolEditModel?> FindByUdiseAsync(string udiseCode);
}
