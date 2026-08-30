using RioCommerce.Core.Interfaces;
using RioCommerce.Core.Interfaces.Repositories;
using RioCommerce.Infrastructure.Data;
namespace RioCommerce.Infrastructure.Repositories;
public class UnitOfWork : IUnitOfWork
{
    private readonly RioCommerceDbContext _context;
    private readonly Dictionary<Type, object> _repos = new();
    public UnitOfWork(RioCommerceDbContext context, IProductRepository products, IOrderRepository orders) { _context = context; Products = products; Orders = orders; }
    public IProductRepository Products { get; }
    public IOrderRepository Orders { get; }
    public IGenericRepository<T> Repository<T>() where T : class { var t = typeof(T); if (!_repos.ContainsKey(t)) _repos[t] = new GenericRepository<T>(_context); return (IGenericRepository<T>)_repos[t]; }
    public async Task<int> SaveChangesAsync() => await _context.SaveChangesAsync();
    public void Dispose() => _context.Dispose();
}
