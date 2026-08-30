using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ReportsService : IReportsService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };
    private readonly RioCommerceDbContext _db;
    public ReportsService(RioCommerceDbContext db) => _db = db;

    public async Task<SalesReport> SalesAsync()
    {
        // Wallet top-ups are orders only so they can be invoiced; they are not sales and would
        // double-count against the courses their money later buys.
        var orders = await _db.Orders.ExcludeWalletTopUps().Include(o => o.Items).ToListAsync();
        var revenue = orders.Where(o => !NonRevenue.Contains(o.Status)).ToList();

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var today = now.Date;

        var revTotal = revenue.Sum(o => o.TotalAmount);
        var revMtd = revenue.Where(o => o.CreatedAt >= monthStart).Sum(o => o.TotalAmount);
        var revToday = revenue.Where(o => o.CreatedAt.Date == today).Sum(o => o.TotalAmount);
        var aov = revenue.Count == 0 ? 0 : Math.Round(revTotal / revenue.Count, 0);

        var bySource = revenue.GroupBy(o => o.Source)
            .Select(g => new SourceRevenue(Pretty(g.Key), g.Count(), g.Sum(o => o.TotalAmount)))
            .OrderByDescending(s => s.Revenue).ToList();

        var topProducts = revenue.SelectMany(o => o.Items)
            .GroupBy(i => i.ProductTitle)
            .Select(g => new TopProduct(g.Key, g.Select(i => i.OrderId).Distinct().Count(), g.Sum(i => i.LineTotal)))
            .OrderByDescending(t => t.Revenue).Take(5).ToList();

        var last14 = Enumerable.Range(0, 14)
            .Select(d => today.AddDays(-13 + d))
            .Select(day => new DailyRevenue(day.ToString("dd MMM"),
                revenue.Where(o => o.CreatedAt.Date == day).Sum(o => o.TotalAmount)))
            .ToList();

        return new SalesReport(revTotal, revMtd, revToday, orders.Count,
            orders.Count(o => o.PaymentStatus == PaymentStatus.Success), aov, bySource, topProducts, last14);
    }

    private static string Pretty(OrderSource s) => s switch
    {
        OrderSource.Website => "Website",
        OrderSource.Counter => "Counter",
        OrderSource.Franchisee => "Franchisee",
        OrderSource.Other => "Other",
        _ => s.ToString()
    };
}
