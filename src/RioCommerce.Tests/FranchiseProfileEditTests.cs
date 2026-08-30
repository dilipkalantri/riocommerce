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
/// Editing a franchise's own particulars from the admin list.
///
/// <para>The point of this screen is that it is narrow: it exists so a GSTIN can be added after
/// approval without re-running the Excel bulk import, and it must not become a back door into a
/// franchisee's money. Most of these tests assert what it CANNOT change.</para>
/// </summary>
public class FranchiseProfileEditTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"frprofile-{Guid.NewGuid()}")
            .Options);

    private static FranchiseService Svc(RioCommerceDbContext db) =>
        new(db,
            new Mock<INotificationService>().Object,
            new Mock<IAuditService>().Object,
            new Mock<IPaymentGatewayFactory>().Object,
            new Mock<IVerificationService>().Object,
            new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new Mock<IFranchiseShareCalculator>().Object,
            new Mock<IInvoiceService>().Object);

    /// <summary>LECTUREWALA as it actually stands in production: approved, no GSTIN.</summary>
    private static async Task<Franchise> SeedAsync(RioCommerceDbContext db, string state = "RAJASTHAN")
    {
        var f = new Franchise
        {
            Id = Guid.NewGuid(),
            Name = "LECTUREWALA",
            BusinessName = "CA ARJUN PARIHAR",
            Code = "JOD",
            City = "Jodhpur",
            State = state,
            ContactEmail = "lecturewalajodhpur@gmail.com",
            ContactPhone = "9529989199",
            Status = FranchiseStatus.Approved,
            IsActive = true,
            WalletBalance = 4250m,
            CreditLimit = 25000m,
            AdminUserId = Guid.NewGuid(),
        };
        db.Franchises.Add(f);
        await db.SaveChangesAsync();
        return f;
    }

    private static FranchiseProfileEdit EditOf(Franchise f) => new()
    {
        FranchiseId = f.Id, Name = f.Name, BusinessName = f.BusinessName,
        ContactPerson = f.ContactPerson, ContactPhone = f.ContactPhone,
        AddressLine = f.AddressLine, City = f.City, State = f.State, PinCode = f.PinCode,
        Gstin = f.Gstin, Pan = f.Pan,
    };

    // ── The thing this was built for ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AGstinCanBeAddedAfterApproval()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var req = EditOf(f);
        req.Gstin = "08ABCDE1234F1Z5";     // 08 = Rajasthan

        var (ok, error) = await Svc(db).UpdateProfileAsync(req, Guid.NewGuid());

        Assert.True(ok, error);
        Assert.Equal("08ABCDE1234F1Z5", (await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id)).Gstin);
    }

    [Fact]
    public async Task AGstinIsStoredUpperCasedAndTrimmed()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var req = EditOf(f);
        req.Gstin = "  08abcde1234f1z5  ";
        req.Pan = " abcde1234f ";

        Assert.True((await Svc(db).UpdateProfileAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id);
        Assert.Equal("08ABCDE1234F1Z5", saved.Gstin);
        Assert.Equal("ABCDE1234F", saved.Pan);
    }

    [Fact]
    public async Task ClearingTheGstinIsAllowed()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        f.Gstin = "08ABCDE1234F1Z5";
        await db.SaveChangesAsync();

        var req = EditOf(f);
        req.Gstin = "";     // deregistered — now raises a bill of supply, not a tax invoice

        Assert.True((await Svc(db).UpdateProfileAsync(req, Guid.NewGuid())).ok);
        Assert.Null((await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id)).Gstin);
    }

    // ── Validation ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("08ABCDE1234F1Z")]      // 14 chars
    [InlineData("08ABCDE1234F1Z55")]    // 16 chars
    [InlineData("ABCDE1234F1Z5XX")]     // no numeric state code
    [InlineData("08ABCDE1234F1X5")]     // 13th char must be Z
    public async Task AMalformedGstinIsRejected(string bad)
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var req = EditOf(f);
        req.Gstin = bad;

        var (ok, error) = await Svc(db).UpdateProfileAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("GSTIN", error);
        Assert.Null((await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id)).Gstin);
    }

    [Fact]
    public async Task AGstinFromTheWrongStateIsRejected()
    {
        using var db = NewDb();
        var f = await SeedAsync(db, state: "RAJASTHAN");
        var req = EditOf(f);
        req.Gstin = "27ABCDE1234F1Z5";   // 27 = Maharashtra, on a Rajasthan franchise

        var (ok, error) = await Svc(db).UpdateProfileAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("08", error);          // names the code Rajasthan should have
        Assert.Contains("RAJASTHAN", error);
    }

    [Fact]
    public async Task AnUnrecognisedStateNameSkipsTheStateCheckRatherThanBlocking()
    {
        using var db = NewDb();
        var f = await SeedAsync(db, state: "Somewhere Else");
        var req = EditOf(f);
        req.Gstin = "27ABCDE1234F1Z5";

        Assert.True((await Svc(db).UpdateProfileAsync(req, Guid.NewGuid())).ok);
    }

    [Theory]
    [InlineData("ABCDE1234")]    // 9 chars
    [InlineData("ABCD01234F")]   // digit in the letter block
    public async Task AMalformedPanIsRejected(string bad)
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var req = EditOf(f);
        req.Pan = bad;

        var (ok, error) = await Svc(db).UpdateProfileAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("PAN", error);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0123456")]
    [InlineData("abcdef")]
    public async Task AMalformedPinCodeIsRejected(string bad)
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var req = EditOf(f);
        req.PinCode = bad;

        var (ok, error) = await Svc(db).UpdateProfileAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Contains("PIN", error);
    }

    [Fact]
    public async Task NameAndCityAreRequired()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);

        var noName = EditOf(f); noName.Name = "";
        Assert.False((await Svc(db).UpdateProfileAsync(noName, Guid.NewGuid())).ok);

        var noCity = EditOf(f); noCity.City = "  ";
        Assert.False((await Svc(db).UpdateProfileAsync(noCity, Guid.NewGuid())).ok);
    }

    // ── What this screen must NOT be able to touch ──────────────────────────────────────────────

    [Fact]
    public async Task MoneyAndLifecycleAreUntouched()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var originalUser = f.AdminUserId;

        var req = EditOf(f);
        req.Gstin = "08ABCDE1234F1Z5";
        req.City = "Jaipur";
        Assert.True((await Svc(db).UpdateProfileAsync(req, Guid.NewGuid())).ok);

        var saved = await db.Franchises.AsNoTracking().FirstAsync(x => x.Id == f.Id);
        Assert.Equal(4250m, saved.WalletBalance);                       // wallet
        Assert.Equal(25000m, saved.CreditLimit);                        // credit line
        Assert.Equal(FranchiseStatus.Approved, saved.Status);           // lifecycle
        Assert.True(saved.IsActive);
        Assert.Equal("JOD", saved.Code);                                // short code
        Assert.Equal("lecturewalajodhpur@gmail.com", saved.ContactEmail); // login identity
        Assert.Equal(originalUser, saved.AdminUserId);
        Assert.Empty(await db.FranchiseLedger.ToListAsync());           // no ledger movement
    }

    [Fact]
    public async Task AnUnknownFranchiseIsRejected()
    {
        using var db = NewDb();
        var req = new FranchiseProfileEdit { FranchiseId = Guid.NewGuid(), Name = "X", City = "Y" };

        var (ok, error) = await Svc(db).UpdateProfileAsync(req, Guid.NewGuid());

        Assert.False(ok);
        Assert.Equal("Franchise not found.", error);
    }

    // ── Audit ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyTheFieldsThatChangedAreLogged()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var audit = new Mock<IAuditService>();
        string? details = null;
        audit.Setup(a => a.LogAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
             .Callback<Guid?, string, string, string, string?, string?>((_, _, _, _, _, d) => details = d)
             .Returns(Task.CompletedTask);

        var svc = new FranchiseService(db,
            new Mock<INotificationService>().Object, audit.Object,
            new Mock<IPaymentGatewayFactory>().Object, new Mock<IVerificationService>().Object,
            new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new Mock<IFranchiseShareCalculator>().Object, new Mock<IInvoiceService>().Object);

        var req = EditOf(f);
        req.Gstin = "08ABCDE1234F1Z5";
        Assert.True((await svc.UpdateProfileAsync(req, Guid.NewGuid())).ok);

        Assert.NotNull(details);
        Assert.Contains("Gstin", details);
        Assert.Contains("08ABCDE1234F1Z5", details);
        Assert.DoesNotContain("City", details);      // untouched fields stay out of the entry
    }

    [Fact]
    public async Task SavingWithNoChangesSucceedsWithoutWritingAnAuditEntry()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var audit = new Mock<IAuditService>();

        var svc = new FranchiseService(db,
            new Mock<INotificationService>().Object, audit.Object,
            new Mock<IPaymentGatewayFactory>().Object, new Mock<IVerificationService>().Object,
            new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new Mock<IFranchiseShareCalculator>().Object, new Mock<IInvoiceService>().Object);

        Assert.True((await svc.UpdateProfileAsync(EditOf(f), Guid.NewGuid())).ok);

        audit.Verify(a => a.LogAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                                     It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
                     Times.Never);
    }
}
