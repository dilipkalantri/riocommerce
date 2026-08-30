using System.Linq.Expressions;
using RioCommerce.Core.Interfaces.Repositories;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace RioCommerce.Infrastructure.Repositories;
public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly RioCommerceDbContext _context;
    protected readonly DbSet<T> _dbSet;
    public GenericRepository(RioCommerceDbContext context) { _context = context; _dbSet = context.Set<T>(); }
    public async Task<T?> GetByIdAsync(Guid id) => await _dbSet.FindAsync(id);
    public async Task<IReadOnlyList<T>> GetAllAsync() => await _dbSet.ToListAsync();
    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate) => await _dbSet.Where(predicate).ToListAsync();
    public async Task<T> AddAsync(T entity) { await _dbSet.AddAsync(entity); return entity; }
    public Task UpdateAsync(T entity) { _dbSet.Update(entity); return Task.CompletedTask; }
    public Task DeleteAsync(T entity) { _dbSet.Remove(entity); return Task.CompletedTask; }
    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null) => predicate != null ? await _dbSet.CountAsync(predicate) : await _dbSet.CountAsync();
}
