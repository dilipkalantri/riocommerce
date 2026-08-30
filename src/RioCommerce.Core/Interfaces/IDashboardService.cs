using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface IDashboardService
{
    Task<DashboardData> GetAsync();
}
