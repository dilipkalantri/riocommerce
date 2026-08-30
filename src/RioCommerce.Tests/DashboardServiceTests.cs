using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The admin dashboard's day and month windows.
///
/// <para>Timestamps are stored in UTC but the business day is Indian, so "today" has to be the IST
/// day. Using <c>DateTime.UtcNow.Date</c> started the day at 05:30 IST, which quietly dropped every
/// order placed between midnight and 05:30 into the previous day's revenue.</para>
/// </summary>
public class DashboardServiceTests
{
    private static readonly TimeZoneInfo India = ResolveIndia();

    private static TimeZoneInfo ResolveIndia()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "IST", "IST");
    }

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"dash-{Guid.NewGuid()}")
            .Options);

    private static DashboardService Svc(RioCommerceDbContext db)
    {
        var sharing = new Mock<IFacultySharingService>();
        sharing.Setup(s => s.PayoutAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .ReturnsAsync(new List<FacultyPayout>());
        return new DashboardService(db, sharing.Object);
    }

    /// <summary>
    /// An order placed at the given IST wall-clock time, <paramref name="dayOffset"/> days from today.
    ///
    /// <para>The timestamp is applied in a SECOND save on purpose: <c>RioCommerceDbContext.SaveChangesAsync</c>
    /// overwrites <c>CreatedAt</c> with <c>UtcNow</c> for every newly added entity, so a seeded value
    /// set on insert is silently discarded. On an update it only touches <c>UpdatedAt</c>, which is
    /// what makes the placed-at time stick.</para>
    /// </summary>
    private static async Task SeedOrderAtIstAsync(
        RioCommerceDbContext db, string number, int hour, int minute, decimal amount,
        OrderStatus status = OrderStatus.Confirmed, OrderSource source = OrderSource.Website,
        int dayOffset = 0, Guid? userId = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = number, UserId = userId ?? Guid.NewGuid(),
            StudentName = "Test Student", StudentPhone = "9876500000",
            Status = status, PaymentStatus = PaymentStatus.Success, Source = source,
            Subtotal = amount, TotalAmount = amount,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        order.CreatedAt = IstToUtc(hour, minute, dayOffset);
        await db.SaveChangesAsync();
    }

    /// <summary>An IST wall-clock time on the day <paramref name="dayOffset"/> from today, as UTC.</summary>
    private static DateTime IstToUtc(int hour, int minute, int dayOffset = 0)
    {
        var istToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India).Date.AddDays(dayOffset);
        var stamp = new DateTime(istToday.Year, istToday.Month, istToday.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(stamp, India);
    }

    // ── The bug this fixes ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOrderPlacedJustAfterMidnightIst_CountsAsToday()
    {
        using var db = NewDb();
        // 00:41 IST — the real RIO-1060 case. In UTC this is 19:11 on the PREVIOUS day, so a
        // UtcNow.Date window excluded it from today entirely.
        await SeedOrderAtIstAsync(db, "RIO-1060", hour: 0, minute: 41, amount: 299m);

        var d = await Svc(db).GetAsync();

        Assert.Equal(1, d.OrdersToday);
        Assert.Equal(299m, d.RevenueToday);
    }

    [Fact]
    public async Task TheWholeIstDayIsCounted_MidnightToLateEvening()
    {
        using var db = NewDb();
        await SeedOrderAtIstAsync(db, "A", 0, 5, 100m);     // just after IST midnight
        await SeedOrderAtIstAsync(db, "B", 5, 29, 200m);    // the last minute before UTC midnight
        await SeedOrderAtIstAsync(db, "C", 12, 0, 300m);    // midday
        await SeedOrderAtIstAsync(db, "D", 23, 55, 400m);   // just before IST midnight

        var d = await Svc(db).GetAsync();

        Assert.Equal(4, d.OrdersToday);
        Assert.Equal(1000m, d.RevenueToday);
    }

    [Fact]
    public async Task YesterdaysOrder_IsNotCountedAsToday()
    {
        using var db = NewDb();
        // 23:30 IST yesterday — after UTC midnight, so a naive UTC window would have called it "today".
        await SeedOrderAtIstAsync(db, "OLD", 23, 30, 999m, dayOffset: -1);

        var d = await Svc(db).GetAsync();

        Assert.Equal(0, d.OrdersToday);
        Assert.Equal(0m, d.RevenueToday);
    }

    // ── The two "today" cards describe the same set of orders ───────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    [InlineData(OrderStatus.Draft)]
    public async Task ANonRevenueOrder_IsExcludedFromBothTheCountAndTheRevenue(OrderStatus status)
    {
        using var db = NewDb();
        await SeedOrderAtIstAsync(db, "GOOD", 10, 0, 500m);
        await SeedOrderAtIstAsync(db, "BAD", 11, 0, 700m, status);

        var d = await Svc(db).GetAsync();

        // Previously the count included it while the revenue did not, so the two cards contradicted
        // each other on the same screen.
        Assert.Equal(1, d.OrdersToday);
        Assert.Equal(500m, d.RevenueToday);
    }

    // ── Everything else on the bar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task WalletTopUps_AreNotSales()
    {
        using var db = NewDb();
        await SeedOrderAtIstAsync(db, "SALE", 10, 0, 500m);
        await SeedOrderAtIstAsync(db, "TOPUP", 11, 0, 50000m, source: OrderSource.WalletTopUp);

        var d = await Svc(db).GetAsync();

        Assert.Equal(1, d.OrdersToday);
        Assert.Equal(500m, d.RevenueToday);
    }

    [Fact]
    public async Task PendingOrders_CountsEveryPendingOrderNotJustTodays()
    {
        using var db = NewDb();
        await SeedOrderAtIstAsync(db, "P1", 9, 0, 100m, OrderStatus.Pending);
        await SeedOrderAtIstAsync(db, "P2", 10, 0, 200m, OrderStatus.Pending);
        await SeedOrderAtIstAsync(db, "C1", 11, 0, 300m, OrderStatus.Confirmed);

        var d = await Svc(db).GetAsync();

        Assert.Equal(2, d.PendingOrders);
        // …and a pending order is still a real order, so it belongs in today's figures.
        Assert.Equal(3, d.OrdersToday);
    }

    [Fact]
    public async Task ActiveStudents_CountsEachCustomerOnce()
    {
        using var db = NewDb();
        var repeatCustomer = Guid.NewGuid();
        await SeedOrderAtIstAsync(db, "R1", 9, 0, 100m, userId: repeatCustomer);
        await SeedOrderAtIstAsync(db, "R2", 10, 0, 100m, userId: repeatCustomer);
        await SeedOrderAtIstAsync(db, "OTHER", 12, 0, 100m);   // a different customer

        var d = await Svc(db).GetAsync();

        Assert.Equal(2, d.ActiveStudents);
    }

    [Fact]
    public async Task FacultyEarnings_AreAskedForTheIstMonth()
    {
        using var db = NewDb();
        var sharing = new Mock<IFacultySharingService>();
        DateTime from = default, to = default;
        sharing.Setup(s => s.PayoutAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
               .Callback<DateTime, DateTime>((f, t) => { from = f; to = t; })
               .ReturnsAsync(new List<FacultyPayout>());

        await new DashboardService(db, sharing.Object).GetAsync();

        var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
        var expected = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(istNow.Year, istNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), India);

        Assert.Equal(expected, from);
        Assert.Equal(expected.AddMonths(1), to);
        // The IST month starts at 18:30 UTC on the last day of the previous month, never at UTC midnight.
        Assert.Equal(new TimeSpan(18, 30, 0), from.TimeOfDay);
    }
}
