using RioCommerce.Core.Constants;
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
/// The State the franchisee picks on /franchise/order is not a label — it is the string that lands on
/// the order, travels to the invoice, and is what the GST place-of-supply comparison comes down to.
/// While the field was free text, "Maharastra", "MH" and a trailing space were all as saveable as the
/// real thing, and nothing downstream could tell the difference.
///
/// <para>These tests pin the list itself, and that the value a dropdown can produce is one the order
/// path accepts. There is no bUnit in this project, so the rendered <c>&lt;select&gt;</c> is not
/// asserted here — what IS asserted is everything the markup depends on: the exact 36 options, their
/// order, and that a selection round-trips through order creation onto the saved row.</para>
/// </summary>
public class IndianStatesTests
{
    // ── The list ────────────────────────────────────────────────────────────────

    [Fact]
    public void There_are_twenty_eight_states_and_eight_union_territories()
    {
        Assert.Equal(28, IndianStates.States.Count);
        Assert.Equal(8, IndianStates.UnionTerritories.Count);
        Assert.Equal(36, IndianStates.All.Count);
    }

    [Fact]
    public void All_thirty_six_names_are_present_and_in_the_expected_order()
    {
        // The full approved list, verbatim: 28 states alphabetically, then the 8 union territories.
        Assert.Equal(new[]
        {
            "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh", "Goa", "Gujarat",
            "Haryana", "Himachal Pradesh", "Jharkhand", "Karnataka", "Kerala", "Madhya Pradesh",
            "Maharashtra", "Manipur", "Meghalaya", "Mizoram", "Nagaland", "Odisha", "Punjab",
            "Rajasthan", "Sikkim", "Tamil Nadu", "Telangana", "Tripura", "Uttar Pradesh",
            "Uttarakhand", "West Bengal",
            "Andaman and Nicobar Islands", "Chandigarh",
            "Dadra and Nagar Haveli and Daman and Diu", "Delhi", "Jammu and Kashmir", "Ladakh",
            "Lakshadweep", "Puducherry",
        }, IndianStates.All.ToArray());
    }

    [Fact]
    public void The_union_territories_follow_the_states()
    {
        Assert.Equal(IndianStates.States, IndianStates.All.Take(28));
        Assert.Equal(IndianStates.UnionTerritories, IndianStates.All.Skip(28));
    }

    [Fact]
    public void No_name_is_duplicated_or_carries_stray_whitespace()
    {
        // A duplicate would render twice in the dropdown; stray whitespace would make an otherwise
        // identical state compare unequal against the seller's own state in the GST check.
        Assert.Equal(IndianStates.All.Count, IndianStates.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(IndianStates.All, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.Equal(s.Trim(), s);
        });
    }

    [Fact]
    public void The_states_the_business_actually_trades_in_are_there()
    {
        // Maharashtra is the seller's own state — the one the intra/inter-state GST split turns on.
        Assert.Contains("Maharashtra", IndianStates.All);
        Assert.Contains("Gujarat", IndianStates.All);
        // Present in the approved list but missing from the older arrays the other screens still use.
        Assert.Contains("Ladakh", IndianStates.All);
        Assert.Contains("Sikkim", IndianStates.All);
        Assert.Contains("Dadra and Nagar Haveli and Daman and Diu", IndianStates.All);
        // "Other" was a free-text escape hatch on the legacy arrays; it is not a state.
        Assert.DoesNotContain("Other", IndianStates.All);
    }

    // ── The order path still accepts what the dropdown produces ─────────────────

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"states-{Guid.NewGuid()}")
            .Options);

    private static FranchisePortalService Portal(RioCommerceDbContext db) =>
        new(db,
            new Mock<IFranchiseService>().Object,
            new Mock<INotificationService>().Object,
            new FranchiseShareCalculator(db),
            new Mock<IInvoiceService>().Object,
            new Mock<IPaymentGatewayFactory>().Object,
            new Mock<ISerialKeyService>().Object,
            new Mock<IPaymentModeRegistry>().Object,
            new StudentAccountProvisioner(db, NullLogger<StudentAccountProvisioner>.Instance),
            new Mock<IFacultySharingService>().Object,
            NullLogger<FranchisePortalService>.Instance);

    private static async Task<(Guid FranchiseId, Guid ProductId)> SeedAsync(RioCommerceDbContext db)
    {
        Guid fid = Guid.NewGuid(), pid = Guid.NewGuid();
        db.Franchises.Add(new Franchise
        {
            Id = fid, Name = "Rajkot Centre", Code = "RAJ", City = "Rajkot", State = "Gujarat",
            AddressLine = "Kalawad Road", PinCode = "360005",
            ContactPerson = "Owner", ContactPhone = "9000000001", ContactEmail = "raj@example.com",
            WalletBalance = 500000m, CreditLimit = 0m, IsActive = true, Status = FranchiseStatus.Approved
        });
        db.Products.Add(new Product
        {
            Id = pid, Title = "CA Inter Audit", Slug = $"p-{pid:N}",
            SellingPrice = 5000m, Mrp = 6000m, GstRate = 18m, Status = ProductStatus.Active
        });
        await db.SaveChangesAsync();
        return (fid, pid);
    }

    private static FranchiseOrderRequest Request(Guid productId, string? state) => new()
    {
        Lines = { new FranchiseOrderLine { ProductId = productId, Quantity = 1 } },
        CustomerName = "Prachi Vaghela",
        Phone = "9876543210",
        Email = "prachi@example.com",
        AddressLine = "12 MG Road",
        City = "Rajkot",
        State = state,
        PinCode = "360005",
        PaymentMethod = FranchisePaymentMethod.Wallet
    };

    [Fact]
    public async Task A_selected_state_is_saved_verbatim_on_the_order()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        var (ok, error, number) = await Portal(db).PlaceOrderAsync(fid, Request(pid, "Tamil Nadu"));

        Assert.True(ok, error);
        var order = await db.Orders.FirstAsync(o => o.OrderNumber == number);
        // Shipping is the student's address; the picked string lands there unchanged.
        Assert.Equal("Tamil Nadu", order.ShippingState);
    }

    [Fact]
    public async Task The_placeholder_option_is_rejected_by_the_existing_required_check()
    {
        using var db = NewDb();
        var (fid, pid) = await SeedAsync(db);

        // "Select State" submits an empty value — the same thing an untouched dropdown sends.
        var (ok, error, _) = await Portal(db).PlaceOrderAsync(fid, Request(pid, ""));

        Assert.False(ok);
        Assert.Equal("State is required.", error);
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Every_option_the_dropdown_offers_is_accepted_by_the_order_path()
    {
        // Guards the pairing rather than the list: an entry that the server would reject would make
        // the dropdown offer a choice the franchisee cannot actually order with.
        foreach (var state in IndianStates.All)
        {
            using var db = NewDb();
            var (fid, pid) = await SeedAsync(db);

            var (ok, error, number) = await Portal(db).PlaceOrderAsync(fid, Request(pid, state));

            Assert.True(ok, $"{state}: {error}");
            var order = await db.Orders.FirstAsync(o => o.OrderNumber == number);
            Assert.Equal(state, order.ShippingState);
        }
    }
}
