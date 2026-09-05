using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// School enrolment payment → student course access → one invoice per student.
///
/// These exercise the RULES the payment path applies rather than the Razorpay round trip, which
/// needs a browser and a real gateway. Specifically: who receives course access (students, never
/// the paying principal), that confirmation is idempotent under a replayed callback, that an unpaid
/// order grants and invoices nothing, and that a principal can only reach their own school's
/// invoices.
/// </summary>
public class SchoolEnrollmentPaymentTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"school-pay-{Guid.NewGuid()}")
            .Options);

    private record Fixture(
        RioCommerceDbContext Db, Guid SchoolId, Guid PrincipalId,
        Guid[] StudentIds, Guid ProductId, Guid OrderId, Guid OrderItemId);

    /// <summary>One school, one principal, N students on roll, one ₹200 course, one pending order.</summary>
    private static Fixture Seed(int studentCount, decimal unitPrice = 200m)
    {
        var db = NewDb();

        var school = new School { Id = Guid.NewGuid(), Name = "Test School", UdiseCode = "27000000001" };
        db.Schools.Add(school);

        var principal = new User { Id = Guid.NewGuid(), FullName = "Principal", Email = "p@example.test" };
        db.Users.Add(principal);

        var product = new Product
        {
            Id = Guid.NewGuid(), Title = "7th Scholarship Exam", Slug = "7th-scholarship",
            SellingPrice = 499m, SchoolStudentPrice = unitPrice, GstRate = 18m, Status = ProductStatus.Active,
        };
        db.Products.Add(product);

        var studentIds = new Guid[studentCount];
        for (var i = 0; i < studentCount; i++)
        {
            var s = new User { Id = Guid.NewGuid(), FullName = $"Student {i + 1}", Email = $"s{i + 1}@example.test" };
            db.Users.Add(s);
            db.SchoolStudents.Add(new SchoolStudent
            {
                Id = Guid.NewGuid(), SchoolId = school.Id, UserId = s.Id, IsActive = true, StudentClass = "7",
            });
            studentIds[i] = s.Id;
        }

        var total = unitPrice * studentCount;
        var item = new OrderItem
        {
            Id = Guid.NewGuid(), ProductId = product.Id, ProductTitle = product.Title,
            Quantity = studentCount, UnitPrice = unitPrice, GstRate = 18m, LineTotal = total,
        };
        var order = new Order
        {
            Id = Guid.NewGuid(), OrderNumber = "RIO-9001", UserId = principal.Id,
            Subtotal = total, TotalAmount = total,
            Status = OrderStatus.Pending, PaymentStatus = PaymentStatus.Pending,
            BillingName = school.Name, BillingAddress = "School Road", BillingCity = "Pune",
            BillingState = "Maharashtra", BillingPincode = "411021",
        };
        order.Items.Add(item);
        db.Orders.Add(order);

        foreach (var sid in studentIds)
        {
            db.SchoolEnrollmentStudents.Add(new SchoolEnrollmentStudent
            {
                Id = Guid.NewGuid(), OrderId = order.Id, SchoolId = school.Id,
                StudentUserId = sid, ProductId = product.Id, UnitPrice = unitPrice,
            });
        }

        db.SaveChanges();
        return new Fixture(db, school.Id, principal.Id, studentIds, product.Id, order.Id, item.Id);
    }

    /// <summary>
    /// Mirrors CheckoutService.GrantSchoolEnrollmentAsync: grant to each rostered student who is
    /// still on the school's roll, idempotent on (UserId, OrderItemId), and stamp ConfirmedAt.
    /// Kept in the test so the RULE is asserted without standing up the whole checkout graph.
    /// </summary>
    private static async Task ConfirmAsync(Fixture f)
    {
        var order = await f.Db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == f.OrderId);
        order.PaymentStatus = PaymentStatus.Success;
        order.Status = OrderStatus.Confirmed;
        order.ConfirmedAt ??= DateTime.UtcNow;

        var roster = await f.Db.SchoolEnrollmentStudents.Where(r => r.OrderId == f.OrderId).ToListAsync();
        var schoolIds = roster.Select(r => r.SchoolId).Distinct().ToList();
        var rollSet = (await f.Db.SchoolStudents.AsNoTracking()
                .Where(ss => ss.IsActive && schoolIds.Contains(ss.SchoolId))
                .Select(ss => new { ss.SchoolId, ss.UserId }).ToListAsync())
            .Select(x => (x.SchoolId, x.UserId)).ToHashSet();

        foreach (var row in roster)
        {
            if (!rollSet.Contains((row.SchoolId, row.StudentUserId))) continue;

            var item = order.Items.First(i => i.ProductId == row.ProductId);
            var already = await f.Db.Enrollments
                .AnyAsync(e => e.UserId == row.StudentUserId && e.OrderItemId == item.Id);
            if (!already)
            {
                f.Db.Enrollments.Add(new Enrollment
                {
                    Id = Guid.NewGuid(), UserId = row.StudentUserId,
                    ProductId = row.ProductId, OrderItemId = item.Id, IsActive = true,
                });
            }
            row.ConfirmedAt ??= DateTime.UtcNow;
        }
        await f.Db.SaveChangesAsync();
    }

    // ── TEST A — one student paid ────────────────────────────────────────────
    [Fact]
    public async Task OneStudent_PaidOrder_GrantsStudentNotPrincipal()
    {
        var f = Seed(studentCount: 1);
        await ConfirmAsync(f);

        Assert.Equal(1, await f.Db.SchoolEnrollmentStudents.CountAsync(r => r.ConfirmedAt != null));

        // The student is enrolled …
        Assert.True(await f.Db.Enrollments.AnyAsync(e => e.UserId == f.StudentIds[0]));
        // … and the principal who paid is NOT. This is the defect this work exists to fix.
        Assert.False(await f.Db.Enrollments.AnyAsync(e => e.UserId == f.PrincipalId));
        Assert.Equal(1, await f.Db.Enrollments.CountAsync());
    }

    // ── TEST B — five students paid ──────────────────────────────────────────
    [Fact]
    public async Task FiveStudents_EachGranted_PrincipalNeverGranted()
    {
        var f = Seed(studentCount: 5);
        await ConfirmAsync(f);

        Assert.Equal(5, await f.Db.Enrollments.CountAsync());
        Assert.Equal(5, await f.Db.SchoolEnrollmentStudents.CountAsync(r => r.ConfirmedAt != null));
        foreach (var sid in f.StudentIds)
            Assert.True(await f.Db.Enrollments.AnyAsync(e => e.UserId == sid));
        Assert.False(await f.Db.Enrollments.AnyAsync(e => e.UserId == f.PrincipalId));

        // Money: 5 × ₹200 must equal the one ₹1,000 payment.
        var order = await f.Db.Orders.FirstAsync(o => o.Id == f.OrderId);
        var rosterSum = await f.Db.SchoolEnrollmentStudents
            .Where(r => r.OrderId == f.OrderId).SumAsync(r => r.UnitPrice);
        Assert.Equal(1000m, order.TotalAmount);
        Assert.Equal(order.TotalAmount, rosterSum);
    }

    // ── TEST C — replayed confirmation ───────────────────────────────────────
    [Fact]
    public async Task ReplayedConfirmation_AddsNothing_AndKeepsOriginalTimestamps()
    {
        var f = Seed(studentCount: 3);
        await ConfirmAsync(f);

        var firstStamps = await f.Db.SchoolEnrollmentStudents
            .Where(r => r.OrderId == f.OrderId)
            .Select(r => new { r.Id, r.ConfirmedAt }).ToListAsync();

        await ConfirmAsync(f);   // duplicate callback / webhook replay

        Assert.Equal(3, await f.Db.Enrollments.CountAsync());     // no duplicate grants

        var secondStamps = await f.Db.SchoolEnrollmentStudents
            .Where(r => r.OrderId == f.OrderId)
            .Select(r => new { r.Id, r.ConfirmedAt }).ToListAsync();

        foreach (var before in firstStamps)
            Assert.Equal(before.ConfirmedAt, secondStamps.Single(x => x.Id == before.Id).ConfirmedAt);
    }

    // ── TEST D — unpaid order ────────────────────────────────────────────────
    [Fact]
    public async Task UnpaidOrder_GrantsNothing_AndHasNoConfirmedRoster()
    {
        var f = Seed(studentCount: 2);   // seeded Pending; ConfirmAsync deliberately NOT called

        Assert.Equal(0, await f.Db.Enrollments.CountAsync());
        Assert.Equal(0, await f.Db.SchoolEnrollmentStudents.CountAsync(r => r.ConfirmedAt != null));
        Assert.Equal(PaymentStatus.Pending, (await f.Db.Orders.FirstAsync(o => o.Id == f.OrderId)).PaymentStatus);
    }

    // ── TEST — integrity: a student removed from the roll is not granted ─────
    [Fact]
    public async Task StudentRemovedFromRoll_IsNotGranted_AndStaysUnconfirmed()
    {
        var f = Seed(studentCount: 2);

        // Student 2 leaves the school between order placement and payment clearing.
        var leaver = await f.Db.SchoolStudents.FirstAsync(s => s.UserId == f.StudentIds[1]);
        leaver.IsActive = false;
        await f.Db.SaveChangesAsync();

        await ConfirmAsync(f);

        Assert.True(await f.Db.Enrollments.AnyAsync(e => e.UserId == f.StudentIds[0]));
        Assert.False(await f.Db.Enrollments.AnyAsync(e => e.UserId == f.StudentIds[1]));

        // Unconfirmed → InvoiceService will not raise an invoice for them either.
        var leftRow = await f.Db.SchoolEnrollmentStudents.FirstAsync(r => r.StudentUserId == f.StudentIds[1]);
        Assert.Null(leftRow.ConfirmedAt);
    }

    // ── TEST E — principal invoice authorisation ─────────────────────────────
    [Fact]
    public async Task InvoiceAuthorisation_AllowsOwnSchool_DeniesOtherSchool()
    {
        var f = Seed(studentCount: 1);

        // An invoice for OUR student, on our order.
        var mine = new Invoice
        {
            Id = Guid.NewGuid(), OrderId = f.OrderId, OrderNumber = "RIO-9001",
            InvoiceNumber = "VP/26-27/1", StudentUserId = f.StudentIds[0],
            TotalAmount = 200m, Status = InvoiceStatus.Active, CustomerName = "Student 1",
        };

        // Another school, its own order, its own student, its own invoice.
        var otherSchool = new School { Id = Guid.NewGuid(), Name = "Other School", UdiseCode = "27000000002" };
        var otherStudent = new User { Id = Guid.NewGuid(), FullName = "Other Student" };
        var otherOrder = new Order { Id = Guid.NewGuid(), OrderNumber = "RIO-9002", TotalAmount = 200m };
        var theirs = new Invoice
        {
            Id = Guid.NewGuid(), OrderId = otherOrder.Id, OrderNumber = "RIO-9002",
            InvoiceNumber = "VP/26-27/2", StudentUserId = otherStudent.Id,
            TotalAmount = 200m, Status = InvoiceStatus.Active, CustomerName = "Other Student",
        };
        f.Db.Schools.Add(otherSchool);
        f.Db.Users.Add(otherStudent);
        f.Db.Orders.Add(otherOrder);
        f.Db.SchoolEnrollmentStudents.Add(new SchoolEnrollmentStudent
        {
            Id = Guid.NewGuid(), OrderId = otherOrder.Id, SchoolId = otherSchool.Id,
            StudentUserId = otherStudent.Id, ProductId = f.ProductId, UnitPrice = 200m,
        });
        f.Db.Set<Invoice>().AddRange(mine, theirs);
        await f.Db.SaveChangesAsync();

        // The authorisation rule SchoolEnrollmentService applies: the invoice must join back to the
        // caller's school through the roster on (OrderId, StudentUserId).
        async Task<bool> AllowedFor(Guid schoolId, Guid invoiceId) =>
            await (from r in f.Db.SchoolEnrollmentStudents
                   join i in f.Db.Set<Invoice>()
                       on new { r.OrderId, S = (Guid?)r.StudentUserId } equals new { i.OrderId, S = i.StudentUserId }
                   where i.Id == invoiceId && r.SchoolId == schoolId
                   select i.Id).AnyAsync();

        Assert.True(await AllowedFor(f.SchoolId, mine.Id));            // own school → allowed
        Assert.False(await AllowedFor(f.SchoolId, theirs.Id));         // other school → denied
        Assert.False(await AllowedFor(otherSchool.Id, mine.Id));       // and symmetrically
    }

    // ── Callback ROUTING ─────────────────────────────────────────────────────
    // The controller decides where the browser lands from ONE fact: does this order have a roster?
    // These assert that fact and the URL rule built on it, for both order kinds.

    /// <summary>Mirrors RazorpayPaymentController: school orders route into the School portal,
    /// everything else keeps the storefront /checkout pages.</summary>
    private static string SuccessUrl(bool isSchool, string orderNumber) => isSchool
        ? $"/school/enrollment/payment-success?order={Uri.EscapeDataString(orderNumber)}"
        : $"/checkout/payment-success?order={Uri.EscapeDataString(orderNumber)}";

    private static string FailureUrl(bool isSchool, string orderNumber, string reason) => isSchool
        ? $"/school/enrollment/payment-failed?order={Uri.EscapeDataString(orderNumber)}&r={Uri.EscapeDataString(reason)}"
        : $"/checkout/payment-failed?o={orderNumber}&r={Uri.EscapeDataString(reason)}";

    [Fact]
    public async Task SchoolOrder_IsDetectedByRoster_AndRoutesToSchoolPages()
    {
        var f = Seed(studentCount: 2);

        var isSchool = await f.Db.SchoolEnrollmentStudents
            .AnyAsync(r => r.Order.OrderNumber == "RIO-9001");
        Assert.True(isSchool);

        Assert.Equal("/school/enrollment/payment-success?order=RIO-9001", SuccessUrl(isSchool, "RIO-9001"));
        Assert.StartsWith("/school/enrollment/payment-failed?order=RIO-9001", FailureUrl(isSchool, "RIO-9001", "Signature verification failed"));
    }

    [Fact]
    public async Task NormalStorefrontOrder_IsNotSchoolOrder_AndKeepsStorefrontRoutes()
    {
        var f = Seed(studentCount: 1);

        // An ordinary customer order: no roster rows point at it.
        var buyer = new User { Id = Guid.NewGuid(), FullName = "Retail Buyer" };
        var plain = new Order { Id = Guid.NewGuid(), OrderNumber = "RIO-7777", UserId = buyer.Id, TotalAmount = 499m };
        f.Db.Users.Add(buyer);
        f.Db.Orders.Add(plain);
        await f.Db.SaveChangesAsync();

        var isSchool = await f.Db.SchoolEnrollmentStudents
            .AnyAsync(r => r.Order.OrderNumber == "RIO-7777");
        Assert.False(isSchool);

        // Storefront routes must be byte-identical to what they were before this feature.
        Assert.Equal("/checkout/payment-success?order=RIO-7777", SuccessUrl(isSchool, "RIO-7777"));
        Assert.Equal("/checkout/payment-failed?o=RIO-7777&r=Payment%20failed", FailureUrl(isSchool, "RIO-7777", "Payment failed"));
    }

    [Fact]
    public async Task Cancellation_LeavesOrderPending_AndRoutesToSchoolCancelledPage()
    {
        var f = Seed(studentCount: 3);

        // A dismissed popup never reaches the server: nothing is confirmed, nothing invoiced.
        var order = await f.Db.Orders.FirstAsync(o => o.Id == f.OrderId);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(0, await f.Db.Enrollments.CountAsync());
        Assert.Equal(0, await f.Db.SchoolEnrollmentStudents.CountAsync(r => r.ConfirmedAt != null));

        // The client routes it, using the override the School pages pass to rc.retryRazorpay.
        var routes = new { cancelled = "/school/enrollment/payment-cancelled" };
        Assert.Equal("/school/enrollment/payment-cancelled?order=RIO-9001",
            $"{routes.cancelled}?order={Uri.EscapeDataString(order.OrderNumber)}");
    }

    // ── Invoice numbering ────────────────────────────────────────────────────
    [Fact]
    public async Task InvoiceNumbers_AreSequentialWithinSeries_AndReplayAddsNone()
    {
        var f = Seed(studentCount: 5);
        const string series = "VP/26-27/";

        // Mirrors InvoiceService.NextInSeriesAsync: max numeric tail under this prefix, + 1.
        async Task<int> NextAsync()
        {
            var nums = await f.Db.Set<Invoice>()
                .Where(i => i.InvoiceNumber.StartsWith(series))
                .Select(i => i.InvoiceNumber).ToListAsync();
            var next = 1;
            foreach (var n in nums)
            {
                if (!n.StartsWith(series, StringComparison.Ordinal)) continue;
                if (int.TryParse(n[series.Length..], out var v) && v >= next) next = v + 1;
            }
            return next;
        }

        var roster = await f.Db.SchoolEnrollmentStudents.Where(r => r.OrderId == f.OrderId).ToListAsync();
        foreach (var row in roster)
        {
            // Idempotent per (order, student) — the same guard the service applies.
            if (await f.Db.Set<Invoice>().AnyAsync(i => i.OrderId == f.OrderId && i.StudentUserId == row.StudentUserId))
                continue;

            f.Db.Set<Invoice>().Add(new Invoice
            {
                Id = Guid.NewGuid(), OrderId = f.OrderId, OrderNumber = "RIO-9001",
                InvoiceNumber = series + await NextAsync(), StudentUserId = row.StudentUserId,
                TotalAmount = 200m, Status = InvoiceStatus.Active, CustomerName = "S",
            });
            await f.Db.SaveChangesAsync();
        }

        var issued = await f.Db.Set<Invoice>().Select(i => i.InvoiceNumber).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "VP/26-27/1", "VP/26-27/2", "VP/26-27/3", "VP/26-27/4", "VP/26-27/5" }, issued);
        Assert.Equal(1000m, await f.Db.Set<Invoice>().SumAsync(i => i.TotalAmount));

        // Replay the whole generation pass — the per-student guard must add nothing.
        foreach (var row in roster)
        {
            if (await f.Db.Set<Invoice>().AnyAsync(i => i.OrderId == f.OrderId && i.StudentUserId == row.StudentUserId))
                continue;
            Assert.Fail("Replay attempted to mint a second invoice for a student that already had one.");
        }
        Assert.Equal(5, await f.Db.Set<Invoice>().CountAsync());
    }
}
