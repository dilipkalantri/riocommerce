using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

public interface IGeographyService
{
    Task<List<StateItem>> ListStatesAsync();
    Task<List<DistrictItem>> ListDistrictsAsync(Guid? stateId = null);
    Task<List<TalukaItem>> ListTalukasAsync(Guid? districtId = null);

    Task<(bool ok, string? error, Guid id)> SaveDistrictAsync(DistrictEditModel model);
    Task<(bool ok, string? error, Guid id)> SaveTalukaAsync(TalukaEditModel model);
    Task<(bool ok, string? error)> DeleteDistrictAsync(Guid id);
    Task<(bool ok, string? error)> DeleteTalukaAsync(Guid id);
}
