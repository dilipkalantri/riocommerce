using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class FinanceReportService : IFinanceReportService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };
    private readonly RioCommerceDbContext _db;
    public FinanceReportService(RioCommerceDbContext db) => _db = db;

    // Wallet top-ups are money IN, not revenue — the sale is recorded when the franchisee spends it.
    private IQueryable<Core.Entities.Order> RevenueOrders =>
        _db.Orders.ExcludeWalletTopUps().Where(o => !NonRevenue.Contains(o.Status));

    public async Task<RevenueSummaryDto> GetRevenueSummaryAsync()
    {
        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        var weekStart = today.AddDays(-6);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var gross = await RevenueOrders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        var refunds = await _db.Refunds.Where(r => r.Status == RefundStatus.Succeeded).SumAsync(r => (decimal?)r.Amount) ?? 0;
        var pending = await _db.Orders.Where(o => o.PaymentStatus == PaymentStatus.Pending && o.Status != OrderStatus.Cancelled)
            .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        var todayRev = await RevenueOrders.Where(o => o.CreatedAt >= today).SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        var weekRev = await RevenueOrders.Where(o => o.CreatedAt >= weekStart).SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        var monthRev = await RevenueOrders.Where(o => o.CreatedAt >= monthStart).SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

        return new RevenueSummaryDto(gross, gross - refunds, refunds, pending, todayRev, weekRev, monthRev);
    }

    public async Task<RefundAnalyticsDto> GetRefundAnalyticsAsync()
    {
        var succeeded = await _db.Refunds.Where(r => r.Status == RefundStatus.Succeeded)
            .GroupBy(_ => 1).Select(g => new { Total = g.Sum(x => x.Amount), Count = g.Count() }).FirstOrDefaultAsync();
        var gross = await RevenueOrders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
        var pendingApprovals = await _db.ApprovalRequests.CountAsync(a => a.Status == ApprovalStatus.Pending && a.Type == ApprovalType.RefundApproval);
        var total = succeeded?.Total ?? 0;
        var rate = gross > 0 ? Math.Round(total / gross * 100, 2) : 0;
        return new RefundAnalyticsDto(total, succeeded?.Count ?? 0, rate, pendingApprovals);
    }

    public async Task<PaymentAnalyticsDto> GetPaymentAnalyticsAsync()
    {
        var byStatus = await _db.Payments.GroupBy(p => p.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int Count(PaymentStatus s) => byStatus.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        var success = Count(PaymentStatus.Success);
        var failed = Count(PaymentStatus.Failed);
        var pending = Count(PaymentStatus.Pending);
        var total = byStatus.Sum(x => x.Count);
        var rate = (success + failed) > 0 ? Math.Round((decimal)success / (success + failed) * 100, 1) : 0;

        var byMethod = await _db.Payments.GroupBy(p => p.PaymentMode)
            .Select(g => new MethodSliceDto(g.Key.ToString(), g.Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Amount).ToListAsync();

        return new PaymentAnalyticsDto(total, success, failed, pending, rate, byMethod);
    }

    public Task<List<NamedRevenueDto>> GetTopCoursesAsync(int take = 5) =>
        _db.OrderItems.Where(i => !NonRevenue.Contains(i.Order.Status))
            .GroupBy(i => i.ProductTitle)
            .Select(g => new NamedRevenueDto(g.Key, g.Count(), g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.Revenue).Take(take).ToListAsync();

    public Task<List<NamedRevenueDto>> GetTopFacultyAsync(int take = 5) =>
        _db.OrderItems.Where(i => !NonRevenue.Contains(i.Order.Status) && i.Product.PrimaryFaculty != null)
            .GroupBy(i => i.Product.PrimaryFaculty!.DisplayName)
            .Select(g => new NamedRevenueDto(g.Key, g.Count(), g.Sum(x => x.LineTotal)))
            .OrderByDescending(x => x.Revenue).Take(take).ToListAsync();

    public Task<List<NamedRevenueDto>> GetFranchiseRevenueAsync(int take = 5) =>
        RevenueOrders.Where(o => o.FranchiseId != null && o.Franchise != null)
            .GroupBy(o => o.Franchise!.Name)
            .Select(g => new NamedRevenueDto(g.Key, g.Count(), g.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.Revenue).Take(take).ToListAsync();

    public async Task<List<RevenueTrendPoint>> GetRevenueTrendAsync(int days = 14)
    {
        var from = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-(days - 1)), DateTimeKind.Utc);
        var raw = await RevenueOrders.Where(o => o.CreatedAt >= from)
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Revenue = g.Sum(x => x.TotalAmount) })
            .ToListAsync();
        var map = raw.ToDictionary(x => x.Date, x => x.Revenue);
        return Enumerable.Range(0, days).Select(i =>
        {
            var d = from.AddDays(i);
            return new RevenueTrendPoint(d, map.TryGetValue(d, out var r) ? r : 0);
        }).ToList();
    }

    public async Task<FinanceDashboardDto> GetDashboardAsync() => new(
        await GetRevenueSummaryAsync(),
        await GetRefundAnalyticsAsync(),
        await GetPaymentAnalyticsAsync(),
        await GetTopCoursesAsync(),
        await GetTopFacultyAsync(),
        await GetFranchiseRevenueAsync(),
        await GetRevenueTrendAsync());
}
