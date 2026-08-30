using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The one-off repair for the 20 franchise orders that were written before the order paths linked a
/// customer. Its whole job is two columns — a customer account per student, and <c>Order.UserId</c> —
/// and these tests exist as much to hold the line on what it must NOT do.
///
/// <para>The rule that earns the most attention here is the repeat student. Four of the twenty orders
/// belong to students who ordered more than once (Baani Waradkar three times), so a backfill that
/// resolves each order independently would hand the same person two or three accounts. Every path
/// through this service therefore has to settle a phone ONCE per pass.</para>
/// </summary>
public class FranchiseCustomerBackfillTests
{
    private static DbContextOptions<RioCommerceDbContext> Options(string name) =>
        new DbContextOptionsBuilder<RioCommerceDbContext>().UseInMemoryDatabase(name).Options;

    private static RioCommerceDbContext NewDb(string? name = null) => new(Options(name ?? $"backfill-{Guid.NewGuid()}"));

    private static FranchiseCustomerBackfillService Service(RioCommerceDbContext db, IStudentAccountProvisioner? students = null) =>
        new(db,
            students ?? new StudentAccountProvisioner(db, NullLogger<StudentAccountProvisioner>.Instance),
            NullLogger<FranchiseCustomerBackfillService>.Instance);

    /// <summary>An unlinked franchise order, the shape the backfill is aimed at.</summary>
    private static Order Order(RioCommerceDbContext db, string number, string name, string phone, string? email = null, Guid? userId = null)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = number, UserId = userId,
            StudentName = name, StudentPhone = phone, StudentEmail = email,
            Source = OrderSource.Franchisee, FranchiseId = Guid.NewGuid(),
            Subtotal = 5000m, TotalAmount = 5000m, FranchiseNetPayable = 5000m,
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            PaymentMode = PaymentMode.BankTransfer
        };
        db.Orders.Add(order);
        return order;
    }

    private static User User(string name, string phone, string? email = null, string? comment = null) => new()
    {
        Id = Guid.NewGuid(), FullName = name, Phone = phone, Email = email,
        PasswordHash = "existing-hash", IsActive = true, IsVerified = true, AdminComment = comment
    };

    private static void SeedStudentRole(RioCommerceDbContext db) =>
        db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "student", DisplayName = "Student", IsActive = true });

    private static StudentLinkBackfillRow Row(StudentLinkBackfillReport r, string orderNumber) =>
        r.Rows.First(x => x.OrderNumber == orderNumber);

    // ── 1. Existing customer → link, nothing created ──────────────────────────
    [Fact]
    public async Task An_order_whose_student_already_has_an_account_is_linked_to_it()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        var existing = User("Pratham Jain", "7976745154", "jainpratham2357@gmail.com");
        db.Users.Add(existing);
        Order(db, "FRN-1021", "Pratham Jain", "7976745154", "jainpratham2357@gmail.com");
        await db.SaveChangesAsync();

        var report = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(1, report.LinkExisting);
        Assert.Equal(0, report.CreateNew);
        Assert.Equal(existing.Id, (await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1021")).UserId);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    // ── 2. New student → account created and linked ───────────────────────────
    [Fact]
    public async Task An_order_whose_student_has_no_account_gets_one_created()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        Order(db, "FRN-1004", "Prem Kumar Ramanand Gupta", "8108236435", "guptapremkumar8108@gmail.com");
        await db.SaveChangesAsync();

        var report = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(1, report.CreateNew);
        Assert.Equal(1, report.UsersCreated);
        Assert.Equal(1, report.OrdersLinked);

        var order = await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1004");
        Assert.NotNull(order.UserId);

        var user = await db.Users.FirstAsync(u => u.Id == order.UserId);
        Assert.Equal("Prem Kumar Ramanand Gupta", user.FullName);
        Assert.Equal("8108236435", user.Phone);
        Assert.Equal("guptapremkumar8108@gmail.com", user.Email);
        Assert.True(user.IsActive);
        Assert.False(user.IsVerified);
        Assert.Null(user.PasswordHash);
        Assert.Equal("Created from Franchise Order FRN-1004", user.AdminComment);
        Assert.True(await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.IsActive));
    }

    // ── 3. Same student on two orders → ONE account ───────────────────────────
    // Madhavi Dnyneshwar Gadhave (FRN-1008 / FRN-1009), Prachi Vaghela, Nitish Bhardwaj.
    [Fact]
    public async Task A_student_on_two_orders_gets_exactly_one_account()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        Order(db, "FRN-1008", "Madhavi Dnyneshwar Gadhave", "7083281835", "gadhavemadhavi064@gmail.com");
        Order(db, "FRN-1009", "Madhavi Dnyneshwar Gadhave", "7083281835", "gadhavemadhavi064@gmail.com");
        await db.SaveChangesAsync();

        var report = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(1, report.CreateNew);              // ONE user…
        Assert.Equal(1, report.LinkedToSameStudent);    // …and the second order links to it
        Assert.Equal(2, report.OrdersLinked);
        Assert.Equal(1, await db.Users.CountAsync());

        var first = await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1008");
        var second = await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1009");
        Assert.NotNull(first.UserId);
        Assert.Equal(first.UserId, second.UserId);

        // Provenance cites the EARLIEST order, and only that one.
        Assert.Equal("Created from Franchise Order FRN-1008",
            (await db.Users.FirstAsync(u => u.Id == first.UserId)).AdminComment);
    }

    // ── 4. Same student on three orders → still ONE account ───────────────────
    // Baani Waradkar's shape, but with no pre-existing account so creation is in play.
    [Fact]
    public async Task A_student_on_three_orders_still_gets_exactly_one_account()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        Order(db, "FRN-1005", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
        Order(db, "FRN-1006", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
        Order(db, "FRN-1007", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
        await db.SaveChangesAsync();

        var report = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(1, report.CreateNew);
        Assert.Equal(2, report.LinkedToSameStudent);
        Assert.Equal(3, report.OrdersLinked);
        Assert.Equal(1, await db.Users.CountAsync());

        var ids = await db.Orders.Select(o => o.UserId).Distinct().ToListAsync();
        Assert.Single(ids);
        Assert.NotNull(ids[0]);
    }

    // ── 5. Email-only match MUST NOT link ─────────────────────────────────────
    [Fact]
    public async Task A_matching_email_on_a_different_phone_never_links()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        var sibling = User("Aarav Mehta", "9833333333", "family@example.com");
        db.Users.Add(sibling);
        Order(db, "FRN-1030", "Anaya Mehta", "9844444444", "family@example.com");
        await db.SaveChangesAsync();

        var report = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(1, report.CreateNew);
        Assert.Equal(0, report.LinkExisting);

        var order = await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1030");
        Assert.NotEqual(sibling.Id, order.UserId);
        Assert.Equal(2, await db.Users.CountAsync());

        // The sibling keeps the address; the new account simply goes without one.
        Assert.Equal("family@example.com", (await db.Users.FirstAsync(u => u.Id == sibling.Id)).Email);
        Assert.Null((await db.Users.FirstAsync(u => u.Id == order.UserId)).Email);
    }

    // ── 6. An existing customer's profile is untouched ────────────────────────
    [Fact]
    public async Task Linking_never_edits_the_customer_it_links_to()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        var existing = User("ABC", "9811111111", "old@example.com", "VIP customer");
        db.Users.Add(existing);
        Order(db, "FRN-1031", "XYZ", "9811111111", "new@example.com");
        await db.SaveChangesAsync();

        await Service(db).RunAsync(dryRun: false);

        var after = await db.Users.AsNoTracking().FirstAsync(u => u.Id == existing.Id);
        Assert.Equal("ABC", after.FullName);
        Assert.Equal("old@example.com", after.Email);
        Assert.Equal("9811111111", after.Phone);
        Assert.Equal("existing-hash", after.PasswordHash);
        Assert.True(after.IsActive);
        Assert.True(after.IsVerified);
        Assert.Equal("VIP customer", after.AdminComment);   // NOT appended to on a link
    }

    // ── 7. AdminComment is appended safely, never overwritten ─────────────────
    [Fact]
    public async Task Provenance_never_overwrites_an_existing_note()
    {
        // A link leaves the note alone entirely (covered above); this pins the append rule itself,
        // including the idempotence a second pass depends on.
        Assert.Equal("VIP customer\nCreated from Franchise Order FRN-1008",
            StudentAccountProvisioner.AppendComment("VIP customer", "Created from Franchise Order FRN-1008"));
        Assert.Equal("Created from Franchise Order FRN-1008",
            StudentAccountProvisioner.AppendComment("Created from Franchise Order FRN-1008", "Created from Franchise Order FRN-1008"));

        using var db = NewDb();
        SeedStudentRole(db);
        Order(db, "FRN-1032", "Sukriti Nagar", "8127420480", "sukritinagar24@gmail.com");
        await db.SaveChangesAsync();

        await Service(db).RunAsync(dryRun: false);
        var user = await db.Users.FirstAsync();
        Assert.Equal("Created from Franchise Order FRN-1032", user.AdminComment);
    }

    // ── 8. A second pass is a no-op ───────────────────────────────────────────
    [Fact]
    public async Task Running_the_backfill_twice_changes_nothing_the_second_time()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        var known = User("Baani Waradkar", "9869260695", "baani7090@gmail.com");
        db.Users.Add(known);
        Order(db, "FRN-1005", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
        Order(db, "FRN-1006", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
        Order(db, "FRN-1015", "Prachi Vaghela", "8866516053", "vaghelaprachi8@gmail.com");
        Order(db, "FRN-1016", "Prachi Vaghela", "8866516053", "vaghelaprachi8@gmail.com");
        await db.SaveChangesAsync();

        var first = await Service(db).RunAsync(dryRun: false);
        Assert.Equal(4, first.OrdersLinked);
        Assert.Equal(1, first.CreateNew);

        var usersAfterFirst = await db.Users.AsNoTracking().OrderBy(u => u.Phone).ToListAsync();
        var linksAfterFirst = await db.Orders.AsNoTracking().OrderBy(o => o.OrderNumber)
            .Select(o => new { o.OrderNumber, o.UserId }).ToListAsync();

        var second = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(4, second.NoAction);
        Assert.Equal(0, second.CreateNew);
        Assert.Equal(0, second.LinkExisting);
        Assert.Equal(0, second.LinkedToSameStudent);
        Assert.Equal(0, second.OrdersLinked);

        // Nothing moved: same accounts, same notes, same links.
        var usersAfterSecond = await db.Users.AsNoTracking().OrderBy(u => u.Phone).ToListAsync();
        Assert.Equal(usersAfterFirst.Count, usersAfterSecond.Count);
        Assert.Equal(usersAfterFirst.Select(u => u.Id), usersAfterSecond.Select(u => u.Id));
        Assert.Equal(usersAfterFirst.Select(u => u.AdminComment), usersAfterSecond.Select(u => u.AdminComment));
        Assert.Equal(linksAfterFirst.Select(o => o.UserId),
            (await db.Orders.AsNoTracking().OrderBy(o => o.OrderNumber).Select(o => o.UserId).ToListAsync()));
    }

    // ── 9. Dry run writes nothing at all ──────────────────────────────────────
    [Fact]
    public async Task The_dry_run_writes_nothing()
    {
        var name = $"dry-{Guid.NewGuid()}";
        using (var seed = NewDb(name))
        {
            SeedStudentRole(seed);
            seed.Users.Add(User("Baani Waradkar", "9869260695", "baani7090@gmail.com"));
            Order(seed, "FRN-1005", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
            Order(seed, "FRN-1006", "Baani Waradkar", "9869260695", "baani7090@gmail.com");
            Order(seed, "FRN-1008", "Madhavi Dnyneshwar Gadhave", "7083281835", "gadhavemadhavi064@gmail.com");
            Order(seed, "FRN-1009", "Madhavi Dnyneshwar Gadhave", "7083281835", "gadhavemadhavi064@gmail.com");
            Order(seed, "FRN-1013", "PUNCH DEV YADAV", "6290958973", "punchdevyadav2000@gmail.com");
            Order(seed, "FRN-1099", "Unreachable Student", "12345", "short@example.com");
            var linked = Order(seed, "RIO-1073", "Aujus Aggarwal", "8708600443", "aggarwalaujus@gmail.com");
            linked.UserId = Guid.NewGuid();
            await seed.SaveChangesAsync();
        }

        StudentLinkBackfillReport report;
        using (var db = new ReadOnlyDbContext(Options(name)))
        {
            report = await Service(db).RunAsync(dryRun: true);
            Assert.Empty(db.ChangeTracker.Entries());
        }

        Assert.True(report.DryRun);
        Assert.Equal(7, report.OrdersScanned);
        Assert.Equal(2, report.LinkExisting);            // FRN-1005, FRN-1006 → the account that exists
        Assert.Equal(2, report.CreateNew);               // Madhavi + Punch Dev Yadav
        Assert.Equal(1, report.LinkedToSameStudent);     // FRN-1009 → the account FRN-1008 would create
        Assert.Equal(1, report.NoAction);                // RIO-1073
        Assert.Equal(1, report.ReviewRequired);          // FRN-1099, five-digit phone
        Assert.Equal(0, report.Failed);
        Assert.Equal(5, report.OrdersLinked);

        Assert.Equal(StudentLinkAction.LinkExisting, Row(report, "FRN-1006").Action);
        Assert.Equal(StudentLinkAction.CreateNew, Row(report, "FRN-1008").Action);
        Assert.Equal("Created from Franchise Order FRN-1008", Row(report, "FRN-1008").ProposedAdminComment);
        Assert.Equal(StudentLinkAction.LinkExisting, Row(report, "FRN-1009").Action);
        Assert.Null(Row(report, "FRN-1009").ProposedAdminComment);   // the 2nd order creates nothing
        Assert.Equal(StudentLinkAction.ReviewRequired, Row(report, "FRN-1099").Action);

        // And the database is byte-for-byte where it was left.
        using var after = NewDb(name);
        Assert.Equal(1, await after.Users.CountAsync());
        Assert.Equal(6, await after.Orders.CountAsync(o => o.UserId == null));
        Assert.Equal(0, await after.Enrollments.CountAsync());
    }

    // ── 10. The unique phone index race ───────────────────────────────────────
    [Fact]
    public async Task A_lost_race_on_the_phone_index_links_the_winner_rather_than_duplicating()
    {
        using var db = NewDb();
        var winner = User("Prachi Vaghela", "8866516053", "vaghelaprachi8@gmail.com");

        // The provisioner is the component that owns race recovery; here it reports the outcome the
        // real one produces after a 23505 — an existing account, not a new one.
        var students = new Mock<IStudentAccountProvisioner>();
        students.Setup(p => p.FindCustomerByPhoneAsync("8866516053", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StudentPhoneMatch(true, null, "8866516053"));
        students.Setup(p => p.ResolveOrCreateAsync(It.IsAny<StudentAccountRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StudentAccountResult(winner.Id, StudentAccountOutcome.LinkedExisting,
                    "Recovered from a duplicate-key race."));

        db.Users.Add(winner);
        Order(db, "FRN-1015", "Prachi Vaghela", "8866516053", "vaghelaprachi8@gmail.com");
        Order(db, "FRN-1016", "Prachi Vaghela", "8866516053", "vaghelaprachi8@gmail.com");
        await db.SaveChangesAsync();

        var report = await Service(db, students.Object).RunAsync(dryRun: false);

        Assert.Equal(0, report.CreateNew);            // the race winner is NOT counted as our creation
        // Both orders count as plain links: the account the race handed back already existed, so
        // nothing was created in this pass for the second order to attach to.
        Assert.Equal(2, report.LinkExisting);
        Assert.Equal(0, report.LinkedToSameStudent);
        Assert.Equal(2, report.OrdersLinked);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.All(await db.Orders.Select(o => o.UserId).ToListAsync(), id => Assert.Equal(winner.Id, id));
    }

    // ── 11. One bad record does not corrupt its neighbours ────────────────────
    [Fact]
    public async Task A_single_failing_order_leaves_every_other_order_correct()
    {
        using var db = NewDb();
        SeedStudentRole(db);

        var real = new StudentAccountProvisioner(db, NullLogger<StudentAccountProvisioner>.Instance);
        var students = new Mock<IStudentAccountProvisioner>();
        students.Setup(p => p.FindCustomerByPhoneAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string? phone, CancellationToken ct) => real.FindCustomerByPhoneAsync(phone, ct));
        students.Setup(p => p.ResolveOrCreateAsync(It.IsAny<StudentAccountRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StudentAccountRequest r, CancellationToken ct) => r.Phone == "9321412209"
                    ? Task.FromResult(StudentAccountResult.Failed("customers table is unavailable"))
                    : real.ResolveOrCreateAsync(r, ct));

        Order(db, "FRN-1010", "Sukriti Nagar", "8127420480", "sukritinagar24@gmail.com");
        Order(db, "FRN-1011", "VIDISHA DONGRE", "9321412209", "dongrevidisha@gmail.com");   // this one fails
        Order(db, "FRN-1012", "DHWANI MISTRY", "9998066031", "dhwanimistry03@gmail.com");
        await db.SaveChangesAsync();

        var report = await Service(db, students.Object).RunAsync(dryRun: false);

        Assert.Equal(1, report.Failed);
        Assert.Equal(2, report.CreateNew);
        Assert.Equal(2, report.OrdersLinked);

        // The failure is reported, not swallowed…
        var failedRow = Row(report, "FRN-1011");
        Assert.Equal(StudentLinkAction.ReviewRequired, failedRow.Action);
        Assert.Equal("customers table is unavailable", failedRow.Error);
        Assert.Null(failedRow.UserId);

        // …its order is left exactly as it was, and its neighbours completed normally.
        Assert.Null((await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1011")).UserId);
        Assert.NotNull((await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1010")).UserId);
        Assert.NotNull((await db.Orders.FirstAsync(o => o.OrderNumber == "FRN-1012")).UserId);
        Assert.Equal(2, await db.Users.CountAsync());
    }

    // ── 12. An already-linked order is left alone ─────────────────────────────
    [Fact]
    public async Task An_already_linked_order_is_reported_as_no_action()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        var owner = User("Aujus Aggarwal", "8708600443", "aggarwalaujus@gmail.com");
        db.Users.Add(owner);
        Order(db, "RIO-1073", "Aujus Aggarwal", "8708600443", "aggarwalaujus@gmail.com", userId: owner.Id);
        await db.SaveChangesAsync();

        var report = await Service(db).RunAsync(dryRun: false);

        Assert.Equal(1, report.NoAction);
        Assert.Equal(0, report.OrdersLinked);
        Assert.Equal(0, report.CreateNew);
        Assert.Equal(StudentLinkAction.NoAction, Row(report, "RIO-1073").Action);
        Assert.Equal(owner.Id, (await db.Orders.FirstAsync()).UserId);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    // ── The backfill stays in its lane ────────────────────────────────────────
    [Fact]
    public async Task The_backfill_touches_nothing_beyond_the_customer_and_the_link()
    {
        using var db = NewDb();
        SeedStudentRole(db);
        var order = Order(db, "FRN-1017", "Saurabh Shejol", "9623486839", "saurabhshejol12@gmail.com");
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductTitle = "CA Final FR", Quantity = 1, UnitPrice = 5000m, LineTotal = 5000m });
        await db.SaveChangesAsync();

        await Service(db).RunAsync(dryRun: false);

        var after = await db.Orders.AsNoTracking().Include(o => o.Items).FirstAsync();
        Assert.NotNull(after.UserId);                                   // the one thing it may change
        Assert.Equal(PaymentStatus.Success, after.PaymentStatus);       // payments untouched
        Assert.Equal(OrderStatus.Confirmed, after.Status);
        Assert.Equal(5000m, after.TotalAmount);
        Assert.Null(after.ActivatedAt);                                 // no enrollment activation
        Assert.All(after.Items, i => Assert.False(i.IsActivated));
        Assert.Equal(0, await db.Enrollments.CountAsync());             // no course access granted
        Assert.Equal(0, await db.Invoices.CountAsync());                // no invoices
        Assert.Equal(0, await db.SerialKeyRecords.CountAsync());        // no serial keys
    }

    /// <summary>Turns any attempted write into a failure, so "dry run" is enforced, not asserted.</summary>
    private sealed class ReadOnlyDbContext : RioCommerceDbContext
    {
        public ReadOnlyDbContext(DbContextOptions<RioCommerceDbContext> options) : base(options) { }

        public override int SaveChanges() => throw new InvalidOperationException("The dry run must not write.");
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The dry run must not write.");
    }
}
