using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// Cancelling and deleting an order are restricted to super admins.
///
/// <para>Both actions reverse a sale the customer has usually already paid for, and both are
/// reachable from more than one place — a sidebar button, a status dropdown, a bulk action, and the
/// admin API, which is open to <c>admin</c> and <c>operations</c> as well. Hiding the buttons is not
/// a control; these tests assert the service refuses regardless of how it is reached.</para>
/// </summary>
public class OrderCancelDeletePermissionTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"perm-{Guid.NewGuid()}")
            .Options);

    private static readonly Guid SuperAdmin = Guid.NewGuid();
    private static readonly Guid Operations = Guid.NewGuid();

    private static Mock<IPermissionService> Perms()
    {
        var m = new Mock<IPermissionService>();
        m.Setup(p => p.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        m.Setup(p => p.IsSuperAdminAsync(SuperAdmin)).ReturnsAsync(true);
        return m;
    }

    private static OrderAdminService Svc(RioCommerceDbContext db, Mock<IPermissionService>? perms = null) =>
        new(db,
            new Mock<IAuditService>().Object,
            new Mock<IRealtimeBus>().Object,
            new Mock<INotificationCenterService>().Object,
            new Mock<IOrderCalculationService>().Object,
            new Mock<INotificationService>().Object,
            new Mock<IFranchiseService>().Object,
            new Mock<IFacultySharingService>().Object,
            new Mock<ISerialKeyService>().Object,
            new Mock<IInvoiceService>().Object,
            new Mock<IInstallmentService>().Object,
            new Mock<INotificationSender>().Object,
            (perms ?? Perms()).Object,
            NullLogger<OrderAdminService>.Instance);

    private static async Task<Order> SeedAsync(RioCommerceDbContext db, string number = "RIO-1073")
    {
        var o = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = number, UserId = Guid.NewGuid(),
            StudentName = "Aujus Aggarwal", StudentPhone = "8708600443", StudentEmail = "a@example.com",
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m,
        };
        db.Orders.Add(o);
        await db.SaveChangesAsync();
        return o;
    }

    private static async Task<Order> ReloadAsync(RioCommerceDbContext db, Guid id) =>
        await db.Orders.IgnoreQueryFilters().AsNoTracking().FirstAsync(o => o.Id == id);

    // ── Cancel ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASuperAdminCanCancel()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);

        var (ok, error) = await Svc(db).UpdateStatusAsync(o.Id, OrderStatus.Cancelled, SuperAdmin, "Boss");

        Assert.True(ok, error);
        Assert.Equal(OrderStatus.Cancelled, (await ReloadAsync(db, o.Id)).Status);
    }

    [Fact]
    public async Task ANonSuperAdminCannotCancel_AndTheOrderIsUntouched()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);

        var (ok, error) = await Svc(db).UpdateStatusAsync(o.Id, OrderStatus.Cancelled, Operations, "Ops");

        Assert.False(ok);
        Assert.Contains("Super Admin", error);
        var after = await ReloadAsync(db, o.Id);
        Assert.Equal(OrderStatus.Confirmed, after.Status);
        Assert.Null(after.CancelledAt);
    }

    [Fact]
    public async Task AnUnknownActorCannotCancel()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);

        // Background jobs and unauthenticated callers arrive with no actor id. There is no path
        // where one of those legitimately cancels a paid order, so absence is refusal.
        var (ok, _) = await Svc(db).UpdateStatusAsync(o.Id, OrderStatus.Cancelled, null, "system");

        Assert.False(ok);
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(db, o.Id)).Status);
    }

    [Fact]
    public async Task AnEmptyActorIdCannotCancel()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var perms = Perms();
        // Guid.Empty is what the API controller produces when the token carries no usable id —
        // it must never be handed to the permission service as if it were a real user.
        perms.Setup(p => p.IsSuperAdminAsync(Guid.Empty)).ReturnsAsync(true);

        var (ok, _) = await Svc(db, perms).UpdateStatusAsync(o.Id, OrderStatus.Cancelled, Guid.Empty, "api");

        Assert.False(ok);
        perms.Verify(p => p.IsSuperAdminAsync(Guid.Empty), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Delivered)]
    public async Task EveryOtherStatusIsStillOpenToNonSuperAdmins(OrderStatus status)
    {
        using var db = NewDb();
        var o = await SeedAsync(db);
        var tracked = await db.Orders.FirstAsync(x => x.Id == o.Id);
        tracked.Status = OrderStatus.Pending;
        await db.SaveChangesAsync();

        var (ok, error) = await Svc(db).UpdateStatusAsync(o.Id, status, Operations, "Ops");

        Assert.True(ok, error);   // the gate is on cancellation alone, not on status changes at large
        Assert.Equal(status, (await ReloadAsync(db, o.Id)).Status);
    }

    // ── Bulk cancel ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANonSuperAdminCannotBulkCancel_AndNothingIsPartiallyApplied()
    {
        using var db = NewDb();
        var a = await SeedAsync(db, "RIO-2001");
        var b = await SeedAsync(db, "RIO-2002");

        var (updated, error) = await Svc(db)
            .BulkUpdateStatusAsync(new[] { a.Id, b.Id }, OrderStatus.Cancelled, Operations, "Ops");

        Assert.Equal(0, updated);
        Assert.Contains("Super Admin", error);
        // A partial bulk cancel is harder to spot and harder to undo than an outright refusal.
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(db, a.Id)).Status);
        Assert.Equal(OrderStatus.Confirmed, (await ReloadAsync(db, b.Id)).Status);
    }

    [Fact]
    public async Task ASuperAdminCanBulkCancel()
    {
        using var db = NewDb();
        var a = await SeedAsync(db, "RIO-2001");
        var b = await SeedAsync(db, "RIO-2002");

        var (updated, error) = await Svc(db)
            .BulkUpdateStatusAsync(new[] { a.Id, b.Id }, OrderStatus.Cancelled, SuperAdmin, "Boss");

        Assert.Null(error);
        Assert.Equal(2, updated);
    }

    [Fact]
    public async Task ANonSuperAdminCanStillBulkConfirm()
    {
        using var db = NewDb();
        var a = await SeedAsync(db, "RIO-2001");

        var (updated, error) = await Svc(db)
            .BulkUpdateStatusAsync(new[] { a.Id }, OrderStatus.Processing, Operations, "Ops");

        Assert.Null(error);
        Assert.Equal(1, updated);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASuperAdminCanDelete()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);

        var (ok, error) = await Svc(db).SoftDeleteAsync(o.Id, SuperAdmin, "Boss");

        Assert.True(ok, error);
        Assert.True((await ReloadAsync(db, o.Id)).IsDeleted);
    }

    [Fact]
    public async Task ANonSuperAdminCannotDelete_AndTheOrderStays()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);

        var (ok, error) = await Svc(db).SoftDeleteAsync(o.Id, Operations, "Ops");

        Assert.False(ok);
        Assert.Contains("Super Admin", error);
        var after = await ReloadAsync(db, o.Id);
        Assert.False(after.IsDeleted);
        Assert.Null(after.DeletedAt);
    }

    [Fact]
    public async Task AnUnknownActorCannotDelete()
    {
        using var db = NewDb();
        var o = await SeedAsync(db);

        var (ok, _) = await Svc(db).SoftDeleteAsync(o.Id, null, "system");

        Assert.False(ok);
        Assert.False((await ReloadAsync(db, o.Id)).IsDeleted);
    }

    [Fact]
    public async Task TheRefusalIsCheckedBeforeTheOrderIsEvenLoaded()
    {
        using var db = NewDb();

        // A non-super-admin gets the permission message, not "not found" — the gate must not leak
        // whether a given order id exists.
        var (ok, error) = await Svc(db).SoftDeleteAsync(Guid.NewGuid(), Operations, "Ops");

        Assert.False(ok);
        Assert.Contains("Super Admin", error);
    }
}
