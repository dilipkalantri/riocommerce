using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces.Repositories;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace RioCommerce.Infrastructure.Repositories;
public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(RioCommerceDbContext context) : base(context) { }
    public async Task<Order?> GetByOrderNumberAsync(string orderNumber) => await _dbSet.Include(o => o.Items).Include(o => o.Payments).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
    public async Task<string> GenerateOrderNumberAsync(bool isFranchise = false)
    {
        var prefix = isFranchise ? "FRN" : "RIO";
        var last = await _dbSet.Where(o => o.OrderNumber.StartsWith(prefix)).OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
        int next = 1001;
        if (last != null && int.TryParse(last.OrderNumber.Split('-').Last(), out var n)) next = n + 1;
        return $"{prefix}-{next:D4}";
    }
}
