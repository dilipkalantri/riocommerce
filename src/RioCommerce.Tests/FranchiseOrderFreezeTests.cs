using RioCommerce.Core.DTOs.Orders;
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
/// Once a franchisee has paid, <b>the franchisee</b> can no longer change that order's basket.
///
/// <para>Payment settles their side irreversibly: the wallet is debited by
/// <c>FranchiseNetPayable</c>, a ledger row records that exact figure, and any coupon redemption is
/// counted. None of it is recomputed when an item changes afterwards — so a franchisee editing their
/// own paid order could quietly change what they owe while the ledger keeps saying something else.</para>
///
/// <para><b>Staff are not locked out.</b> Admins and super admins keep full editing rights; they are
/// the ones who fix a mis-keyed order and can correct the ledger side too. Half of these tests exist
/// to hold that line, because a lock that caught staff would make bad orders unfixable.</para>
/// </summary>
public class FranchiseOrderFreezeTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"freeze-{Guid.NewGuid()}")
            .Options);

    private static OrderAdminService Svc(RioCommerceDbContext db) =>
        new(db,
            new Mock<IAuditService>().Object,
            new Mock<IRealtimeBus>().Object,
            new Mock<INotificationCenterService>().Object,
            new OrderCalculationService(db),
            new Mock<INotificationService>().Object,
            new Mock<IFranchiseService>().Object,
            new Mock<IFacultySharingService>().Object,
            new Mock<ISerialKeyService>().Object,
            new Mock<IInvoiceService>().Object,
            new Mock<IInstallmentService>().Object,
            new Mock<INotificationSender>().Object,
            new Mock<IPermissionService>().Object,
            NullLogger<OrderAdminService>.Instance);

    /// <summary>Creates a user carrying the given role names, the way the seed data does.</summary>
    private static async Task<Guid> UserWithRolesAsync(RioCommerceDbContext db, params string[] roleNames)
    {
        var userId = Guid.NewGuid();
        foreach (var name in roleNames)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
            if (role == null)
            {
                role = new Role { Id = Guid.NewGuid(), Name = name, DisplayName = name };
                db.Roles.Add(role);
                await db.SaveChangesAsync();
            }
            db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userId, RoleId = role.Id, IsActive = true });
        }
        await db.SaveChangesAsync();
        return userId;
    }

    /// <summary>FRN-1021 shape: a franchise order with two lines.</summary>
    private static async Task<(Order Order, OrderItem First)> SeedAsync(
        RioCommerceDbContext db,
        OrderSource source = OrderSource.Franchisee,
        PaymentStatus payment = PaymentStatus.Success)
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var second = Guid.NewGuid();

        db.Products.Add(new Product { Id = second, Title = "CA Inter Costing", Slug = $"p-{second:N}", SellingPrice = 700m });
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "FRN-1021", UserId = Guid.NewGuid(),
            StudentName = "Pratham Jain", StudentPhone = "7976745154", StudentEmail = "p@example.com",
            Source = source, Status = OrderStatus.Confirmed, PaymentStatus = payment,
            Subtotal = 1000m, TotalAmount = 1000m, FranchiseId = Guid.NewGuid(),
        });
        var a = new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = orderId, ProductId = productId,
            ProductTitle = "CA Inter Audit", Quantity = 1, UnitPrice = 600m, LineTotal = 600m,
        };
        var b = new OrderItem
        {
            Id = Guid.NewGuid(), OrderId = orderId, ProductId = second,
            ProductTitle = "CA Inter Costing", Quantity = 1, UnitPrice = 400m, LineTotal = 400m,
        };
        db.OrderItems.AddRange(a, b);
        await db.SaveChangesAsync();
        return (db.Orders.First(o => o.Id == orderId), a);
    }

    private static UpdateOrderItemRequest Edit(Guid itemId, int qty = 5, decimal unit = 100m) =>
        new() { ItemId = itemId, Quantity = qty, UnitPrice = unit, Discount = 0m };

    // ── The franchisee is locked out ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFranchiseUserCannotEditTheirOwnPaidOrder()
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");

        var (ok, error) = await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id), franchisee, "Franchise");

        Assert.False(ok);
        Assert.Contains("locked", error, StringComparison.OrdinalIgnoreCase);

        var after = await db.OrderItems.AsNoTracking().FirstAsync(i => i.Id == item.Id);
        Assert.Equal(1, after.Quantity);
        Assert.Equal(600m, after.UnitPrice);
    }

    [Fact]
    public async Task AFranchiseUserCannotAddAProductToTheirPaidOrder()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");
        var product = await db.Products.AsNoTracking().FirstAsync();

        var (ok, error) = await Svc(db).AddItemAsync(order.Id,
            new AddOrderItemRequest { ProductId = product.Id, Quantity = 1 }, franchisee, "Franchise");

        Assert.False(ok);
        Assert.Contains("locked", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await db.OrderItems.CountAsync(i => i.OrderId == order.Id));
    }

    [Fact]
    public async Task AFranchiseUserCannotRemoveAnItemFromTheirPaidOrder()
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");

        var (ok, error) = await Svc(db).RemoveItemAsync(order.Id, item.Id, franchisee, "Franchise");

        Assert.False(ok);
        Assert.Contains("locked", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, await db.OrderItems.CountAsync(i => i.OrderId == order.Id));
    }

    [Fact]
    public async Task AFranchiseOwnerIdentifiedOnlyByTheFranchiseRecordIsAlsoLocked()
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        // No franchise_admin role — linked purely by Franchise.AdminUserId. The two are set
        // independently, so checking only the role would leave a way around the lock.
        var owner = Guid.NewGuid();
        db.Franchises.Add(new Franchise
        {
            Id = Guid.NewGuid(), Name = "CA Point", Code = "CAP",
            AdminUserId = owner, IsActive = true, Status = FranchiseStatus.Approved,
        });
        await db.SaveChangesAsync();

        var (ok, _) = await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id), owner, "Owner");

        Assert.False(ok);
    }

    [Fact]
    public async Task TheOrderTotalIsUntouchedByARefusedEdit()
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");

        await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id, qty: 9, unit: 9999m), franchisee, "Franchise");

        // The wallet was debited against this figure — it must not drift.
        Assert.Equal(1000m, (await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id)).TotalAmount);
    }

    // ── Staff keep working ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("admin")]
    [InlineData("super_admin")]
    public async Task StaffCanStillEditAPaidFranchiseOrder(string role)
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        var staff = await UserWithRolesAsync(db, role);

        var (ok, error) = await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id, qty: 3, unit: 600m), staff, "Staff");

        // Staff are the ones who fix a mis-keyed order, and they can correct the ledger side too.
        Assert.True(ok, error);
        Assert.Equal(3, (await db.OrderItems.AsNoTracking().FirstAsync(i => i.Id == item.Id)).Quantity);
    }

    [Fact]
    public async Task StaffCanStillAddAndRemoveOnAPaidFranchiseOrder()
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        var staff = await UserWithRolesAsync(db, "admin");
        var product = await db.Products.AsNoTracking().FirstAsync();
        var svc = Svc(db);

        Assert.True((await svc.AddItemAsync(order.Id,
            new AddOrderItemRequest { ProductId = product.Id, Quantity = 1 }, staff, "Staff")).ok);
        Assert.True((await svc.RemoveItemAsync(order.Id, item.Id, staff, "Staff")).ok);
    }

    [Fact]
    public async Task AFranchiseRoleAlongsideAdminIsTreatedAsStaff()
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db);
        var both = await UserWithRolesAsync(db, "franchise_admin", "admin");

        var (ok, error) = await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id, qty: 2, unit: 600m), both, "Staff");

        // Granting someone franchise access must never quietly strip their admin rights.
        Assert.True(ok, error);
    }

    // ── Still open before payment ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    public async Task AFranchiseUserCanStillEditTheirUnpaidOrder(PaymentStatus payment)
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db, payment: payment);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");

        var (ok, error) = await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id, qty: 2, unit: 600m), franchisee, "Franchise");

        // Nothing has been settled yet — no wallet debit, no ledger row, nothing to contradict.
        Assert.True(ok, error);
        Assert.Equal(2, (await db.OrderItems.AsNoTracking().FirstAsync(i => i.Id == item.Id)).Quantity);
    }

    // ── Only franchise orders are covered ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderSource.Website)]
    [InlineData(OrderSource.Counter)]
    public async Task APaidNonFranchiseOrderIsNotLocked(OrderSource source)
    {
        using var db = NewDb();
        var (order, item) = await SeedAsync(db, source: source);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");

        var (ok, error) = await Svc(db).UpdateItemAsync(order.Id, Edit(item.Id, qty: 3, unit: 600m), franchisee, "Franchise");

        // No franchise wallet or ledger row is involved, so the reason for locking does not apply.
        Assert.True(ok, error);
    }

    // ── What the lock deliberately does NOT cover ───────────────────────────────────────────────

    [Fact]
    public async Task TheDeliveryAddressCanStillBeCorrected()
    {
        using var db = NewDb();
        var (order, _) = await SeedAsync(db);
        var franchisee = await UserWithRolesAsync(db, "franchise_admin");

        var (ok, error) = await Svc(db).UpdateAddressesAsync(new OrderAddressEdit
        {
            OrderId = order.Id,
            ShippingAddress = "12 New Lane",
            ShippingCity = "Jaipur",
            ShippingState = "Rajasthan",
            ShippingPincode = "302001",
        }, franchisee);

        // A delivery address legitimately changes after payment and moves no money — locking it
        // would block a correction the courier depends on.
        Assert.True(ok, error);
        Assert.Equal("12 New Lane", (await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id)).ShippingAddress);
    }
}
