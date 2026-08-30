using RioCommerce.Core.DTOs.Products;
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
/// The admin product grid must report the batch status the product actually carries.
///
/// <para><c>ListAsync</c> projected every other column but left <c>BatchStatus</c> out, so each row
/// fell back to the DTO's initialiser — <c>BatchStatus.Upcoming</c>, the enum's zero value. The
/// column therefore read "Upcoming" for all 104 live products while the database held three
/// different states, and it contradicted the edit form one click away.</para>
///
/// <para>A default that happens to be a real, plausible value is the dangerous kind: nothing looks
/// broken, so these tests assert the non-default states specifically.</para>
/// </summary>
public class ProductBatchStatusGridTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"batch-{Guid.NewGuid()}")
            .Options);

    private static ProductAdminService Products(RioCommerceDbContext db) =>
        new(db, new Mock<IPublicFileStorage>().Object, new Mock<ISeoUrlService>().Object);

    private static Product P(string title, BatchStatus batch, int order) => new()
    {
        Id = Guid.NewGuid(), Title = title, Slug = $"p-{Guid.NewGuid():N}",
        BatchStatus = batch, DisplayOrder = order,
        Level = CourseLevel.Intermediate, SellingPrice = 5000m, Status = ProductStatus.Active
    };

    /// <summary>One product per state, so a projection that ignores the column cannot pass.</summary>
    private static async Task SeedAsync(RioCommerceDbContext db)
    {
        db.Products.AddRange(
            P("Ongoing Course", BatchStatus.Ongoing, 1),
            P("Pre-Recorded Course", BatchStatus.PreRecorded, 2),
            P("Upcoming Course", BatchStatus.Upcoming, 3),
            P("Out Of Stock Course", BatchStatus.OutOfStock, 4),
            P("Coming Soon Course", BatchStatus.ComingSoon, 5));
        await db.SaveChangesAsync();
    }

    private static ProductFilterRequest All() => new() { Page = 1, PageSize = 50 };

    // ── The reported bug ─────────────────────────────────────────────────────────
    [Fact]
    public async Task The_grid_reports_each_products_own_batch_status()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await Products(db).ListAsync(All());
        var byTitle = page.Items.ToDictionary(i => i.Title, i => i.BatchStatus);

        Assert.Equal(BatchStatus.Ongoing, byTitle["Ongoing Course"]);
        Assert.Equal(BatchStatus.PreRecorded, byTitle["Pre-Recorded Course"]);
        Assert.Equal(BatchStatus.Upcoming, byTitle["Upcoming Course"]);
        Assert.Equal(BatchStatus.OutOfStock, byTitle["Out Of Stock Course"]);
        Assert.Equal(BatchStatus.ComingSoon, byTitle["Coming Soon Course"]);
    }

    [Fact]
    public async Task Not_every_row_collapses_to_the_enums_default()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await Products(db).ListAsync(All());

        // The exact symptom that was on screen: five products, one badge.
        Assert.Equal(5, page.Items.Select(i => i.BatchStatus).Distinct().Count());
        Assert.Single(page.Items.Where(i => i.BatchStatus == BatchStatus.Upcoming));
    }

    [Fact]
    public async Task The_label_follows_the_status_it_was_given()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await Products(db).ListAsync(All());

        Assert.Equal("Ongoing Batch", page.Items.First(i => i.Title == "Ongoing Course").BatchStatusText);
        Assert.Equal("Pre-Recorded", page.Items.First(i => i.Title == "Pre-Recorded Course").BatchStatusText);
    }

    // ── The filter the grid offers now actually filters ──────────────────────────
    [Fact]
    public async Task Filtering_by_batch_status_returns_only_that_status()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var svc = Products(db);

        var ongoing = await svc.ListAsync(new ProductFilterRequest { BatchStatus = BatchStatus.Ongoing, Page = 1, PageSize = 50 });

        Assert.Single(ongoing.Items);
        Assert.Equal(1, ongoing.TotalCount);          // the count must narrow too, not just the page
        Assert.Equal("Ongoing Course", ongoing.Items[0].Title);
    }

    [Fact]
    public async Task No_batch_filter_still_returns_everything()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var page = await Products(db).ListAsync(All());

        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task The_batch_filter_combines_with_the_other_filters()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var extra = P("Foundation Ongoing", BatchStatus.Ongoing, 6);
        extra.Level = CourseLevel.Beginner;
        db.Products.Add(extra);
        await db.SaveChangesAsync();

        var svc = Products(db);
        var both = await svc.ListAsync(new ProductFilterRequest
        {
            BatchStatus = BatchStatus.Ongoing, Level = CourseLevel.Beginner, Page = 1, PageSize = 50
        });

        Assert.Single(both.Items);
        Assert.Equal("Foundation Ongoing", both.Items[0].Title);
    }
}
