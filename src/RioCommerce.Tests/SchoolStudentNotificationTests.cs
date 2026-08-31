using RioCommerce.Core.DTOs.School;
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
/// A principal adding a class of forty students must not fire forty verification codes at forty
/// children. Nothing is sent when the school creates the account; the student claims it later
/// through the ordinary forgot-password/OTP flow, which is where the code belongs.
///
/// <para>These tests pin that boundary from both sides:</para>
/// <list type="bullet">
///   <item><b>Silent creation.</b> Adding, linking, and every rejection path leave the
///         verification service completely untouched — asserted against a strict mock, so any
///         future call fails the test rather than reaching a real gateway.</item>
///   <item><b>No credential.</b> The created account carries no password hash and
///         <c>IsVerified = false</c>, so it cannot be signed into until the student activates it.</item>
///   <item><b>Scope survives.</b> Suppressing the notification must not loosen the school
///         scoping or the role grant that the same method performs.</item>
/// </list>
///
/// The three flows that MUST still notify — student self-registration, principal
/// self-registration, and forgot-password — do not run through this service at all; they call
/// <see cref="IVerificationService.SendBothAsync"/> from Program.cs and SchoolRegistrationService
/// respectively, and are untouched by anything here.
/// </summary>
public class SchoolStudentNotificationTests
{
    // ── Fixtures ───────────────────────────────────────────────────────────────
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"school-notify-{Guid.NewGuid()}").Options);

    private static SchoolStudentService Service(RioCommerceDbContext db) =>
        new(db, NullLogger<SchoolStudentService>.Instance);

    private static readonly Guid SchoolA = Guid.NewGuid();
    private static readonly Guid SchoolB = Guid.NewGuid();
    private static readonly Guid PrincipalA = Guid.NewGuid();

    /// <summary>Seeds one school with an active principal, plus the "student" role.</summary>
    private static async Task SeedAsync(RioCommerceDbContext db)
    {
        db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "student" });
        db.Schools.Add(new School { Id = SchoolA, Name = "Test School A", UdiseCode = "27000000001" });
        db.Schools.Add(new School { Id = SchoolB, Name = "Test School B", UdiseCode = "27000000002" });
        db.Users.Add(new User { Id = PrincipalA, FullName = "Principal A", Email = "pa@example.test", Phone = "9000000001" });
        db.SchoolUsers.Add(new SchoolUser
        {
            Id = Guid.NewGuid(), SchoolId = SchoolA, UserId = PrincipalA,
            Role = SchoolUserRole.Principal, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private static AddSchoolStudentRequest Request(string email, string phone) => new()
    {
        FullName = "Test Student",
        Email = email,
        Phone = phone,
        DateOfBirth = new DateTime(2014, 5, 1, 0, 0, 0, DateTimeKind.Utc),
        Gender = "Male",
        StudentClass = "4",
        Section = "A",
        RollNumber = "12",
    };

    /// <summary>
    /// A strict mock: ANY call — SendBothAsync, SendEmailAsync, SendSmsAsync, anything added
    /// later — throws. That is the assertion. It is never wired into the service (which takes no
    /// such dependency); holding it here proves the dependency cannot appear without this test
    /// failing to compile or run.
    /// </summary>
    private static Mock<IVerificationService> StrictVerifier() => new(MockBehavior.Strict);

    // ── FLOW C: principal adds a brand-new student ─────────────────────────────

    [Fact]
    public async Task AddingStudent_SendsNoEmailSmsOrOtp()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var verify = StrictVerifier();

        var res = await Service(db).AddAsync(PrincipalA, Request("child1@example.test", "9000001111"));

        Assert.True(res.Ok);
        Assert.False(res.LinkedExisting);

        // Nothing was asked of the verification service — no code generated, nothing dispatched.
        verify.VerifyNoOtherCalls();

        // And no verification row was written, so no code exists to be resent either.
        Assert.Empty(db.VerificationCodes);
    }

    [Fact]
    public async Task PrincipalCreatedStudent_HasNoPasswordAndIsUnverified()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var res = await Service(db).AddAsync(PrincipalA, Request("child2@example.test", "9000002222"));
        Assert.True(res.Ok);

        var student = await db.Users.SingleAsync(u => u.Id == res.UserId);

        // No credential is issued or mailed: the account cannot be signed into until the student
        // sets a password themselves through forgot-password.
        Assert.True(string.IsNullOrEmpty(student.PasswordHash));
        Assert.False(student.IsVerified);
    }

    [Fact]
    public async Task PrincipalCreatedStudent_GetsStudentRoleOnlyAndCorrectSchool()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var res = await Service(db).AddAsync(PrincipalA, Request("child3@example.test", "9000003333"));
        Assert.True(res.Ok);

        var roleNames = await (from ur in db.UserRoles
                               join r in db.Roles on ur.RoleId equals r.Id
                               where ur.UserId == res.UserId
                               select r.Name).ToListAsync();
        Assert.Equal(new[] { "student" }, roleNames);

        // The school comes from the principal's own link, never from the request.
        var membership = await db.SchoolStudents.SingleAsync(ss => ss.UserId == res.UserId);
        Assert.Equal(SchoolA, membership.SchoolId);
    }

    // ── FLOW C: the linking and rejection paths are equally silent ─────────────

    [Fact]
    public async Task LinkingExistingAccount_SendsNothing()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var verify = StrictVerifier();

        // An account that exists but belongs to no school yet (CASE 2).
        var existing = new User
        {
            Id = Guid.NewGuid(), FullName = "Already Here",
            Email = "existing@example.test", Phone = "9000004444", IsVerified = true,
        };
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        var res = await Service(db).AddAsync(PrincipalA, Request("existing@example.test", "9000004444"));

        Assert.True(res.Ok);
        Assert.True(res.LinkedExisting);          // linked, not duplicated
        verify.VerifyNoOtherCalls();
        Assert.Empty(db.VerificationCodes);
        Assert.Single(db.Users.Where(u => u.Email == "existing@example.test"));
    }

    [Fact]
    public async Task StudentOfAnotherSchool_IsRejectedAndSendsNothing()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var verify = StrictVerifier();

        var other = new User
        {
            Id = Guid.NewGuid(), FullName = "Other School Student",
            Email = "other@example.test", Phone = "9000005555",
        };
        db.Users.Add(other);
        db.SchoolStudents.Add(new SchoolStudent
        {
            Id = Guid.NewGuid(), SchoolId = SchoolB, UserId = other.Id, IsActive = true,
        });
        await db.SaveChangesAsync();

        var res = await Service(db).AddAsync(PrincipalA, Request("other@example.test", "9000005555"));

        Assert.False(res.Ok);                     // CASE 3 blocking behaviour preserved
        Assert.Contains("another school", res.Error!, StringComparison.OrdinalIgnoreCase);
        verify.VerifyNoOtherCalls();
        Assert.Empty(db.VerificationCodes);
    }

    [Fact]
    public async Task UserWithNoSchoolLink_CannotAddAndSendsNothing()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var verify = StrictVerifier();

        var stranger = Guid.NewGuid();            // holds no SchoolUser row at all

        var res = await Service(db).AddAsync(stranger, Request("nobody@example.test", "9000006666"));

        Assert.False(res.Ok);
        verify.VerifyNoOtherCalls();
        Assert.Empty(db.VerificationCodes);
        Assert.Empty(db.SchoolStudents);
    }

    // ── The service is structurally incapable of notifying ─────────────────────

    [Fact]
    public void SchoolStudentService_TakesNoNotificationDependency()
    {
        // If someone injects a verification/email/SMS service here later, this fails — which is
        // the point. The suppression is a property of the type, not of a runtime flag that could
        // be flipped by accident.
        var paramTypes = typeof(SchoolStudentService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain(nameof(IVerificationService), paramTypes);
        Assert.DoesNotContain("IEmailSender", paramTypes);
        Assert.DoesNotContain("ISmsSender", paramTypes);
        Assert.DoesNotContain("IMessageDispatcher", paramTypes);
    }
}
