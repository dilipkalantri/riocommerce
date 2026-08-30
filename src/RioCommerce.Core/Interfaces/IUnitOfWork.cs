using RioCommerce.Core.Interfaces.Repositories;
namespace RioCommerce.Core.Interfaces;
public interface IUnitOfWork : IDisposable
{
    IProductRepository Products { get; }
    IOrderRepository Orders { get; }
    IGenericRepository<T> Repository<T>() where T : class;
    Task<int> SaveChangesAsync();
}
