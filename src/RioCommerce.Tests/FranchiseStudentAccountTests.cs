using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services;
using RioCommerce.Infrastructure.Services.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// A franchise order is placed FOR a student who never signs in to place it. Until this change
/// nothing linked those orders to a customer account: <c>Order.UserId</c> stayed null, so the student
/// had no login, no order history and — because enrollment keys off exactly that column — no course
/// access at all. Twenty live orders were sitting in that state.
///
/// <para>The rules these tests hold:</para>
/// <list type="bullet">
///   <item><b>Phone is the identity.</b> Nothing else is matched on — email is optional and families
///         share one, so matching on it would hand one person's courses to another.</item>
///   <item><b>An existing customer is linked, never edited.</b> A franchisee typing a nickname and a
///         new email into their order form must not rewrite a real account.</item>
///   <item><b>One phone, one account.</b> Two orders for the same student resolve to the same user,
///         including when they race each other into the unique index.</item>
///   <item><b>The order survives provisioning failure.</b> The franchisee has already paid.</item>
/// </list>
/// </summary>
public class FranchiseStudentAccountTests
{
    // ── Fixtures ───────────────────────────────────────────────────────────────
    private static DbContextOptions<RioCommerceDbContext> Options(string name) =>
        new DbContextOptionsBuilder<RioCommerceDbContext>().UseInMemoryDatabase(name).Options;

    private static RioCommerceDbContext NewDb(string? name = null) =>
        new(Options(name ?? $"student-{Guid.NewGuid()}"));

    private static StudentAccountProvisioner Provisioner(RioCommerceDbContext db) =>
        new(db, NullLogger<StudentAccountProvisioner>.Instance);

    private static FranchisePortalService Portal(
        RioCommerceDbContext db,
        IStudentAccountProvisioner students,
        IPaymentGateway? gateway = null,
        IPaymentModeRegistry? modes = null)
    {
        var gateways = new Mock<IPaymentGatewayFactory>();
        if (gateway != null) gateways.Setup(g => g.TryGet(It.IsAny<string>())).Returns(gateway);

        var registry = modes ?? new Mock<IPaymentModeRegistry>().Object;

        return new FranchisePortalService(
            db,
            new Mock<IFranchiseService>().Object,
            new Mock<INotificationService>().Object,
            new FranchiseShareCalculator(db),
            new Mock<IInvoiceService>().Object,
            gateways.Object,
            new Mock<ISerialKeyService>().Object,
            registry,
            students,
            new Mock<IFacultySharingService>().Object,
            NullLogger<FranchisePortalService>.Instance);
    }

    /// <summary>An active franchise with money in the wallet, and one sellable course.</summary>
    private static async Task<(Guid FranchiseId, Guid ProductId)> SeedAsync(RioCommerceDbContext db)
    {
        var franchiseId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        db.Franchises.Add(new Franchise
        {
            Id = franchiseId, Name = "Rajkot Centre", Code = "RAJ", City = "Rajkot", State = "Gujarat",
            AddressLine = "Kalawad Road", PinCode = "360005",
            ContactPerson = "Owner", ContactPhone = "9000000001", ContactEmail = "raj@example.com",
            WalletBalance = 500000m, CreditLimit = 0m, IsActive = true, Status = FranchiseStatus.Approved
        });
        db.Products.Add(new Product
        {
            Id = productId, Title = "Advanced FR", Slug = $"p-{productId:N}",
            SellingPrice = 5000m, Mrp = 6000m, GstRate = 18m, Status = ProductStatus.Active
        });
        db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "student", DisplayName = "Student", IsActive = true });
        await db.SaveChangesAsync();
        return (franchiseId, productId);
    }

    /// <summary>A complete, valid franchise order form for the given student.</summary>
    private static FranchiseOrderRequest Request(Guid productId, string name, string phone, string? email) => new()
    {
        Lines = { new FranchiseOrderLine { ProductId = productId, Quantity = 1 } },
        CustomerName = name,
        Phone = phone,
        Email = email,
        AddressLine = "12 MG Road",
        City = "Rajkot",
        State = "Gujarat",
        PinCode = "360005",
        PaymentMethod = FranchisePaymentMethod.Wallet
    };

    private static User NewUser(string name, string phone, string? email = null, string? comment = null) => new()
    {
        Id = Guid.NewGuid(), FullName = name, Phone = phone, Email = email,
        PasswordHash = "existing-hash", IsActive = true, IsVerified = true, AdminComment = comment
    };

    private static async Task<Order> OrderAsync(RioCommerceDbContext db, string number) =>
        await db.Orders.FirstAsync(o => o.OrderNumber == number);

    // ── A. New student → account created and linked ───────────────────────────
    [Fact]
    public async Task PlaceOrder_creates_and_links_a_customer_for_a_new_student()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Prachi Vaghela", "9876543210", "prachi@example.com"));

        Assert.True(ok, error);
        var order = await OrderAsync(db, number!);
        Assert.NotNull(order.UserId);

        var user = await db.Users.FirstAsync(u => u.Id == order.UserId);
        Assert.Equal("Prachi Vaghela", user.FullName);
        Assert.Equal("9876543210", user.Phone);
        Assert.Equal("prachi@example.com", user.Email);
    }

    // ── B. Existing phone → reused, no second account ─────────────────────────
    [Fact]
    public async Task PlaceOrder_reuses_the_account_that_already_owns_the_phone()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);
        var existing = NewUser("Nitish Bhardwaj", "9812345678", "nitish@example.com");
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Nitish B", "9812345678", "nitish@example.com"));

        Assert.True(ok, error);
        Assert.Equal(existing.Id, (await OrderAsync(db, number!)).UserId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == "9812345678"));
    }

    // ── C. Two orders, one student → one account ──────────────────────────────
    // FRN-1015 / FRN-1016 (Prachi) and FRN-1018 / FRN-1019 (Nitish) are exactly this shape.
    [Fact]
    public async Task Two_orders_for_the_same_student_share_one_account()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);
        var portal = Portal(db, Provisioner(db));

        var (ok1, err1, first) = await portal.PlaceOrderAsync(fid, Request(pid, "Prachi Vaghela", "9876543210", "prachi@example.com"));
        var (ok2, err2, second) = await portal.PlaceOrderAsync(fid, Request(pid, "Prachi Vaghela", "9876543210", "prachi@example.com"));

        Assert.True(ok1, err1);
        Assert.True(ok2, err2);
        var a = await OrderAsync(db, first!);
        var b = await OrderAsync(db, second!);
        Assert.NotNull(a.UserId);
        Assert.Equal(a.UserId, b.UserId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == "9876543210"));
    }

    // ── D. An existing profile is never edited by an order form ───────────────
    [Fact]
    public async Task Existing_customer_profile_is_left_completely_untouched()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);
        var existing = NewUser("ABC", "9811111111", "old@example.com", "VIP customer");
        existing.IsVerified = true;
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "XYZ", "9811111111", "new@example.com"));
        Assert.True(ok, error);

        var after = await db.Users.AsNoTracking().FirstAsync(u => u.Id == existing.Id);
        Assert.Equal("ABC", after.FullName);
        Assert.Equal("old@example.com", after.Email);
        Assert.Equal("9811111111", after.Phone);
        Assert.Equal("existing-hash", after.PasswordHash);
        Assert.True(after.IsVerified);
        Assert.Equal("VIP customer", after.AdminComment);
        Assert.Equal(existing.Id, (await OrderAsync(db, number!)).UserId);
    }

    // ── E. Different email, same phone → still the same person ────────────────
    [Fact]
    public async Task A_different_email_on_the_same_phone_still_resolves_to_the_same_account()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);
        var existing = NewUser("Riya Shah", "9822222222", "riya@example.com");
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Riya Shah", "9822222222", "riya.new@example.com"));

        Assert.True(ok, error);
        Assert.Equal(existing.Id, (await OrderAsync(db, number!)).UserId);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    // ── F. Same email, different phone → NOT the same person ──────────────────
    // Siblings on one family address. Matching on email here would give the brother his sister's
    // course access, so a new account is created and the shared address is simply not duplicated
    // onto it (the users table has a unique index on Email; the order keeps the address regardless).
    [Fact]
    public async Task A_shared_email_on_a_new_phone_never_matches_the_existing_account()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);
        var sibling = NewUser("Aarav Mehta", "9833333333", "family@example.com");
        db.Users.Add(sibling);
        await db.SaveChangesAsync();

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Anaya Mehta", "9844444444", "family@example.com"));

        Assert.True(ok, error);
        var order = await OrderAsync(db, number!);
        Assert.NotNull(order.UserId);
        Assert.NotEqual(sibling.Id, order.UserId);

        var created = await db.Users.FirstAsync(u => u.Id == order.UserId);
        Assert.Equal("9844444444", created.Phone);
        Assert.Null(created.Email);                                  // the address belongs to the sibling
        Assert.Equal("family@example.com", order.StudentEmail);      // …but is never lost off the order
    }

    // ── G. New accounts are active, unverified, and have NO password ──────────
    [Fact]
    public async Task A_provisioned_account_is_active_unverified_and_has_no_credentials()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Kunal Rao", "9855555555", "kunal@example.com"));
        Assert.True(ok, error);

        var order = await OrderAsync(db, number!);
        var user = await db.Users.FirstAsync(u => u.Id == order.UserId);
        Assert.True(user.IsActive);
        Assert.False(user.IsVerified);
        Assert.Null(user.PasswordHash);   // the student sets one via the normal reset flow

        var role = await db.Roles.FirstAsync(r => r.Name == "student");
        Assert.True(await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == role.Id && ur.IsActive));
    }

    // ── H. Provenance is recorded, and never overwrites an existing note ──────
    [Fact]
    public async Task The_franchise_order_is_recorded_as_the_source_of_a_new_account()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Meera Joshi", "9866666666", "meera@example.com"));
        Assert.True(ok, error);

        var order = await OrderAsync(db, number!);
        var user = await db.Users.FirstAsync(u => u.Id == order.UserId);
        Assert.Equal($"Created from Franchise Order {number}", user.AdminComment);
    }

    [Fact]
    public void An_existing_admin_comment_is_appended_to_never_replaced()
    {
        Assert.Equal("VIP customer\nCreated from Franchise Order FRN-1021",
            StudentAccountProvisioner.AppendComment("VIP customer", "Created from Franchise Order FRN-1021"));

        // Idempotent: re-running the backfill must not stack the same line up again.
        Assert.Equal("Created from Franchise Order FRN-1021",
            StudentAccountProvisioner.AppendComment("Created from Franchise Order FRN-1021", "Created from Franchise Order FRN-1021"));

        Assert.Equal("VIP customer", StudentAccountProvisioner.AppendComment("VIP customer", null));
    }

    // ── I. Popup-gateway confirmation resolves the student too ────────────────
    [Fact]
    public async Task Gateway_confirmation_links_an_order_that_reached_it_unlinked()
    {
        using var db = NewDb();
        var (fid, _) = await SeedAsync(db);
        var order = PendingGatewayOrder(db, fid, "FRN-2001", "Sahil Kapoor", "9877777777", "sahil@example.com");
        await db.SaveChangesAsync();

        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupGet(g => g.Name).Returns("Razorpay");
        gateway.Setup(g => g.VerifyPayment(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var modes = new Mock<IPaymentModeRegistry>();
        modes.Setup(m => m.GatewayNameFor(PaymentMode.Razorpay)).Returns("Razorpay");

        var (ok, error, _) = await Portal(db, Provisioner(db), gateway.Object, modes.Object)
            .ConfirmGatewayOrderAsync(fid, order.OrderNumber, "order_x", "pay_x", "sig");

        Assert.True(ok, error);
        var after = await OrderAsync(db, "FRN-2001");
        Assert.NotNull(after.UserId);
        Assert.Equal("9877777777", (await db.Users.FirstAsync(u => u.Id == after.UserId)).Phone);
    }

    // ── J. Redirect-gateway callback settlement resolves the student too ──────
    [Fact]
    public async Task Gateway_callback_settlement_links_an_order_that_reached_it_unlinked()
    {
        using var db = NewDb();
        var (fid, _) = await SeedAsync(db);
        PendingGatewayOrder(db, fid, "FRN-2002", "Devika Nair", "9888888888", "devika@example.com");
        await db.SaveChangesAsync();

        var (ok, error, _) = await Portal(db, Provisioner(db))
            .SettleGatewayCallbackAsync("FRN-2002", "pay_y", 5000m, null);

        Assert.True(ok, error);
        var after = await OrderAsync(db, "FRN-2002");
        Assert.NotNull(after.UserId);
        Assert.Equal("9888888888", (await db.Users.FirstAsync(u => u.Id == after.UserId)).Phone);
    }

    /// <summary>An unpaid gateway order in the state the two settlement paths receive it.</summary>
    private static Order PendingGatewayOrder(RioCommerceDbContext db, Guid franchiseId, string number, string name, string phone, string email)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = number, UserId = null,
            StudentName = name, StudentPhone = phone, StudentEmail = email,
            Source = OrderSource.Franchisee, FranchiseId = franchiseId,
            Subtotal = 5000m, TotalAmount = 5000m, FranchiseNetPayable = 5000m,
            Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Pending,
            PaymentMode = PaymentMode.Razorpay
        };
        db.Orders.Add(order);
        return order;
    }

    // ── A repeated gateway callback must not produce a second customer ────────
    // Easebuzz can POST its result more than once. The settlement is already idempotent; this pins
    // that the customer resolution inherits that property rather than running again per delivery.
    [Fact]
    public async Task A_repeated_gateway_callback_creates_no_second_customer()
    {
        using var db = NewDb();
        var (fid, _) = await SeedAsync(db);
        PendingGatewayOrder(db, fid, "FRN-2003", "Rahul Sharma", "9876543210", "rahul@example.com");
        await db.SaveChangesAsync();

        var portal = Portal(db, Provisioner(db));
        var (ok1, err1, _) = await portal.SettleGatewayCallbackAsync("FRN-2003", "pay_1", 5000m, null);
        var (ok2, err2, _) = await portal.SettleGatewayCallbackAsync("FRN-2003", "pay_1", 5000m, null);

        Assert.True(ok1, err1);
        Assert.True(ok2, err2);                       // a repeat is success, not an error
        Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == "9876543210"));

        var order = await OrderAsync(db, "FRN-2003");
        Assert.NotNull(order.UserId);
        // Settled once: the second delivery returned before the ledger, invoice or notification.
        Assert.Equal(1, await db.FranchiseLedger.CountAsync(e => e.OrderId == order.Id));
    }

    // ── Identity does not wait for the money, and does not imply access ───────
    [Fact]
    public async Task An_unpaid_gateway_order_has_a_customer_but_no_course_access()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var gateway = new Mock<IPaymentGateway>();
        gateway.SetupGet(g => g.Name).Returns("Razorpay");
        gateway.Setup(g => g.CreateOrderAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<GatewayCreateContext>()))
               .ReturnsAsync(new GatewayOrder("Razorpay", "order_x", "key_x", 5000m));
        var modes = new Mock<IPaymentModeRegistry>();
        modes.Setup(m => m.ResolveOnlineGatewayAsync()).ReturnsAsync(new ResolvedGateway(PaymentMode.Razorpay, "Razorpay"));

        var req = Request(pid, "Ananya Iyer", "9812000001", "ananya@example.com");
        req.OriginBaseUrl = "https://www.riocommerce.com";

        var (ok, error, handoff) = await Portal(db, Provisioner(db), gateway.Object, modes.Object)
            .CreateGatewayOrderAsync(fid, req);

        Assert.True(ok, error);
        Assert.NotNull(handoff);

        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.OrderNumber == handoff!.OrderNumber);
        Assert.NotNull(order.UserId);                          // the student exists from creation…
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.ActivatedAt);                        // …but owns nothing yet
        Assert.All(order.Items, i => Assert.False(i.IsActivated));
        Assert.Equal(0, await db.Enrollments.CountAsync());
    }

    // ── Every franchise payment path resolves the SAME customer ──────────────
    [Fact]
    public async Task The_wallet_path_and_the_callback_path_resolve_the_same_customer()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);
        var portal = Portal(db, Provisioner(db));

        // First order: paid from the franchise wallet, settled inline.
        var (ok, error, walletOrder) = await portal.PlaceOrderAsync(
            fid, Request(pid, "Rahul Sharma", "9876543210", "rahul@example.com"));
        Assert.True(ok, error);

        // Second order for the same student, settled much later by a redirect-gateway callback.
        PendingGatewayOrder(db, fid, "FRN-2004", "Rahul Sharma", "9876543210", "rahul@example.com");
        await db.SaveChangesAsync();
        var (ok2, err2, _) = await portal.SettleGatewayCallbackAsync("FRN-2004", "pay_2", 5000m, null);
        Assert.True(ok2, err2);

        var first = await OrderAsync(db, walletOrder!);
        var second = await OrderAsync(db, "FRN-2004");
        Assert.NotNull(first.UserId);
        Assert.Equal(first.UserId, second.UserId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == "9876543210"));
    }

    // ── K. Enrollment activation works off the link ───────────────────────────
    [Fact]
    public async Task Course_access_activates_through_the_existing_enrollment_path()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var (ok, error, number) = await Portal(db, Provisioner(db))
            .PlaceOrderAsync(fid, Request(pid, "Ishaan Verma", "9899999999", "ishaan@example.com"));
        Assert.True(ok, error);

        var order = await db.Orders.Include(o => o.Items).FirstAsync(o => o.OrderNumber == number);
        Assert.NotNull(order.UserId);

        // The untouched, pre-existing admin path — no franchise-specific enrollment mechanism.
        var granted = await OrderAdmin(db).ActivateEnrollmentAsync(order.Id, null, "test");

        Assert.Equal(1, granted);
        Assert.True(await db.Enrollments.AnyAsync(e => e.UserId == order.UserId && e.ProductId == pid && e.IsActive));
    }

    private static OrderAdminService OrderAdmin(RioCommerceDbContext db) =>
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

    // ── L. A provisioning failure must not cost the franchisee their order ────
    [Fact]
    public async Task The_order_still_completes_when_provisioning_fails()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var broken = new Mock<IStudentAccountProvisioner>();
        broken.Setup(p => p.ResolveOrCreateAsync(It.IsAny<StudentAccountRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(StudentAccountResult.Failed("customers table is unavailable"));

        var (ok, error, number) = await Portal(db, broken.Object)
            .PlaceOrderAsync(fid, Request(pid, "Farhan Sheikh", "9800000000", "farhan@example.com"));

        Assert.True(ok, error);                       // the money moved; the order is kept
        var order = await OrderAsync(db, number!);
        Assert.Null(order.UserId);                    // …flagged for follow-up rather than faked
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task Provisioning_never_throws_out_of_the_order_path()
    {
        using var db = NewDb();
        await SeedAsync(db);

        // A phone that cannot be an identity key at all — skipped, not thrown, not invented.
        var result = await Provisioner(db).ResolveOrCreateAsync(new StudentAccountRequest("No Phone", "  "));

        Assert.Equal(StudentAccountOutcome.Skipped, result.Outcome);
        Assert.Null(result.UserId);
        Assert.Equal(0, await db.Users.CountAsync());
    }

    // ── M. Losing the race against the unique phone index ─────────────────────
    [Fact]
    public async Task A_lost_race_links_the_winning_account_instead_of_creating_a_second()
    {
        var name = $"race-{Guid.NewGuid()}";
        var winner = NewUser("Prachi Vaghela", "9876543210", "prachi@example.com");

        // The competing request commits between our duplicate check and our insert; Postgres then
        // rejects ours on the filtered unique index over users.Phone.
        using var db = new RacingDbContext(Options(name), () =>
        {
            using var other = NewDb(name);
            other.Users.Add(winner);
            other.SaveChanges();
        });

        var result = await Provisioner(db).ResolveOrCreateAsync(
            new StudentAccountRequest("Prachi Vaghela", "9876543210", "prachi@example.com",
                SourceNote: "Created from Franchise Order FRN-1016"));

        Assert.Equal(StudentAccountOutcome.LinkedExisting, result.Outcome);
        Assert.Equal(winner.Id, result.UserId);

        using var check = NewDb(name);
        Assert.Equal(1, await check.Users.CountAsync(u => u.Phone == "9876543210"));
    }

    /// <summary>Fails the first save the way the unique phone index does, after letting a competing
    /// writer commit — the only faithful way to reproduce a 23505 on the in-memory provider.</summary>
    private sealed class RacingDbContext : RioCommerceDbContext
    {
        private readonly Action _competitor;
        private bool _fired;
        public RacingDbContext(DbContextOptions<RioCommerceDbContext> options, Action competitor) : base(options)
            => _competitor = competitor;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_fired)
            {
                _fired = true;
                _competitor();
                throw new DbUpdateException("23505: duplicate key value violates unique constraint \"IX_users_Phone\"");
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    // ── The same phone in a different shape is still the same person ──────────
    [Fact]
    public async Task A_country_code_prefix_does_not_create_a_second_account()
    {
        using var db = NewDb();
        var existing = NewUser("Rohit Salvi", "+919876500001");
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var result = await Provisioner(db).ResolveOrCreateAsync(new StudentAccountRequest("Rohit Salvi", "9876500001"));

        Assert.Equal(StudentAccountOutcome.LinkedExisting, result.Outcome);
        Assert.Equal(existing.Id, result.UserId);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    // ── N. The backfill dry run writes nothing ────────────────────────────────
    [Fact]
    public async Task The_backfill_dry_run_reports_without_writing_anything()
    {
        var name = $"dryrun-{Guid.NewGuid()}";
        Guid linkedUserId;

        using (var seed = NewDb(name))
        {
            var (fid, _) = await SeedAsync(seed);
            var known = NewUser("Nitish Bhardwaj", "9812345678", "nitish@example.com");
            seed.Users.Add(known);
            linkedUserId = known.Id;

            // Two orders for one student who HAS an account, two for one who does not, one already
            // linked, and one with an unusable number.
            PendingGatewayOrder(seed, fid, "FRN-1018", "Nitish Bhardwaj", "9812345678", "nitish@example.com");
            PendingGatewayOrder(seed, fid, "FRN-1019", "Nitish Bhardwaj", "9812345678", "nitish@example.com");
            PendingGatewayOrder(seed, fid, "FRN-1015", "Prachi Vaghela", "9876543210", "prachi@example.com");
            PendingGatewayOrder(seed, fid, "FRN-1016", "Prachi Vaghela", "9876543210", "prachi@example.com");
            PendingGatewayOrder(seed, fid, "FRN-1020", "Aditi Rane", "98700", "aditi@example.com");
            var linked = PendingGatewayOrder(seed, fid, "FRN-1021", "Already Linked", "9865000000", "linked@example.com");
            linked.UserId = known.Id;
            await seed.SaveChangesAsync();
        }

        StudentLinkDryRunReport report;
        using (var db = new ReadOnlyDbContext(Options(name)))
        {
            report = await Provisioner(db).BackfillDryRunAsync(OrderSource.Franchisee);
            // Nothing left tracked means nothing a later SaveChanges could ever flush.
            Assert.Empty(db.ChangeTracker.Entries());
        }

        Assert.Equal(6, report.OrdersScanned);
        Assert.Equal(3, report.DistinctStudents);              // 9812345678, 9876543210, 9865000000 — "98700" is not an identity
        Assert.Equal(1, report.StudentsWithExistingAccount);
        Assert.Equal(1, report.StudentsNeedingNewAccount);
        Assert.Equal(1, report.OrdersAlreadyLinked);
        Assert.Equal(1, report.OrdersNeedingReview);

        Assert.Equal(StudentLinkAction.LinkExisting, Action(report, "FRN-1018"));
        Assert.Equal(StudentLinkAction.LinkExisting, Action(report, "FRN-1019"));
        Assert.Equal(StudentLinkAction.CreateNew, Action(report, "FRN-1015"));
        Assert.Equal(StudentLinkAction.CreateNew, Action(report, "FRN-1016"));
        Assert.Equal(StudentLinkAction.ReviewRequired, Action(report, "FRN-1020"));
        Assert.Equal(StudentLinkAction.NoAction, Action(report, "FRN-1021"));
        Assert.Equal(linkedUserId, report.Rows.First(r => r.OrderNumber == "FRN-1018").MatchedUserId);

        // Both repeat students are called out so nobody backfills them into two accounts.
        Assert.Equal(2, report.DuplicateStudents.Count);
        Assert.All(report.DuplicateStudents, g => Assert.Equal(2, g.OrderNumbers.Count));

        // And the database is exactly as it was left.
        using var after = NewDb(name);
        Assert.Equal(1, await after.Users.CountAsync());
        Assert.Equal(5, await after.Orders.CountAsync(o => o.UserId == null));
        Assert.Equal(0, await after.Enrollments.CountAsync());
    }

    private static StudentLinkAction Action(StudentLinkDryRunReport report, string orderNumber) =>
        report.Rows.First(r => r.OrderNumber == orderNumber).Action;

    /// <summary>Turns any attempted write into a test failure, so "read-only" is enforced rather than
    /// asserted after the fact.</summary>
    private sealed class ReadOnlyDbContext : RioCommerceDbContext
    {
        public ReadOnlyDbContext(DbContextOptions<RioCommerceDbContext> options) : base(options) { }

        public override int SaveChanges() => throw new InvalidOperationException("The dry run must not write.");
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The dry run must not write.");
    }
}
