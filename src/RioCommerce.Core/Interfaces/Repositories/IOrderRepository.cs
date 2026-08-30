using RioCommerce.Core.Entities;
namespace RioCommerce.Core.Interfaces.Repositories;
public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetByOrderNumberAsync(string orderNumber);
    Task<string> GenerateOrderNumberAsync(bool isFranchise = false);
}
