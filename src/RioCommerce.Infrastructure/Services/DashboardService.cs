using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };

    /// <summary>
    /// The business day is an Indian one. Timestamps are stored in UTC, so "today" has to be the IST
    /// day converted back to UTC — <c>DateTime.UtcNow.Date</c> would start the day at 05:30 IST and
    /// put every order placed between midnight and 05:30 into YESTERDAY's figures. Setting the
    /// process TZ does not help: UtcNow.Date is UTC midnight by definition.
    /// </summary>
    private static readonly TimeZoneInfo India = ResolveIndiaZone();

    private static TimeZoneInfo ResolveIndiaZone()
    {
        // Windows and Linux disagree on the id, and a machine with no tz database must not take the
        // dashboard down — fall back to the fixed +05:30 offset, which is correct for India anyway
        // (it has no daylight saving).
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }

    /// <summary>Start of the current IST day, expressed in UTC for comparison against stored timestamps.</summary>
    private static DateTime IstDayStartUtc()
    {
        var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(istNow.Date, DateTimeKind.Unspecified), India);
    }

    /// <summary>Start of the current IST month, expressed in UTC.</summary>
    private static DateTime IstMonthStartUtc()
    {
        var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
        var firstOfMonth = new DateTime(istNow.Year, istNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(firstOfMonth, India);
    }

    private readonly RioCommerceDbContext _db;
    private readonly IFacultySharingService _sharing;
    public DashboardService(RioCommerceDbContext db, IFacultySharingService sharing) { _db = db; _sharing = sharing; }

    public async Task<DashboardData> GetAsync()
    {
        var todayStart = IstDayStartUtc();
        var tomorrow = todayStart.AddDays(1);
        var monthStart = IstMonthStartUtc();

        // Franchise wallet top-ups are stored as orders so they invoice normally. They're money in,
        // not sales, so every figure on this dashboard is built from the sales-only view.
        var sales = _db.Orders.ExcludeWalletTopUps();

        // One definition of "an order that counts", shared by the revenue and the count, so the two
        // cards can never describe different sets of orders sitting next to each other.
        var todaysOrders = sales.Where(o => o.CreatedAt >= todayStart
                                         && o.CreatedAt < tomorrow
                                         && !NonRevenue.Contains(o.Status));

        var d = new DashboardData
        {
            RevenueToday = await todaysOrders.SumAsync(o => (decimal?)o.TotalAmount) ?? 0,
            OrdersToday = await todaysOrders.CountAsync(),
            PendingOrders = await sales.CountAsync(o => o.Status == OrderStatus.Pending),
            ActiveStudents = await sales.Where(o => o.UserId != null).Select(o => o.UserId).Distinct().CountAsync(),
            RecentOrders = await sales.Include(o => o.Items).OrderByDescending(o => o.CreatedAt).Take(6)
                .Select(o => new RecentOrder(
                    o.OrderNumber, o.StudentName,
                    o.Items.Count == 0 ? "—" : o.Items.First().ProductTitle + (o.Items.Count > 1 ? $" +{o.Items.Count - 1}" : ""),
                    o.TotalAmount, o.Status))
                .ToListAsync()
        };
        d.FacultyEarnings = await _sharing.PayoutAsync(monthStart, monthStart.AddMonths(1));
        return d;
    }
}
