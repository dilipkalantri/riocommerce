using RioCommerce.Core.DTOs.Franchise;
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
/// Approving a franchise provisions a login, and users are unique on Email and — separately — on
/// Phone. Franchisees usually already have an account here from buying as a student, so a clash is
/// the normal case, not the exceptional one.
///
/// <para>These tests pin the rule that matters: <b>an existing account is never silently reused,
/// replaced, duplicated or deleted</b>, and the screen can never be left with nothing to show. What is
/// asserted throughout is the state of the database afterwards — the student's password hash, roles,
/// orders and wallet — rather than only the return value.</para>
/// </summary>
public class FranchiseApprovalConflictTests
{
    private static readonly Guid FranchiseAdminRoleId = Guid.Parse("44444444-4444-4444-4444-444444444405");
    private static readonly Guid StudentRoleId = Guid.Parse("44444444-4444-4444-4444-444444444401");

    // ── Fixture ─────────────────────────────────────────────────────────────────────────────────

    private static RioCommerceDbContext NewDb()
    {
        var db = new RioCommerceDbContext(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"frapp-{Guid.NewGuid()}")
            .Options);
        db.Roles.Add(new Role { Id = FranchiseAdminRoleId, Name = "franchise_admin" });
        db.Roles.Add(new Role { Id = StudentRoleId, Name = "student" });
        db.SaveChanges();
        return db;
    }

    private static FranchiseService Svc(RioCommerceDbContext db, Mock<INotificationService>? notify = null) =>
        new(db,
            (notify ?? new Mock<INotificationService>()).Object,
            new Mock<IAuditService>().Object,
            new Mock<IPaymentGatewayFactory>().Object,
            new Mock<IVerificationService>().Object,
            new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new Mock<IFranchiseShareCalculator>().Object,
            new Mock<IInvoiceService>().Object);

    private static async Task<Franchise> SeedPendingFranchiseAsync(
        RioCommerceDbContext db, string email = "piyushgupta.aoc@gmail.com", string phone = "9784835519")
    {
        var f = new Franchise
        {
            Id = Guid.NewGuid(),
            Name = "SMART EDUCATION LEARNING",
            BusinessName = "SMART EDUCATION LEARNING",
            City = "ALWAR",
            State = "Rajasthan",
            ContactEmail = email,
            ContactPhone = phone,
            Status = FranchiseStatus.Pending,
        };
        db.Franchises.Add(f);
        await db.SaveChangesAsync();
        return f;
    }

    /// <summary>A student who already exists — with the history that must survive untouched.</summary>
    private static async Task<User> SeedStudentAsync(
        RioCommerceDbContext db, string? email, string? phone, string name = "piyush gupta")
    {
        var u = new User
        {
            Id = Guid.NewGuid(),
            FullName = name,
            Email = email,
            Phone = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("StudentsOwnPassword1!"),
            IsActive = true,
            IsVerified = true,
        };
        db.Users.Add(u);
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = u.Id, RoleId = StudentRoleId, IsActive = true });

        // The records that must not move: an order, and the wallet balance on that order's user.
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid(), OrderNumber = "RIO-8001", UserId = u.Id,
            StudentName = name, StudentPhone = phone ?? "", StudentEmail = email,
            Status = OrderStatus.Confirmed, PaymentStatus = PaymentStatus.Success,
            Subtotal = 1000m, TotalAmount = 1000m,
        });
        await db.SaveChangesAsync();
        return u;
    }

    private static FranchiseApprovalRequest Approval(Guid franchiseId) => new()
    {
        FranchiseId = franchiseId, Code = "", CreditLimit = 0, Remarks = "ok",
    };

    // ── TEST 1 — the clean path still works ─────────────────────────────────────────────────────

    [Fact]
    public async Task Test1_NoExistingAccount_ApprovesAndCreatesAFreshLogin()
    {
        using var db = NewDb();
        var f = await SeedPendingFranchiseAsync(db);

        var conflict = await Svc(db).CheckApprovalConflictAsync(f.Id);
        Assert.False(conflict.HasConflict);

        var (ok, error) = await Svc(db).ApproveAsync(Approval(f.Id), Guid.NewGuid());

        Assert.True(ok, error);
        var saved = await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id);
        Assert.Equal(FranchiseStatus.Approved, saved.Status);
        Assert.Equal("ALW", saved.Code);
        Assert.NotNull(saved.AdminUserId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "piyushgupta.aoc@gmail.com"));
    }

    // ── TEST 2 / 3 — the clash is reported, not acted on ────────────────────────────────────────

    [Fact]
    public async Task Test2_ExistingPhone_IsReportedAsAConflict()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);

        var conflict = await Svc(db).CheckApprovalConflictAsync(f.Id);

        Assert.Equal(FranchiseApprovalConflictKind.ExistingAccount, conflict.Kind);
        Assert.Equal(student.Id, conflict.PhoneOwner!.UserId);
        Assert.True(conflict.PhoneOwner.MatchedOnPhone);
        Assert.Equal("Student", conflict.PhoneOwner.AccountType);
        Assert.Null(conflict.EmailOwner);          // the email is free
        Assert.False(conflict.BlocksApproval);      // resolvable by the admin
    }

    [Fact]
    public async Task Test3_ExistingEmail_IsReportedAsAConflict()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "piyushgupta.aoc@gmail.com", "9000000001");
        var f = await SeedPendingFranchiseAsync(db);

        var conflict = await Svc(db).CheckApprovalConflictAsync(f.Id);

        Assert.Equal(FranchiseApprovalConflictKind.ExistingAccount, conflict.Kind);
        Assert.Equal(student.Id, conflict.EmailOwner!.UserId);
        Assert.True(conflict.EmailOwner.MatchedOnEmail);
    }

    // ── TEST 4 — one user holds both identifiers ────────────────────────────────────────────────

    [Fact]
    public async Task Test4_SameUserHoldsBoth_IsOneAccountNotTwo()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "piyushgupta.aoc@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);

        var conflict = await Svc(db).CheckApprovalConflictAsync(f.Id);

        Assert.Equal(FranchiseApprovalConflictKind.ExistingAccount, conflict.Kind);
        Assert.Same(conflict.EmailOwner, conflict.PhoneOwner);      // collapsed to a single card
        Assert.Equal(student.Id, conflict.EmailOwner!.UserId);
        Assert.True(conflict.EmailOwner.MatchedOnEmail && conflict.EmailOwner.MatchedOnPhone);
    }

    // ── TEST 5 — two different people ⇒ blocked outright ────────────────────────────────────────

    [Fact]
    public async Task Test5_PhoneAndEmailOnDifferentUsers_BlocksApprovalEntirely()
    {
        using var db = NewDb();
        await SeedStudentAsync(db, "someone.else@example.com", "9784835519", "Phone Holder");
        await SeedStudentAsync(db, "piyushgupta.aoc@gmail.com", "9000000002", "Email Holder");
        var f = await SeedPendingFranchiseAsync(db);

        var conflict = await Svc(db).CheckApprovalConflictAsync(f.Id);
        Assert.Equal(FranchiseApprovalConflictKind.MultipleAccounts, conflict.Kind);
        Assert.True(conflict.BlocksApproval);
        Assert.NotEqual(conflict.PhoneOwner!.UserId, conflict.EmailOwner!.UserId);

        // …and no resolution can force it through — the data has to change first.
        foreach (var resolution in new[]
                 {
                     FranchiseAccountResolution.UseExistingAccount,
                     FranchiseAccountResolution.ReplaceExistingContact,
                 })
        {
            var req = Approval(f.Id);
            req.AccountResolution = resolution;
            req.ExistingUserId = conflict.PhoneOwner.UserId;
            req.ConfirmContactReplacement = true;
            req.ReplaceEmail = true;

            var (ok, _) = await Svc(db).ApproveAsync(req, Guid.NewGuid());
            Assert.False(ok);
        }

        Assert.Equal(FranchiseStatus.Pending, (await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id)).Status);
    }

    // ── TEST 6 — a franchise already holds these details ────────────────────────────────────────

    [Fact]
    public async Task Test6_DuplicateFranchise_IsReportedAndBlocks()
    {
        using var db = NewDb();
        db.Franchises.Add(new Franchise
        {
            Id = Guid.NewGuid(), Name = "SMART EDUCATION (existing)", Code = "ALW",
            City = "ALWAR", ContactEmail = "piyushgupta.aoc@gmail.com", ContactPhone = "9784835519",
            Status = FranchiseStatus.Approved,
        });
        await db.SaveChangesAsync();
        var f = await SeedPendingFranchiseAsync(db);

        var conflict = await Svc(db).CheckApprovalConflictAsync(f.Id);
        Assert.Equal(FranchiseApprovalConflictKind.DuplicateFranchise, conflict.Kind);
        Assert.True(conflict.BlocksApproval);
        Assert.Equal("ALW", conflict.DuplicateFranchise!.Code);

        var (ok, error) = await Svc(db).ApproveAsync(Approval(f.Id), Guid.NewGuid());
        Assert.False(ok);
        Assert.Contains("Another franchise already uses", error);
        Assert.Equal(FranchiseStatus.Pending, (await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id)).Status);
    }

    // ── TEST 7 / 10 — reuse grants the role and changes nothing else ────────────────────────────

    [Fact]
    public async Task Test7And10_UseExistingAccount_AddsRole_AndLeavesPasswordAndContactUntouched()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var originalHash = student.PasswordHash;
        var f = await SeedPendingFranchiseAsync(db);
        var svc = Svc(db);

        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.UseExistingAccount;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;

        var (ok, error) = await svc.ApproveAsync(req, Guid.NewGuid());

        Assert.True(ok, error);
        var reloaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == student.Id);
        Assert.Equal(originalHash, reloaded.PasswordHash);                       // password untouched
        Assert.Equal("smarteducat.learning@gmail.com", reloaded.Email);          // email untouched
        Assert.Equal("9784835519", reloaded.Phone);                              // phone untouched

        // The franchise is linked to that same user — no second account for the same person.
        var saved = await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id);
        Assert.Equal(FranchiseStatus.Approved, saved.Status);
        Assert.Equal(student.Id, saved.AdminUserId);
        Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == "9784835519"));

        // Existing role kept, franchise_admin added alongside it.
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == student.Id && ur.IsActive)
            .Select(ur => ur.RoleId).ToListAsync();
        Assert.Contains(StudentRoleId, roleIds);
        Assert.Contains(FranchiseAdminRoleId, roleIds);
    }

    // ── TEST 8 — replacement touches only the ticked field ──────────────────────────────────────

    [Fact]
    public async Task Test8_ReplaceContact_ChangesOnlyTheFieldTicked()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var originalHash = student.PasswordHash;
        var f = await SeedPendingFranchiseAsync(db);
        var svc = Svc(db);

        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.ReplaceExistingContact;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;
        req.ConfirmContactReplacement = true;
        req.ReplaceEmail = true;      // email only — phone deliberately left alone
        req.ReplacePhone = false;

        var (ok, error) = await svc.ApproveAsync(req, Guid.NewGuid());

        Assert.True(ok, error);
        var reloaded = await db.Users.AsNoTracking().FirstAsync(u => u.Id == student.Id);
        Assert.Equal("piyushgupta.aoc@gmail.com", reloaded.Email);   // the ticked field changed
        Assert.Equal("9784835519", reloaded.Phone);                  // the unticked one did not
        Assert.Equal(originalHash, reloaded.PasswordHash);           // password still never touched
    }

    [Fact]
    public async Task ReplaceContact_WithoutTheConfirmationTick_IsRefused()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);
        var svc = Svc(db);

        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.ReplaceExistingContact;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;
        req.ConfirmContactReplacement = false;      // the tick the admin never gave
        req.ReplaceEmail = true;

        var (ok, error) = await svc.ApproveAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("explicit confirmation", error);
        Assert.Equal("smarteducat.learning@gmail.com",
            (await db.Users.AsNoTracking().FirstAsync(u => u.Id == student.Id)).Email);
    }

    [Fact]
    public async Task ReplaceContact_WithNoFieldSelected_IsRefused()
    {
        using var db = NewDb();
        await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);
        var svc = Svc(db);

        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.ReplaceExistingContact;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;
        req.ConfirmContactReplacement = true;
        req.ReplaceEmail = false; req.ReplacePhone = false;

        var (ok, error) = await svc.ApproveAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("Select which contact detail", error);
    }

    // ── TEST 9 / 16 — nothing happens without an explicit decision ──────────────────────────────

    [Fact]
    public async Task Test9And16_ConflictWithNoDecision_IsRefused_AndWritesNothing()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var originalHash = student.PasswordHash;
        var f = await SeedPendingFranchiseAsync(db);

        // Exactly what the old code did implicitly — approve without ever seeing the conflict.
        var (ok, error) = await Svc(db).ApproveAsync(Approval(f.Id), Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("existing account", error, StringComparison.OrdinalIgnoreCase);

        // Full rollback semantics: the franchise is untouched and no login was provisioned.
        var saved = await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id);
        Assert.Equal(FranchiseStatus.Pending, saved.Status);
        Assert.Null(saved.AdminUserId);
        Assert.Null(saved.ApprovedAt);
        Assert.Equal(1, await db.Users.CountAsync());                       // no second user
        Assert.Equal(originalHash, (await db.Users.AsNoTracking().FirstAsync()).PasswordHash);
        Assert.DoesNotContain(FranchiseAdminRoleId,
            await db.UserRoles.Where(ur => ur.UserId == student.Id).Select(ur => ur.RoleId).ToListAsync());
    }

    [Fact]
    public async Task AStaleScreenPointingAtTheWrongUser_IsRefused()
    {
        using var db = NewDb();
        await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);

        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.UseExistingAccount;
        req.ExistingUserId = Guid.NewGuid();     // not the account the conflict actually found

        var (ok, error) = await Svc(db).ApproveAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("changed since the conflict was shown", error);
        Assert.Equal(FranchiseStatus.Pending, (await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id)).Status);
    }

    // ── TEST 11-14 — the student's own records survive reuse ────────────────────────────────────

    [Fact]
    public async Task Test11To14_ReusingAnAccount_LeavesOrdersAndCustomerRecordsAlone()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var ordersBefore = await db.Orders.AsNoTracking().Where(o => o.UserId == student.Id).ToListAsync();
        var f = await SeedPendingFranchiseAsync(db);
        var svc = Svc(db);

        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.UseExistingAccount;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;
        Assert.True((await svc.ApproveAsync(req, Guid.NewGuid())).ok);

        var ordersAfter = await db.Orders.AsNoTracking().Where(o => o.UserId == student.Id).ToListAsync();
        Assert.Equal(ordersBefore.Count, ordersAfter.Count);
        Assert.Equal(ordersBefore.Select(o => o.OrderNumber).OrderBy(x => x),
                     ordersAfter.Select(o => o.OrderNumber).OrderBy(x => x));
        Assert.Equal(ordersBefore.Sum(o => o.TotalAmount), ordersAfter.Sum(o => o.TotalAmount));

        // The user still exists and is still active — nothing was deleted or deactivated.
        Assert.True((await db.Users.AsNoTracking().FirstAsync(u => u.Id == student.Id)).IsActive);
    }

    // ── TEST 15 — the FRANCHISE wallet is the one that gets the opening balance ─────────────────

    [Fact]
    public async Task Test15_OpeningBalanceAndCreditLimit_LandOnTheFranchiseExactlyAsEntered()
    {
        using var db = NewDb();
        var student = await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);
        var svc = Svc(db);

        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.UseExistingAccount;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;
        req.CreditLimit = 25000m;
        req.OpeningBalance = 5000m;

        Assert.True((await svc.ApproveAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id);
        Assert.Equal(25000m, saved.CreditLimit);
        Assert.Equal(5000m, saved.WalletBalance);

        // Exactly one ledger row — no duplicate wallet entries.
        var ledger = await db.FranchiseLedger.AsNoTracking().Where(l => l.FranchiseId == f.Id).ToListAsync();
        Assert.Single(ledger);
        Assert.Equal(5000m, ledger[0].Amount);
        Assert.True(ledger[0].IsCredit);
    }

    // ── TEST 17 — the credentials email must not quote a password that was never set ────────────

    [Fact]
    public async Task Test17_ReusedAccount_IsNotEmailedAFabricatedPassword()
    {
        using var db = NewDb();
        await SeedStudentAsync(db, "smarteducat.learning@gmail.com", "9784835519");
        var f = await SeedPendingFranchiseAsync(db);

        var notify = new Mock<INotificationService>();
        IDictionary<string, string>? tokens = null;
        notify.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                      It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
              .Callback<string, NotificationRecipient, IDictionary<string, string>, string?>((_, _, t, _) => tokens = t)
              .ReturnsAsync((true, (string?)null, 1));

        var svc = Svc(db, notify);
        var conflict = await svc.CheckApprovalConflictAsync(f.Id);
        var req = Approval(f.Id);
        req.AccountResolution = FranchiseAccountResolution.UseExistingAccount;
        req.ExistingUserId = conflict.PhoneOwner!.UserId;

        Assert.True((await svc.ApproveAsync(req, Guid.NewGuid())).ok);

        Assert.NotNull(tokens);
        Assert.Contains("unchanged", tokens!["password"], StringComparison.OrdinalIgnoreCase);
        // …and it addresses the account's real email, not one it does not own.
        Assert.Equal("smarteducat.learning@gmail.com", tokens["email"]);
    }

    [Fact]
    public async Task AFreshApproval_IsStillEmailedARealGeneratedPassword()
    {
        using var db = NewDb();
        var f = await SeedPendingFranchiseAsync(db);

        var notify = new Mock<INotificationService>();
        IDictionary<string, string>? tokens = null;
        notify.Setup(n => n.SendAsync(It.IsAny<string>(), It.IsAny<NotificationRecipient>(),
                                      It.IsAny<IDictionary<string, string>>(), It.IsAny<string?>()))
              .Callback<string, NotificationRecipient, IDictionary<string, string>, string?>((_, _, t, _) => tokens = t)
              .ReturnsAsync((true, (string?)null, 1));

        Assert.True((await Svc(db, notify).ApproveAsync(Approval(f.Id), Guid.NewGuid())).ok);

        Assert.NotNull(tokens);
        Assert.DoesNotContain("unchanged", tokens!["password"], StringComparison.OrdinalIgnoreCase);
        Assert.True(tokens["password"].Length >= 8);
    }
}
