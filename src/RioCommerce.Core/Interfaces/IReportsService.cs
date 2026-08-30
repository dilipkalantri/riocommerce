using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface IReportsService
{
    Task<SalesReport> SalesAsync();
}
