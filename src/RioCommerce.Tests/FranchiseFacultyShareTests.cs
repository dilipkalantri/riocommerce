using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using RioCommerce.Infrastructure.Services.Customers;
using RioCommerce.Infrastructure.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// A faculty earns from a sale whoever sold it — but the franchise portal never recorded it.
///
/// <para>The website and admin/counter paths have always written a <c>FacultyShareEntry</c> when an
/// order became paid. <c>FranchisePortalService</c> did not, so a course sold by a franchisee earned
/// its teacher nothing and the order could not appear on the Faculty-Wise report at all — that report
/// reads the ledger and nothing else.</para>
///
/// <para>All three franchise payment routes — wallet, popup gateway, redirect callback — converge on
/// <c>FinalizeOrderAsync</c>, and only paid orders reach it. That is where the write belongs, and it
/// is where the invoice and serial-key provisioning already sit. These tests pin both halves: that
/// each paid route now records, and that the routes which must NOT record still do not.</para>
///
/// <para>The share itself is computed by the existing calculator. Nothing here re-implements a rule,
/// a split or a GST figure — the tests assert what that calculator produced, which is the point.</para>
/// </summary>
public class FranchiseFacultyShareTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"frnshare-{Guid.NewGuid()}")
            .Options);

    /// <summary>The REAL sharing service and calculator — a mock here would prove nothing about
    /// whether a franchise sale actually earns a share.</summary>
    private static FacultySharingService RealSharing(RioCommerceDbContext db) =>
        new(db, new FacultyShareCalculator(db), new Mock<IAuditService>().Object);

    private static FranchisePortalService Portal(
        RioCommerceDbContext db,
        IFacultySharingService? facultyShares = null,
        IPaymentGateway? gateway = null,
        IPaymentModeRegistry? modes = null)
    {
        var gateways = new Mock<IPaymentGatewayFactory>();
        if (gateway != null) gateways.Setup(g => g.TryGet(It.IsAny<string>())).Returns(gateway);

        return new FranchisePortalService(
            db,
            new Mock<IFranchiseService>().Object,
            new Mock<INotificationService>().Object,
            new FranchiseShareCalculator(db),
            new Mock<IInvoiceService>().Object,
            gateways.Object,
            new Mock<ISerialKeyService>().Object,
            modes ?? new Mock<IPaymentModeRegistry>().Object,
            new StudentAccountProvisioner(db, NullLogger<StudentAccountProvisioner>.Instance),
            facultyShares ?? RealSharing(db),
            NullLogger<FranchisePortalService>.Instance);
    }

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────
    // ₹1,180 @ 18% GST → taxable base ₹1,000. A 10% rule therefore earns ₹100.

    private sealed record Fx(Guid FranchiseId, Guid ProductId, Guid Harshad, Guid Tejal, Guid SubjectId);

    private static async Task<Fx> SeedAsync(RioCommerceDbContext db, bool withRule = true, bool secondFaculty = false)
    {
        Guid fid = Guid.NewGuid(), pid = Guid.NewGuid(),
             harshad = Guid.NewGuid(), tejal = Guid.NewGuid(), subject = Guid.NewGuid();

        db.Franchises.Add(new Franchise
        {
            Id = fid, Name = "Rajkot Centre", Code = "RAJ", City = "Rajkot", State = "Gujarat",
            AddressLine = "Kalawad Road", PinCode = "360005",
            ContactPerson = "Owner", ContactPhone = "9000000001", ContactEmail = "raj@example.com",
            WalletBalance = 500000m, CreditLimit = 0m, IsActive = true, Status = FranchiseStatus.Approved
        });

        db.Subjects.Add(new Subject { Id = subject, Name = "Audit", Slug = "audit", IsActive = true });
        db.Faculty.AddRange(
            new Faculty { Id = harshad, DisplayName = "CA Harshad Jaju", IsActive = true },
            new Faculty { Id = tejal, DisplayName = "CA CS Tejal Katariya", IsActive = true });

        db.Products.Add(new Product
        {
            Id = pid, Title = "CA Inter Audit Regular", Slug = $"p-{pid:N}", SubjectId = subject,
            SellingPrice = 1180m, Mrp = 1500m, GstRate = 18m, Status = ProductStatus.Active
        });
        db.ProductSubjects.Add(new ProductSubject
        { Id = Guid.NewGuid(), ProductId = pid, SubjectId = subject, IsPrimary = true });

        // Attachment alone earns nothing — a rule is what creates the entitlement.
        db.ProductFaculty.Add(new ProductFaculty
        { Id = Guid.NewGuid(), ProductId = pid, FacultyId = harshad, IsPrimary = true });

        if (withRule)
            db.FacultySharingRules.Add(new FacultySharingRule
            {
                Id = Guid.NewGuid(), ProductId = pid, FacultyId = harshad,
                ShareType = SharingType.Percentage, ShareValue = 10m, IsActive = true
            });

        if (secondFaculty)
        {
            db.ProductFaculty.Add(new ProductFaculty
            { Id = Guid.NewGuid(), ProductId = pid, FacultyId = tejal, IsPrimary = false });
            db.FacultySharingRules.Add(new FacultySharingRule
            {
                Id = Guid.NewGuid(), ProductId = pid, FacultyId = tejal,
                ShareType = SharingType.Percentage, ShareValue = 15m, IsActive = true
            });
        }

        await db.SaveChangesAsync();
        return new Fx(fid, pid, harshad, tejal, subject);
    }

    private static FranchiseOrderRequest Request(Guid productId, int qty = 1) => new()
    {
        Lines = { new FranchiseOrderLine { ProductId = productId, Quantity = qty } },
        CustomerName = "Prachi Vaghela",
        Phone = "9876543210",
        Email = "prachi@example.com",
        AddressLine = "12 MG Road",
        City = "Rajkot",
        State = "Gujarat",
        PinCode = "360005",
        PaymentMethod = FranchisePaymentMethod.Wallet
    };

    /// <summary>An unpaid gateway order in the state the two settlement paths receive it.</summary>
    private static Order PendingGatewayOrder(RioCommerceDbContext db, Guid franchiseId, Guid productId, string number)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = number,
            StudentName = "Prachi Vaghela", StudentPhone = "9876543210", StudentEmail = "prachi@example.com",
            Source = OrderSource.Franchisee, FranchiseId = franchiseId,
            Subtotal = 1180m, TotalAmount = 1180m, FranchiseNetPayable = 1180m,
            Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Pending,
            PaymentMode = PaymentMode.Razorpay
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(), ProductId = productId, ProductTitle = "CA Inter Audit Regular",
            Quantity = 1, UnitPrice = 1180m, Discount = 0m, LineTotal = 1180m
        });
        db.Orders.Add(order);
        return order;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 1. Wallet-paid franchise order
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_wallet_paid_franchise_order_with_a_rule_records_one_ledger_entry()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var (ok, error, number) = await Portal(db).PlaceOrderAsync(fx.FranchiseId, Request(fx.ProductId));
        Assert.True(ok, error);

        var entries = await db.FacultyShareEntries.ToListAsync();
        var entry = Assert.Single(entries);

        Assert.Equal(fx.Harshad, entry.FacultyId);
        Assert.Equal(number, entry.OrderNumber);
        Assert.Equal(fx.ProductId, entry.ProductId);
        // Produced by the existing calculator: 10% of the ₹1,000 taxable base.
        Assert.Equal(100m, entry.ShareAmount);
        Assert.Equal(0m, entry.GstOnShare);              // faculty is not GST-registered
        Assert.Equal(100m, entry.TotalPayout);
        Assert.Equal(SharingType.Percentage, entry.ShareType);
        Assert.Equal(10m, entry.ShareValue);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 2. Razorpay popup confirmation
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_gateway_confirmed_franchise_order_records_one_ledger_entry()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        var order = PendingGatewayOrder(db, fx.FranchiseId, fx.ProductId, "FRN-3001");
        await db.SaveChangesAsync();

        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupGet(g => g.Name).Returns("Razorpay");
        gateway.Setup(g => g.VerifyPayment(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var modes = new Mock<IPaymentModeRegistry>();
        modes.Setup(m => m.GatewayNameFor(PaymentMode.Razorpay)).Returns("Razorpay");

        var (ok, error, _) = await Portal(db, gateway: gateway.Object, modes: modes.Object)
            .ConfirmGatewayOrderAsync(fx.FranchiseId, order.OrderNumber, "order_x", "pay_x", "sig");

        Assert.True(ok, error);
        var entry = Assert.Single(await db.FacultyShareEntries.ToListAsync());
        Assert.Equal(100m, entry.ShareAmount);
        Assert.Equal("FRN-3001", entry.OrderNumber);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 3. Easebuzz redirect callback
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task An_easebuzz_settled_franchise_order_records_one_ledger_entry()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        PendingGatewayOrder(db, fx.FranchiseId, fx.ProductId, "FRN-3002");
        await db.SaveChangesAsync();

        var (ok, error, _) = await Portal(db).SettleGatewayCallbackAsync("FRN-3002", "pay_y", 1180m, null);

        Assert.True(ok, error);
        var entry = Assert.Single(await db.FacultyShareEntries.ToListAsync());
        Assert.Equal(100m, entry.ShareAmount);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 4. A repeated callback must not duplicate the ledger
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_repeated_easebuzz_callback_does_not_duplicate_the_ledger()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);
        PendingGatewayOrder(db, fx.FranchiseId, fx.ProductId, "FRN-3003");
        await db.SaveChangesAsync();

        var portal = Portal(db);
        var (ok1, err1, _) = await portal.SettleGatewayCallbackAsync("FRN-3003", "pay_z", 1180m, null);
        var (ok2, err2, _) = await portal.SettleGatewayCallbackAsync("FRN-3003", "pay_z", 1180m, null);

        Assert.True(ok1, err1);
        Assert.True(ok2, err2);                          // a repeat is success, not an error
        Assert.Single(await db.FacultyShareEntries.ToListAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 5. An unpaid order earns nothing
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_pending_gateway_order_earns_no_share()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupGet(g => g.Name).Returns("Razorpay");
        gateway.Setup(g => g.CreateOrderAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<GatewayCreateContext>()))
               .ReturnsAsync(new GatewayOrder("Razorpay", "order_x", "key_x", 1180m));
        var modes = new Mock<IPaymentModeRegistry>();
        modes.Setup(m => m.ResolveOnlineGatewayAsync()).ReturnsAsync(new ResolvedGateway(PaymentMode.Razorpay, "Razorpay"));

        var req = Request(fx.ProductId);
        req.OriginBaseUrl = "https://www.riocommerce.com";

        var (ok, error, handoff) = await Portal(db, gateway: gateway.Object, modes: modes.Object)
            .CreateGatewayOrderAsync(fx.FranchiseId, req);

        Assert.True(ok, error);
        Assert.NotNull(handoff);

        // The order exists and is Pending — FinalizeOrderAsync was never reached, so no share.
        var order = await db.Orders.FirstAsync(o => o.OrderNumber == handoff!.OrderNumber);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Empty(await db.FacultyShareEntries.ToListAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 6. No sharing rule → no entry
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_paid_franchise_order_without_a_sharing_rule_records_nothing()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, withRule: false);

        var (ok, error, _) = await Portal(db).PlaceOrderAsync(fx.FranchiseId, Request(fx.ProductId));

        Assert.True(ok, error);
        // Faculty IS attached to the product — attachment alone is not an entitlement. This is the
        // exact shape of the live data today, where 15 sold products have faculty but no rules.
        Assert.NotEmpty(await db.ProductFaculty.ToListAsync());
        Assert.Empty(await db.FacultyShareEntries.ToListAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 7. Two faculty on one product
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Two_sharing_rules_produce_one_entry_each_from_the_existing_calculator()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db, secondFaculty: true);

        var (ok, error, _) = await Portal(db).PlaceOrderAsync(fx.FranchiseId, Request(fx.ProductId));
        Assert.True(ok, error);

        var entries = await db.FacultyShareEntries.ToListAsync();
        Assert.Equal(2, entries.Count);

        var harshad = entries.Single(e => e.FacultyId == fx.Harshad);
        var tejal = entries.Single(e => e.FacultyId == fx.Tejal);
        Assert.Equal(100m, harshad.ShareAmount);         // 10% of ₹1,000
        Assert.Equal(150m, tejal.ShareAmount);           // 15% of ₹1,000

        // One order line, two entitlements — 25% of the base in total, well inside the cap, so the
        // calculator did not scale either of them down.
        Assert.All(entries, e => Assert.False(e.WasCapped));
        Assert.Equal(250m, entries.Sum(e => e.TotalPayout));
        Assert.Single(entries.Select(e => e.OrderItemId).Distinct());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 8. A ledger failure must not undo a settled payment
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_share_ledger_failure_leaves_the_paid_order_intact()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var broken = new Mock<IFacultySharingService>();
        broken.Setup(s => s.RecordOrderFacultyShareAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("share ledger unavailable"));

        var (ok, error, number) = await Portal(db, facultyShares: broken.Object)
            .PlaceOrderAsync(fx.FranchiseId, Request(fx.ProductId));

        Assert.True(ok, error);                          // the money moved; the order stands
        var order = await db.Orders.FirstAsync(o => o.OrderNumber == number);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentStatus.Success, order.PaymentStatus);
        Assert.Empty(await db.FacultyShareEntries.ToListAsync());

        // The wallet was still debited and the franchise ledger still written — the failure was
        // contained to the share write alone.
        Assert.Equal(1, await db.FranchiseLedger.CountAsync(e => e.OrderId == order.Id));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 9. The shared service is unchanged for its existing callers
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Recording_a_website_order_behaves_exactly_as_before()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        // A website order, written the way CheckoutService leaves one after payment succeeds.
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = "RIO-4001",
            StudentName = "Web Buyer", StudentPhone = "9811111111",
            Source = OrderSource.Website, UserId = Guid.NewGuid(),
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1180m, TotalAmount = 1180m
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(), ProductId = fx.ProductId, ProductTitle = "CA Inter Audit Regular",
            Quantity = 1, UnitPrice = 1180m, Discount = 0m, LineTotal = 1180m
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var sharing = RealSharing(db);
        await sharing.RecordOrderFacultyShareAsync(order.Id);
        var afterFirst = await db.FacultyShareEntries.CountAsync();

        await sharing.RecordOrderFacultyShareAsync(order.Id);   // idempotent, as it always was

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, await db.FacultyShareEntries.CountAsync());
        var entry = await db.FacultyShareEntries.FirstAsync();
        Assert.Equal(100m, entry.ShareAmount);
        Assert.Equal(OrderSource.Website, (await db.Orders.FirstAsync()).Source);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 10. End to end — the order reaches the Faculty-Wise report
    // ══════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task A_paid_franchise_order_appears_on_the_faculty_wise_report()
    {
        using var db = NewDb();
        var fx = await SeedAsync(db);

        var (ok, error, number) = await Portal(db).PlaceOrderAsync(fx.FranchiseId, Request(fx.ProductId));
        Assert.True(ok, error);

        // Straight through the real report builder — the whole reason this fix exists.
        var table = await new FacultyReportBuilder(db)
            .BuildAsync(new ReportQuery { Page = 1, PageSize = 50 }, default);

        Assert.Equal(1, table.TotalRows);
        Assert.Equal(1, table.Totals!.TotalOrders);
        Assert.Equal(1, table.Totals.TotalQuantity);
        Assert.Equal(1180m, table.Totals.TotalSales);
        Assert.Equal("₹100.00", table.Totals.Extra.First(e => e.Label == "Teacher share (cost)").Value);
        Assert.Equal("₹100.00", table.Totals.Extra.First(e => e.Label == "Total payout").Value);
        Assert.Equal("1", table.Totals.Extra.First(e => e.Label == "Faculty covered").Value);

        var facultyCol = table.Columns.ToList().FindIndex(c => c.Key == "faculty");
        var orderCol = table.Columns.ToList().FindIndex(c => c.Key == "order");
        Assert.Equal("CA Harshad Jaju", table.Rows[0][facultyCol]);
        Assert.Equal(number, table.Rows[0][orderCol]);
    }
}
