using System.Text.Json;
using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RioCommerce.Tests;

/// <summary>
/// The report summary bar has to survive the trip through JSON.
///
/// <para><c>ReportTotals.Extra</c> was a <c>List&lt;(string, string)&gt;</c>. A ValueTuple carries its
/// parts as FIELDS, and <c>System.Text.Json</c> serialises properties only — so every entry left the
/// API as an empty <c>{}</c> and the caller got <c>[{}, {}, {}, {}]</c> where four totals should
/// have been. The Faculty-Wise report lost Teacher Share, GST on Share, Total Payout and Faculty
/// Covered exactly that way.</para>
///
/// <para>It was invisible in the admin UI because Blazor Server calls the service directly and never
/// serialises anything — which is precisely why it needed a test at the JSON boundary rather than at
/// the service one. These tests serialise with the same defaults ASP.NET Core uses.</para>
/// </summary>
public class ReportTotalsSerializationTests
{
    /// <summary>Matches the ASP.NET Core default: camelCase property names.</summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"totalsjson-{Guid.NewGuid()}")
            .Options);

    private static JsonElement Roundtrip(object value) =>
        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value, WebJson));

    /// <summary>Every Extra entry as a (label, value) pair read back out of the JSON.</summary>
    private static List<(string Label, string Value)> ExtraFromJson(JsonElement totals) =>
        totals.GetProperty("extra").EnumerateArray()
            .Select(e => (e.GetProperty("label").GetString()!, e.GetProperty("value").GetString()!))
            .ToList();

    // ── 1. The shape itself ─────────────────────────────────────────────────────
    [Fact]
    public void An_extra_total_serialises_as_an_object_with_label_and_value()
    {
        var totals = new ReportTotals();
        totals.Extra.Add(("Teacher share (cost)", "₹4,500.00"));

        var json = Roundtrip(totals);
        var entry = json.GetProperty("extra").EnumerateArray().Single();

        // The regression: this used to be {} with no properties at all.
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.Equal("Teacher share (cost)", entry.GetProperty("label").GetString());
        Assert.Equal("₹4,500.00", entry.GetProperty("value").GetString());
    }

    [Fact]
    public void The_tuple_call_sites_still_compile_and_carry_their_values()
    {
        // Twenty Extra.Add(("…","…")) sites across the report builders rely on this conversion.
        ReportTotalItem converted = ("B2B invoices", "7");

        Assert.Equal("B2B invoices", converted.Label);
        Assert.Equal("7", converted.Value);
    }

    [Fact]
    public void Deconstruction_still_works_for_the_grid_and_the_exporters()
    {
        // ReportGrid.razor and both exporters iterate as `foreach (var (label, value) in Items())`.
        var totals = new ReportTotals { TotalOrders = 2 };
        totals.Extra.Add(("Faculty covered", "1"));

        var pairs = totals.Items().Select(i => { var (label, value) = i; return (label, value); }).ToList();

        Assert.Equal(("Total Orders", "2"), pairs[0]);
        Assert.Equal(("Faculty covered", "1"), pairs[1]);
    }

    // ── 2-5. The four totals the Faculty-Wise report was losing ─────────────────

    /// <summary>A paid order with a 10% rule on a ₹1,180 @ 18% course — taxable base ₹1,000,
    /// share ₹100. Mirrors the live FRN-1024 shape, scaled down.</summary>
    private static async Task<RioCommerceDbContext> SeedEarnedShareAsync()
    {
        var db = NewDb();
        Guid fac = Guid.NewGuid(), prod = Guid.NewGuid(), subj = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        db.Faculty.Add(new Faculty { Id = fac, DisplayName = "CA Harshad Jaju", IsActive = true });
        db.Subjects.Add(new Subject { Id = subj, Name = "Economics", Slug = "eco", IsActive = true });
        db.Products.Add(new Product
        {
            Id = prod, Title = "Beginner Combo", Slug = $"p-{prod:N}", SubjectId = subj,
            SellingPrice = 1180m, GstRate = 18m, Status = ProductStatus.Active
        });
        db.Orders.Add(new Order
        {
            Id = orderId, OrderNumber = "FRN-9999", StudentName = "vishal", StudentPhone = "9800000000",
            Source = OrderSource.Franchisee, Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Success, TotalAmount = 1180m
        });
        db.OrderItems.Add(new OrderItem
        {
            Id = itemId, OrderId = orderId, ProductId = prod, ProductTitle = "Beginner Combo",
            Quantity = 1, UnitPrice = 1180m, Discount = 0m, LineTotal = 1180m
        });
        db.FacultyShareEntries.Add(new FacultyShareEntry
        {
            Id = Guid.NewGuid(), FacultyId = fac, OrderId = orderId, OrderNumber = "FRN-9999",
            OrderItemId = itemId, ProductId = prod, ProductTitle = "Beginner Combo",
            BaseAmount = 1000m, ShareType = SharingType.Percentage, ShareValue = 10m,
            ShareAmount = 100m, GstOnShare = 0m, TotalPayout = 100m
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task The_faculty_report_json_carries_all_four_of_its_extra_totals()
    {
        using var db = await SeedEarnedShareAsync();

        var table = await new FacultyReportBuilder(db)
            .BuildAsync(new ReportQuery { Page = 1, PageSize = 50 }, default);

        var extra = ExtraFromJson(Roundtrip(table).GetProperty("totals"));
        var byLabel = extra.ToDictionary(e => e.Label, e => e.Value);

        Assert.Equal(4, extra.Count);
        Assert.Equal("1", byLabel["Faculty covered"]);              // 5
        Assert.Equal("₹100.00", byLabel["Teacher share (cost)"]);   // 2
        Assert.Equal("₹0.00", byLabel["GST on share (ITC)"]);       // 3
        Assert.Equal("₹100.00", byLabel["Total payout"]);           // 4
    }

    [Fact]
    public async Task No_extra_entry_survives_the_trip_as_an_empty_object()
    {
        using var db = await SeedEarnedShareAsync();

        var table = await new FacultyReportBuilder(db)
            .BuildAsync(new ReportQuery { Page = 1, PageSize = 50 }, default);

        var entries = Roundtrip(table).GetProperty("totals").GetProperty("extra").EnumerateArray().ToList();

        Assert.NotEmpty(entries);
        // The exact symptom that was observed on the live API: an entry with no properties at all.
        Assert.All(entries, e => Assert.NotEmpty(e.EnumerateObject()));
    }

    [Fact]
    public async Task The_ordering_of_the_extra_totals_is_unchanged()
    {
        using var db = await SeedEarnedShareAsync();

        var table = await new FacultyReportBuilder(db)
            .BuildAsync(new ReportQuery { Page = 1, PageSize = 50 }, default);

        Assert.Equal(
            new[] { "Faculty covered", "Teacher share (cost)", "GST on share (ITC)", "Total payout" },
            ExtraFromJson(Roundtrip(table).GetProperty("totals")).Select(e => e.Label).ToArray());
    }

    // ── 6. The numbers themselves did not move ──────────────────────────────────
    [Fact]
    public async Task The_standard_totals_serialise_with_the_same_values_the_builder_computed()
    {
        using var db = await SeedEarnedShareAsync();

        var table = await new FacultyReportBuilder(db)
            .BuildAsync(new ReportQuery { Page = 1, PageSize = 50 }, default);

        var json = Roundtrip(table).GetProperty("totals");

        Assert.Equal(table.Totals!.TotalOrders, json.GetProperty("totalOrders").GetInt32());
        Assert.Equal(table.Totals.TotalQuantity, json.GetProperty("totalQuantity").GetInt32());
        Assert.Equal(table.Totals.GrossAmount, json.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(table.Totals.Discount, json.GetProperty("discount").GetDecimal());
        Assert.Equal(table.Totals.TotalSales, json.GetProperty("totalSales").GetDecimal());

        // And the service-side values are exactly what they were before the type changed.
        Assert.Equal(1, table.Totals.TotalOrders);
        Assert.Equal(1180m, table.Totals.TotalSales);
    }

    // ── Every other report that uses Extra survives the same trip ───────────────
    [Fact]
    public void The_other_reports_extra_totals_serialise_too()
    {
        // Representative of GstReportBuilder, ShippingReportBuilder, TeacherSettlementReportBuilder,
        // ProductSubjectReportBuilder, ReInvoiceReportBuilder and FranchiseeReportBuilder — all of
        // which add their own Extra entries through the same list.
        var totals = new ReportTotals { TotalOrders = 3, TotalGst = 180m };
        totals.Extra.Add(("Less: GST on refunds", "−₹20.00"));
        totals.Extra.Add(("Net GST", "₹160.00"));
        totals.Extra.Add(("B2B invoices", "1"));
        totals.Extra.Add(("Products covered", "2"));

        var extra = ExtraFromJson(Roundtrip(totals));

        Assert.Equal(4, extra.Count);
        Assert.Equal("Less: GST on refunds", extra[0].Label);
        Assert.Equal("−₹20.00", extra[0].Value);
        Assert.Equal("Net GST", extra[1].Label);
        Assert.Equal("B2B invoices", extra[2].Label);
        Assert.Equal("Products covered", extra[3].Label);
    }

    [Fact]
    public void An_empty_extra_list_still_serialises_as_an_empty_array()
    {
        var json = Roundtrip(new ReportTotals { TotalOrders = 0 });

        Assert.Equal(JsonValueKind.Array, json.GetProperty("extra").ValueKind);
        Assert.Equal(0, json.GetProperty("extra").GetArrayLength());
    }
}
