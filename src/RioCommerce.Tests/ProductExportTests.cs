using ClosedXML.Excel;
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
/// The Products screen's "Export to Excel" — every field of every product the filters select.
///
/// <para>Each test opens the produced bytes with ClosedXML and reads real cells, so a workbook that
/// is corrupt, empty, or missing a column fails here rather than in someone's Excel.</para>
///
/// <para>The export shares <c>ApplyGridFilterAsync</c> with the grid, so what downloads always
/// matches what is on screen — except that it is unpaged, which several tests pin down.</para>
/// </summary>
public class ProductExportTests
{
    private static RioCommerceDbContext NewDb() =>
        new(new DbContextOptionsBuilder<RioCommerceDbContext>()
            .UseInMemoryDatabase($"pexport-{Guid.NewGuid()}")
            .Options);

    private static ProductAdminService Svc(RioCommerceDbContext db) =>
        new(db, new Mock<IPublicFileStorage>().Object, new Mock<ISeoUrlService>().Object);

    private sealed record Fx(Guid Harshad, Guid Nipurn, Guid Subject, Guid Category, Guid CoTaught, Guid Solo);

    private static async Task<Fx> SeedAsync(RioCommerceDbContext db)
    {
        var f = new Fx(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        db.Faculty.AddRange(
            new Faculty { Id = f.Harshad, DisplayName = "CA Harshad Jaju", IsActive = true },
            new Faculty { Id = f.Nipurn, DisplayName = "CA Nipurn Modi", IsActive = true });
        db.Subjects.Add(new Subject { Id = f.Subject, Name = "Audit", Slug = "audit", IsActive = true });
        db.Categories.Add(new Category { Id = f.Category, Name = "CA Inter", Slug = "inter", IsActive = true });

        db.Products.AddRange(
            new Product
            {
                Id = f.CoTaught, Title = "CA Inter Audit & FMSM COMBO", Slug = "combo",
                Sku = "0012345", Level = CourseLevel.CaIntermediate, CourseType = CourseType.Combo,
                SubjectId = f.Subject, CategoryId = f.Category, PrimaryFacultyId = f.Harshad,
                Mrp = 15500m, SellingPrice = 14500m, GstRate = 18m, Status = ProductStatus.Active,
                BatchStatus = BatchStatus.Upcoming, TotalLectures = "120 Lectures", Language = "Hinglish",
                Tags = "combo,audit", Badge = "Bestseller", IsFeatured = true, DisplayOrder = 1,
                TotalOrders = 7, AvgRating = 4.5m, RatingCount = 2,
            },
            new Product
            {
                Id = f.Solo, Title = "CA Final SCMPE", Slug = "final-scmpe",
                Level = CourseLevel.CaFinal, SellingPrice = 9000m,
                Status = ProductStatus.Draft, DisplayOrder = 2,
            });

        db.Set<ProductFaculty>().AddRange(
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.CoTaught, FacultyId = f.Harshad, IsPrimary = true },
            new ProductFaculty { Id = Guid.NewGuid(), ProductId = f.CoTaught, FacultyId = f.Nipurn, IsPrimary = false });

        db.Set<ProductMode>().AddRange(
            new ProductMode { Id = Guid.NewGuid(), ProductId = f.CoTaught, ModeName = "Recorded", Price = 14500m, IsEnabled = true, DisplayOrder = 1 },
            new ProductMode { Id = Guid.NewGuid(), ProductId = f.CoTaught, ModeName = "Live", Price = 16500m, IsEnabled = false, DisplayOrder = 2 });

        db.Set<ProductInclusion>().Add(new ProductInclusion
        {
            Id = Guid.NewGuid(), ProductId = f.CoTaught, Title = "Hardcopy Books", DisplayOrder = 1,
        });

        await db.SaveChangesAsync();
        return f;
    }

    /// <summary>Opens the produced bytes as a real workbook — the check that it is not corrupt.</summary>
    private static IXLWorksheet Sheet(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return new XLWorkbook(ms).Worksheet(1);
    }

    private static int ColumnOf(IXLWorksheet ws, string header)
    {
        for (var c = 1; c <= ws.LastColumnUsed()!.ColumnNumber(); c++)
            if (ws.Cell(1, c).GetString() == header) return c;
        throw new Xunit.Sdk.XunitException($"Column '{header}' is missing from the export.");
    }

    private static string Cell(IXLWorksheet ws, int row, string header) => ws.Cell(row, ColumnOf(ws, header)).GetString();

    private static int RowOf(IXLWorksheet ws, string title)
    {
        var col = ColumnOf(ws, "Title");
        for (var r = 2; r <= ws.LastRowUsed()!.RowNumber(); r++)
            if (ws.Cell(r, col).GetString() == title) return r;
        throw new Xunit.Sdk.XunitException($"Product '{title}' is not in the export.");
    }

    // ── It produces a real workbook ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheExportIsAValidWorkbookWithAHeaderAndOneRowPerProduct()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));

        Assert.Equal("Products", ws.Name);
        Assert.Equal("Title", ws.Cell(1, 1).GetString());
        Assert.Equal(3, ws.LastRowUsed()!.RowNumber());   // header + 2 products
    }

    [Fact]
    public async Task TheHeaderRowIsFrozenAndBold()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));

        Assert.True(ws.Row(1).Style.Font.Bold);
        Assert.Equal(1, ws.SheetView.SplitRow);
    }

    // ── Every field is actually carried ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AProductsFieldsAreWrittenToTheirColumns()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));
        var r = RowOf(ws, "CA Inter Audit & FMSM COMBO");

        Assert.Equal("CA Intermediate", Cell(ws, r, "Level"));
        Assert.Equal("Combo", Cell(ws, r, "Course Type"));
        Assert.Equal("CA Inter", Cell(ws, r, "Category"));
        Assert.Equal("Audit", Cell(ws, r, "Subject"));
        Assert.Equal("CA Harshad Jaju", Cell(ws, r, "Primary Faculty"));
        Assert.Equal("Active", Cell(ws, r, "Status"));
        Assert.Equal("Upcoming", Cell(ws, r, "Batch Status"));
        Assert.Equal("120 Lectures", Cell(ws, r, "Total Lectures"));
        Assert.Equal("Hinglish", Cell(ws, r, "Language"));
        Assert.Equal("Bestseller", Cell(ws, r, "Badge"));
        Assert.Equal("Yes", Cell(ws, r, "Featured"));
        Assert.Equal("Hardcopy Books", Cell(ws, r, "Inclusions"));
        Assert.Contains("15500", Cell(ws, r, "MRP").Replace(",", ""));
        Assert.Contains("7", Cell(ws, r, "Total Orders"));
    }

    [Fact]
    public async Task AllFacultyAreListed_NotJustThePrimary()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));
        var all = Cell(ws, RowOf(ws, "CA Inter Audit & FMSM COMBO"), "All Faculty");

        // A co-taught course reads correctly — the primary alone would misrepresent it.
        Assert.Contains("CA Harshad Jaju", all);
        Assert.Contains("CA Nipurn Modi", all);
        Assert.StartsWith("CA Harshad Jaju", all);   // primary first
    }

    [Fact]
    public async Task ModesAreListedWithPricesAndDisabledOnesMarked()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));
        var modes = Cell(ws, RowOf(ws, "CA Inter Audit & FMSM COMBO"), "Modes (name | price)");

        Assert.Contains("Recorded", modes);
        Assert.Contains("Live", modes);
        Assert.Contains("(off)", modes);   // the disabled mode is visibly disabled
    }

    [Fact]
    public async Task ASkuWithLeadingZerosSurvives()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));

        // Written as text; as a number Excel would render it 12345 and the code would be wrong.
        Assert.Equal("0012345", Cell(ws, RowOf(ws, "CA Inter Audit & FMSM COMBO"), "SKU"));
    }

    [Fact]
    public async Task CaFinalExportsWithItsProperLabel()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));

        Assert.Equal("CA Final", Cell(ws, RowOf(ws, "CA Final SCMPE"), "Level"));
    }

    // ── It matches the grid ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ItHonoursTheSameFiltersAsTheGrid()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var svc = Svc(db);
        var filter = new ProductFilterRequest { Level = CourseLevel.CaFinal };

        var grid = await svc.ListAsync(filter);
        var ws = Sheet(await svc.ExportExcelAsync(filter));

        Assert.Equal(1, grid.TotalCount);
        Assert.Equal(2, ws.LastRowUsed()!.RowNumber());   // header + the one CA Final product
        Assert.Equal("CA Final SCMPE", ws.Cell(2, ColumnOf(ws, "Title")).GetString());
    }

    [Fact]
    public async Task ItIgnoresPagingAndExportsEverythingSelected()
    {
        using var db = NewDb();
        await SeedAsync(db);
        var svc = Svc(db);

        // The grid is showing one row per page; the export must still carry both products.
        var ws = Sheet(await svc.ExportExcelAsync(new ProductFilterRequest { Page = 1, PageSize = 1 }));

        Assert.Equal(3, ws.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task ADraftProductIsIncluded_TheAdminGridShowsEveryStatus()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest()));

        Assert.Equal("Draft", Cell(ws, RowOf(ws, "CA Final SCMPE"), "Status"));
    }

    [Fact]
    public async Task AFilterMatchingNothingStillProducesAReadableWorkbook()
    {
        using var db = NewDb();
        await SeedAsync(db);

        var ws = Sheet(await Svc(db).ExportExcelAsync(new ProductFilterRequest
        {
            Level = CourseLevel.TestSeries,   // nothing at this level
        }));

        // Header only — an empty export must still open, not be a zero-byte file.
        Assert.Equal(1, ws.LastRowUsed()!.RowNumber());
        Assert.Equal("Title", ws.Cell(1, 1).GetString());
    }

    // ── Read-only ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportingChangesNothing()
    {
        using var db = NewDb();
        var f = await SeedAsync(db);
        var before = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Title, p.SellingPrice, p.Status, p.TotalOrders }).ToListAsync();

        await Svc(db).ExportExcelAsync(new ProductFilterRequest());

        var after = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Title, p.SellingPrice, p.Status, p.TotalOrders }).ToListAsync();
        Assert.Equal(before, after);
        Assert.Equal(2, await db.Set<ProductFaculty>().CountAsync());
        Assert.NotEqual(Guid.Empty, f.CoTaught);
    }
}
