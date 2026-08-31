using ClosedXML.Excel;
using RioCommerce.Infrastructure.Services.Catalog;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ProductAdminService : IProductAdminService
{
    private readonly RioCommerceDbContext _db;
    private readonly IPublicFileStorage _files;
    private readonly ISeoUrlService _seo;
    public ProductAdminService(RioCommerceDbContext db, IPublicFileStorage files, ISeoUrlService seo) { _db = db; _files = files; _seo = seo; }

    /// <summary>
    /// The admin grid's WHERE clause, shared with the Excel export so a download can never select a
    /// different set than the screen it was launched from.
    ///
    /// <para><c>filter.BatchStatus</c> IS applied now. It used to be skipped on the grounds that the
    /// grid ignored it and the export should not diverge — but the grid ignored it only because the
    /// column was never projected, so every row claimed "Upcoming". With the real value on screen a
    /// picker that changes nothing is plainly broken, and applying it here fixes both sides at once:
    /// grid and export still select from the identical WHERE clause.</para>
    /// </summary>
    private async Task<IQueryable<Product>> ApplyGridFilterAsync(IQueryable<Product> q, ProductFilterRequest filter)
    {
        if (filter.Level.HasValue) q = q.Where(p => p.Level == filter.Level);
        if (filter.BatchStatus.HasValue) q = q.Where(p => p.BatchStatus == filter.BatchStatus);
        if (filter.CourseType.HasValue) q = q.Where(p => p.CourseType == filter.CourseType);
        if (filter.FacultyId.HasValue) q = q.Where(p => p.PrimaryFacultyId == filter.FacultyId);
        // Any assigned subject, not just the primary — otherwise a combo is unreachable from the
        // second subject an admin deliberately gave it.
        if (filter.SubjectId.HasValue)
            q = q.Where(p => _db.ProductSubjects.Any(ps => ps.ProductId == p.Id && ps.SubjectId == filter.SubjectId)
                          || p.SubjectId == filter.SubjectId);
        if (filter.CategoryId.HasValue)
        {
            if (filter.SearchSubcategories)
            {
                var catIds = await DescendantCategoryIdsAsync(filter.CategoryId.Value);
                q = q.Where(p => p.CategoryId != null && catIds.Contains(p.CategoryId.Value));
            }
            else q = q.Where(p => p.CategoryId == filter.CategoryId);
        }
        if (filter.Status.HasValue) q = q.Where(p => p.Status == filter.Status);
        if (!string.IsNullOrEmpty(filter.Search)) q = q.Where(p => EF.Functions.ILike(p.Title, $"%{filter.Search.Trim()}%"));
        return q;
    }

    public async Task<PagedResult<ProductListItem>> ListAsync(ProductFilterRequest filter)
    {
        var q = _db.Products.Include(p => p.PrimaryFaculty).AsQueryable();   // all statuses for admin
        q = await ApplyGridFilterAsync(q, filter);

        var total = await q.CountAsync();
        var items = await q.OrderBy(p => p.DisplayOrder).ThenByDescending(p => p.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(p => new ProductListItem
            {
                Id = p.Id, Title = p.Title, Slug = p.Slug, Level = p.Level, CourseType = p.CourseType,
                FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                Mrp = p.Mrp, SellingPrice = p.SellingPrice,
                SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
                Badge = p.Badge, TotalOrders = p.TotalOrders, Status = p.Status,
                IsFeatured = p.IsFeatured, HomePageDisplayOrder = p.HomePageDisplayOrder,
                // Read from the product, not left to the DTO's default. Omitting it made every row
                // in the admin grid render "Upcoming" — the enum's zero value — regardless of what
                // the product actually said, so the column silently contradicted the edit form.
                BatchStatus = p.BatchStatus
            }).ToListAsync();
        return new PagedResult<ProductListItem> { Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize };
    }

    public async Task<AdminProductStats> StatsAsync() => new(
        await _db.Products.CountAsync(),
        await _db.Products.CountAsync(p => p.Status == ProductStatus.Active),
        await _db.Products.CountAsync(p => p.Level == CourseLevel.Beginner),
        await _db.Products.CountAsync(p => p.Level == CourseLevel.Intermediate));

    public async Task<byte[]> ExportExcelAsync(ProductFilterRequest filter, CancellationToken ct = default)
    {
        // Unpaged on purpose: the grid shows 20 at a time, but an export of "the products I filtered
        // to" means all of them. Page/PageSize on the filter are ignored here.
        var q = _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Subject)
            .Include(p => p.PrimaryFaculty)
            .Include(p => p.Modes)
            .Include(p => p.Inclusions)
            .Include(p => p.ProductFaculty).ThenInclude(pf => pf.Faculty)
            .AsQueryable();
        q = await ApplyGridFilterAsync(q, filter);

        var rows = await q.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Title).ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Products");

        string[] headers =
        {
            // Identity
            "Title", "SKU", "Slug", "Status", "Product ID",
            // Classification
            "Level", "Course Type", "Category", "Subject", "Primary Faculty", "All Faculty",
            // Pricing
            "MRP", "Selling Price", "Special Price", "Special From", "Special To", "Effective Price",
            "Product Cost", "GST Rate %", "GST Inclusive", "SAC Code",
            // Modes
            "Modes (name | price)",
            // Batch
            "Batch Status", "Batch Start", "Applicable Attempts", "Available From", "Available To",
            // Course content
            "Total Lectures", "Total Hours", "Views", "Validity", "Language", "Course Schedule",
            "Books Info", "Exam Oriented Info", "Inclusions", "Additional Details",
            "Lecture Access Timing", "Notes Dispatch Timeline", "Estimated Delivery",
            // Sharing defaults
            "Franchise Share On", "Franchise Share Type", "Franchise Share Value",
            "Faculty Share On", "Faculty Share Type", "Faculty Share Value",
            // Media
            "Lectures Video URL", "Book Preview PDF", "Home Card Image", "Offer Image",
            // Merchandising
            "Badge", "Featured", "Mark As New", "Display Order", "Home Page Order", "Tags", "GTIN",
            // Stats
            "Total Orders", "Total Views", "Avg Rating", "Rating Count", "Allow Reviews",
            // SEO + text
            "SEO Title", "SEO Description", "Short Description", "Full Description",
            "Admin Comment", "Published At", "Created At",
        };
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        var row = 1;
        foreach (var p in rows)
        {
            row++;
            var c = 1;

            ws.Cell(row, c++).Value = p.Title;
            // Text format: SKUs and GTINs are codes, and Excel would otherwise eat leading zeros
            // or reformat a long numeric one into scientific notation.
            ws.Cell(row, c++).SetValue(p.Sku ?? "").Style.NumberFormat.Format = "@";
            ws.Cell(row, c++).Value = p.Slug;
            ws.Cell(row, c++).Value = p.Status.ToString();
            ws.Cell(row, c++).Value = p.Id.ToString();

            ws.Cell(row, c++).Value = LevelLabel(p.Level);
            ws.Cell(row, c++).Value = p.CourseType.ToString();
            ws.Cell(row, c++).Value = p.Category?.Name ?? "";
            ws.Cell(row, c++).Value = p.Subject?.Name ?? "";
            ws.Cell(row, c++).Value = p.PrimaryFaculty?.DisplayName ?? "";
            // Every mapped teacher, not just the primary — a co-taught course reads correctly here.
            ws.Cell(row, c++).Value = string.Join(", ", p.ProductFaculty
                .Where(pf => pf.Faculty != null)
                .OrderByDescending(pf => pf.IsPrimary)
                .Select(pf => pf.Faculty!.DisplayName));

            ws.Cell(row, c++).Value = p.Mrp;
            ws.Cell(row, c++).Value = p.SellingPrice;
            if (p.SpecialPrice is { } sp) ws.Cell(row, c).Value = sp; c++;
            ws.Cell(row, c++).Value = Date(p.SpecialPriceStartDateUtc);
            ws.Cell(row, c++).Value = Date(p.SpecialPriceEndDateUtc);
            ws.Cell(row, c++).Value = p.EffectiveSellingPrice;
            ws.Cell(row, c++).Value = p.ProductCost;
            ws.Cell(row, c++).Value = p.GstRate;
            ws.Cell(row, c++).Value = p.GstInclusive ? "Yes" : "No";
            ws.Cell(row, c++).SetValue(p.SacCode ?? "").Style.NumberFormat.Format = "@";

            ws.Cell(row, c++).Value = string.Join(" | ", p.Modes
                .OrderBy(m => m.DisplayOrder)
                .Select(m => $"{m.ModeName} ₹{m.Price:0}{(m.IsEnabled ? "" : " (off)")}"));

            ws.Cell(row, c++).Value = p.BatchStatus.ToString();
            ws.Cell(row, c++).Value = p.BatchStartDate?.ToString("dd MMM yyyy") ?? "";
            ws.Cell(row, c++).Value = p.ApplicableAttempts is { Length: > 0 } a ? string.Join(", ", a) : "";
            ws.Cell(row, c++).Value = Date(p.AvailableStartUtc);
            ws.Cell(row, c++).Value = Date(p.AvailableEndUtc);

            ws.Cell(row, c++).Value = p.TotalLectures ?? "";
            ws.Cell(row, c++).Value = p.TotalHours ?? "";
            ws.Cell(row, c++).Value = p.Views ?? "";
            ws.Cell(row, c++).Value = p.Validity ?? "";
            ws.Cell(row, c++).Value = p.Language ?? "";
            ws.Cell(row, c++).Value = p.CourseSchedule ?? "";
            ws.Cell(row, c++).Value = p.BooksInfo ?? "";
            ws.Cell(row, c++).Value = p.ExamOrientedInfo ?? "";
            ws.Cell(row, c++).Value = string.Join(", ", p.Inclusions.OrderBy(i => i.DisplayOrder).Select(i => i.Title));
            ws.Cell(row, c++).Value = p.AdditionalDetails ?? "";
            ws.Cell(row, c++).Value = p.LectureAccessTiming ?? "";
            ws.Cell(row, c++).Value = p.NotesDispatchTimeline ?? "";
            ws.Cell(row, c++).Value = p.EstimatedDeliveryMessage ?? "";

            ws.Cell(row, c++).Value = p.EnableDefaultFranchiseShare ? "Yes" : "No";
            ws.Cell(row, c++).Value = p.DefaultFranchiseShareType.ToString();
            ws.Cell(row, c++).Value = p.DefaultFranchiseShareValue;
            ws.Cell(row, c++).Value = p.EnableDefaultFacultyShare ? "Yes" : "No";
            ws.Cell(row, c++).Value = p.DefaultFacultyShareType.ToString();
            ws.Cell(row, c++).Value = p.DefaultFacultyShareValue;

            ws.Cell(row, c++).Value = p.LecturesVideoUrl ?? "";
            ws.Cell(row, c++).Value = p.BookPreviewPdfUrl ?? "";
            ws.Cell(row, c++).Value = p.HomeCardImageUrl ?? "";
            ws.Cell(row, c++).Value = p.OfferImageUrl ?? "";

            ws.Cell(row, c++).Value = p.Badge ?? "";
            ws.Cell(row, c++).Value = p.IsFeatured ? "Yes" : "No";
            ws.Cell(row, c++).Value = p.MarkAsNew ? "Yes" : "No";
            ws.Cell(row, c++).Value = p.DisplayOrder;
            ws.Cell(row, c++).Value = p.HomePageDisplayOrder;
            ws.Cell(row, c++).Value = p.Tags ?? "";
            ws.Cell(row, c++).SetValue(p.Gtin ?? "").Style.NumberFormat.Format = "@";

            ws.Cell(row, c++).Value = p.TotalOrders;
            ws.Cell(row, c++).Value = p.TotalViews;
            ws.Cell(row, c++).Value = p.AvgRating;
            ws.Cell(row, c++).Value = p.RatingCount;
            ws.Cell(row, c++).Value = p.AllowReviews ? "Yes" : "No";

            ws.Cell(row, c++).Value = p.SeoTitle ?? "";
            ws.Cell(row, c++).Value = p.SeoDescription ?? "";
            ws.Cell(row, c++).Value = p.ShortDesc ?? "";
            // The description columns hold HTML and can run to thousands of characters. Excel
            // refuses a cell over 32767, so they are cut rather than failing the whole workbook.
            ws.Cell(row, c++).Value = Clip(p.FullDesc);
            ws.Cell(row, c++).Value = Clip(p.AdminComment);
            ws.Cell(row, c++).Value = Date(p.PublishedAt);
            ws.Cell(row, c++).Value = Date(p.CreatedAt);
        }

        ws.Columns().AdjustToContents(1, 1, 12d, 55d);
        ws.RangeUsed()?.SetAutoFilter();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();

        // IST, matching every other date the admin sees on screen.
        static string Date(DateTime? utc) =>
            utc is { } d ? TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(d, DateTimeKind.Utc), Ist).ToString("dd MMM yyyy HH:mm") : "";

        static string Clip(string? s) =>
            s is null ? "" : s.Length <= 32000 ? s : s[..32000] + "… [truncated]";
    }

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    private static string LevelLabel(CourseLevel l) => l.Label();

    public async Task<ProductFilterMeta> GetFilterMetaAsync(
        Guid? categoryId = null, Guid? subjectId = null, Guid? facultyId = null, CourseLevel? level = null)
    {
        // Cascade inputs. Every list below narrows to what the OTHER selections allow and ignores
        // its own, so a different value in the same dropdown can always be chosen. With nothing
        // picked, Constrains is false everywhere and each query is left exactly as it was.
        var picked = CatalogCascade.Selection.Of(
            facultyId: facultyId, subjectId: subjectId, categoryId: categoryId, level: level);

        var cats = await _db.Categories.Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.ParentId }).ToListAsync();

        // Categories are a tree, so a straight filter would orphan children whose parent holds no
        // products of its own. A branch is kept when it OR any descendant still has one.
        HashSet<Guid>? keepCategories = null;
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Category))
        {
            var direct = await CatalogCascade
                .CategoryIdsFor(CatalogCascade.ProductsMatching(_db, picked, CatalogCascade.Dimension.Category))
                .ToListAsync();
            keepCategories = direct.ToHashSet();
            var parentOf = cats.ToDictionary(c => c.Id, c => c.ParentId);
            foreach (var id in direct)
            {
                var parent = parentOf.TryGetValue(id, out var p) ? p : null;
                var guard = 0;
                while (parent is { } pid && guard++ < 32 && keepCategories.Add(pid))
                    parent = parentOf.TryGetValue(pid, out var next) ? next : null;
            }
            // Where preserves the DisplayOrder/Name ordering the query already applied.
            cats = cats.Where(c => keepCategories.Contains(c.Id)).ToList();
        }
        // Flatten the category tree depth-first with em-dash indentation so the dropdown reads hierarchically.
        var byParent = cats.ToLookup(c => c.ParentId);
        var known = cats.Select(c => c.Id).ToHashSet();
        var ordered = new List<IdName>();
        void Walk(Guid? parent, int depth)
        {
            foreach (var c in byParent[parent])
            {
                ordered.Add(new IdName(c.Id, (depth > 0 ? new string('—', depth) + " " : "") + c.Name));
                Walk(c.Id, depth + 1);
            }
        }
        Walk(null, 0);
        // Surface any orphans (parent inactive/missing) at the root so nothing disappears.
        foreach (var c in cats.Where(c => c.ParentId != null && !known.Contains(c.ParentId!.Value)))
            if (ordered.All(o => o.Id != c.Id)) ordered.Add(new IdName(c.Id, c.Name));

        var subjectQuery = _db.Subjects.Where(s => s.IsActive);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Subject))
        {
            var ids = CatalogCascade.SubjectIdsFor(_db,
                CatalogCascade.ProductsMatching(_db, picked, CatalogCascade.Dimension.Subject));
            subjectQuery = subjectQuery.Where(s => ids.Contains(s.Id));
        }
        var subjects = await subjectQuery
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new IdName(s.Id, s.Name)).ToListAsync();

        var facultyQuery = _db.Faculty.Where(f => f.IsActive);
        if (CatalogCascade.Constrains(picked, CatalogCascade.Dimension.Faculty))
        {
            var ids = CatalogCascade.FacultyIdsFor(_db,
                CatalogCascade.ProductsMatching(_db, picked, CatalogCascade.Dimension.Faculty));
            facultyQuery = facultyQuery.Where(f => ids.Contains(f.Id));
        }
        var faculties = await facultyQuery
            .OrderBy(f => f.DisplayOrder).ThenBy(f => f.DisplayName)
            .Select(f => new IdName(f.Id, f.DisplayName)).ToListAsync();

        return new ProductFilterMeta(ordered, subjects, faculties);
    }

    public async Task<Guid?> FindBySkuAsync(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        var s = sku.Trim();
        return await _db.Products.Where(p => p.Sku != null && EF.Functions.ILike(p.Sku!, s))
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
    }

    // Selected category + all its descendants (BFS over the active+inactive tree).
    private async Task<HashSet<Guid>> DescendantCategoryIdsAsync(Guid rootId)
    {
        var edges = await _db.Categories.Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var byParent = edges.ToLookup(c => c.ParentId);
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var child in byParent[id])
                if (result.Add(child.Id)) queue.Enqueue(child.Id);
        }
        return result;
    }

    public async Task<ProductEditModel?> GetForEditAsync(Guid id)
    {
        // Read-only projection to a DTO — don't pollute the (circuit-lived) context's change tracker.
        var p = await _db.Products.AsNoTracking().Include(x => x.Modes).Include(x => x.Inclusions).Include(x => x.Images)
            .Include(x => x.OptionGroups).ThenInclude(g => g.Items)
            .Include(x => x.AttributeMappings).ThenInclude(am => am.Values)
            .Include(x => x.SpecificationAttributes).ThenInclude(sa => sa.SpecificationAttributeOption)
            .Include(x => x.Testimonials)
            .Include(x => x.ProductCategories)
            .Include(x => x.ProductFaculty)
            .Include(x => x.ProductSubject)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return null;
        return new ProductEditModel
        {
            Id = p.Id, Title = p.Title, Slug = p.Slug, ShortDesc = p.ShortDesc, FullDesc = p.FullDesc,
            Level = p.Level, CourseType = p.CourseType, CategoryId = p.CategoryId, SubjectId = p.SubjectId,
            PrimaryFacultyId = p.PrimaryFacultyId, Mrp = p.Mrp, SellingPrice = p.SellingPrice, EnableDefaultFranchiseShare = p.EnableDefaultFranchiseShare, DefaultFranchiseShareType = p.DefaultFranchiseShareType, DefaultFranchiseShareValue = p.DefaultFranchiseShareValue,
            EnableDefaultFacultyShare = p.EnableDefaultFacultyShare, DefaultFacultyShareType = p.DefaultFacultyShareType, DefaultFacultyShareValue = p.DefaultFacultyShareValue,
            // Multi-category & multi-faculty
            CategoryIds = p.ProductCategories.OrderBy(pc => pc.DisplayOrder).Select(pc => pc.CategoryId).ToList(),
            FacultyIds = p.ProductFaculty.OrderBy(pf => pf.IsPrimary ? 0 : 1).Select(pf => pf.FacultyId).ToList(),
            // Primary first so the form's first chip is the primary, and a save round-trips it back
            // unchanged. Falls back to the legacy single column for rows the backfill has not reached.
            SubjectIds = p.ProductSubject.Count > 0
                ? p.ProductSubject.OrderBy(ps => ps.IsPrimary ? 0 : 1).Select(ps => ps.SubjectId).ToList()
                : (p.SubjectId is { } sid ? new List<Guid> { sid } : new List<Guid>()),
            GstRate = p.GstRate, GstInclusive = p.GstInclusive, TotalLectures = p.TotalLectures, TotalHours = p.TotalHours,
            BooksInfo = p.BooksInfo, AdditionalDetails = p.AdditionalDetails,
            Views = p.Views, Validity = p.Validity, Language = p.Language, CourseSchedule = p.CourseSchedule,
            ApplicableAttemptsCsv = p.ApplicableAttempts != null ? string.Join(", ", p.ApplicableAttempts) : null,
            Sku = p.Sku, Gtin = p.Gtin, Tags = p.Tags, AdminComment = p.AdminComment, MarkAsNew = p.MarkAsNew,
            AvailableStartUtc = p.AvailableStartUtc, AvailableEndUtc = p.AvailableEndUtc, ProductCost = p.ProductCost, AllowReviews = p.AllowReviews,
            AllowCustomerPurchase = p.AllowCustomerPurchase,
            // Special price
            SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
            Badge = p.Badge, IsFeatured = p.IsFeatured, DisplayOrder = p.DisplayOrder, HomePageDisplayOrder = p.HomePageDisplayOrder, Status = p.Status,
            BatchStatus = p.BatchStatus,
            LectureAccessTiming = p.LectureAccessTiming,
            NotesDispatchTimeline = p.NotesDispatchTimeline,
            EstimatedDeliveryMessage = p.EstimatedDeliveryMessage,
            SeoTitle = p.SeoTitle, SeoDescription = p.SeoDescription,
            HomeCardImageUrl = p.HomeCardImageUrl,
            OfferImageUrl = p.OfferImageUrl,
            LecturesVideoUrl = p.LecturesVideoUrl, BookPreviewPdfUrl = p.BookPreviewPdfUrl,
            TestimonialVideoUrls = p.TestimonialVideoUrls, RelatedProductSlugs = p.RelatedProductSlugs,
            Faqs = DeserializeFaqs(p.FaqsJson),
            Images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new ProductImageEdit(i.Id, i.ImageUrl, i.AltText, i.Title, i.DisplayOrder, i.IsPrimary)).ToList(),
            Modes = p.Modes.OrderBy(m => m.DisplayOrder).Select(m => new ProductModeEdit { Id = m.Id, ModeName = m.ModeName, ModeType = m.ModeType, Price = m.Price, IsEnabled = m.IsEnabled }).ToList(),
            OptionGroups = p.OptionGroups.OrderBy(g => g.SortOrder).Select(g => new ProductOptionGroupEdit
            {
                Id = g.Id, Name = g.Name, SortOrder = g.SortOrder, IsActive = g.IsActive,
                Items = g.Items.OrderBy(i => i.SortOrder).Select(i => new ProductOptionItemEdit
                {
                    Id = i.Id, Name = i.Name, PriceAddOn = i.PriceAddOn, SortOrder = i.SortOrder, IsActive = i.IsActive
                }).ToList()
            }).ToList(),
            Inclusions = p.Inclusions.OrderBy(i => i.DisplayOrder).Select(i => new ProductInclusionEdit { Icon = i.Icon, Title = i.Title }).ToList(),
            AttributeMappings = p.AttributeMappings.OrderBy(am => am.DisplayOrder).Select(am => new ProductAttributeMappingEdit
            {
                Id = am.Id, ProductAttributeId = am.ProductAttributeId, TextPrompt = am.TextPrompt, IsRequired = am.IsRequired,
                ControlType = am.ControlType, DisplayOrder = am.DisplayOrder,
                Values = am.Values.OrderBy(v => v.DisplayOrder).Select(v => new ProductAttributeValueEdit
                {
                    Id = v.Id, Name = v.Name, PriceAdjustment = v.PriceAdjustment,
                    PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage, IsPreSelected = v.IsPreSelected, DisplayOrder = v.DisplayOrder
                }).ToList()
            }).ToList(),
            SpecificationAttributes = p.SpecificationAttributes.OrderBy(sa => sa.DisplayOrder).Select(sa => new ProductSpecEdit
            {
                Id = sa.Id,
                SpecificationAttributeId = sa.SpecificationAttributeOption != null ? sa.SpecificationAttributeOption.SpecificationAttributeId : Guid.Empty,
                SpecificationAttributeOptionId = sa.SpecificationAttributeOptionId,
                AllowFiltering = sa.AllowFiltering, ShowOnProductPage = sa.ShowOnProductPage, DisplayOrder = sa.DisplayOrder
            }).ToList(),
            Testimonials = p.Testimonials
                .OrderByDescending(t => t.IsFeatured).ThenBy(t => t.DisplayOrder).ThenBy(t => t.CreatedAt)
                .Select(t => new ProductTestimonialEdit
                {
                    Id = t.Id, StudentName = t.StudentName, CourseName = t.CourseName,
                    YoutubeUrl = t.YoutubeUrl, ThumbnailUrl = t.ThumbnailUrl,
                    DisplayOrder = t.DisplayOrder, IsFeatured = t.IsFeatured, IsActive = t.IsActive
                }).ToList()
        };
    }

    public async Task<ProductAttributeMeta> GetAttributeMetaAsync()
    {
        var attrs = await _db.ProductAttributes.OrderBy(a => a.Name)
            .Select(a => new AttrPick(a.Id, a.Name,
                a.PredefinedValues.OrderBy(v => v.DisplayOrder)
                    .Select(v => new PredefinedPick(v.Name, v.PriceAdjustment, v.PriceAdjustmentUsePercentage)).ToList()))
            .ToListAsync();

        var specs = await _db.SpecificationAttributes.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new SpecPick(s.Id, s.Name,
                s.Options.OrderBy(o => o.DisplayOrder).Select(o => new SpecOptionPick(o.Id, o.Name)).ToList()))
            .ToListAsync();

        return new ProductAttributeMeta(attrs, specs);
    }

    public async Task<Guid> SaveAsync(ProductEditModel m)
    {
        _db.ChangeTracker.Clear();
        var attempts = string.IsNullOrWhiteSpace(m.ApplicableAttemptsCsv)
            ? null
            : m.ApplicableAttemptsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var slug = string.IsNullOrWhiteSpace(m.Slug) ? Slugify(m.Title) : Slugify(m.Slug);

        // Global URL check (one public URL = one owner), excluding self. Throws a clear conflict the UI surfaces.
        var url = await _seo.CheckAsync(slug, SeoEntityTypes.Product, m.Id);
        if (!url.IsAvailable)
        {
            var who = url.IsReserved ? "a reserved system route" : $"{url.ExistingEntityType}: {url.ExistingEntityName}";
            throw new InvalidOperationException($"The URL “/{url.NormalizedSlug}” is already used by {who}. Suggested available URL: /{url.SuggestedSlug}");
        }

        Product p;
        bool isNew;
        if (m.Id is Guid id)
        {
            isNew = false;
            p = await _db.Products.Include(x => x.Modes).Include(x => x.Inclusions)
                .Include(x => x.OptionGroups).ThenInclude(g => g.Items)
                .Include(x => x.AttributeMappings).ThenInclude(am => am.Values)
                .Include(x => x.SpecificationAttributes)
                .Include(x => x.ProductCategories)
                .Include(x => x.ProductFaculty)
                .Include(x => x.ProductSubject)
                .FirstAsync(x => x.Id == id);
            // ProductModes & ProductInclusions are UPSERTED in place below — never RemoveRange.
            // CartItem.ProductModeId and OrderItem.ProductModeId hold live FKs to ProductMode.Id;
            // a blind delete trips FK_CartItems_ProductModes_ProductModeId (23503).
            _db.ProductAttributeValues.RemoveRange(p.AttributeMappings.SelectMany(am => am.Values));
            _db.ProductAttributeMappings.RemoveRange(p.AttributeMappings);
            _db.ProductSpecificationAttributes.RemoveRange(p.SpecificationAttributes);
        }
        else
        {
            isNew = true;
            p = new Product { Id = Guid.NewGuid() };
            _db.Products.Add(p);
        }

        p.Title = m.Title; p.Slug = slug; p.ShortDesc = m.ShortDesc; p.FullDesc = m.FullDesc;
        p.Level = m.Level; p.CourseType = m.CourseType; p.CategoryId = m.CategoryId; p.SubjectId = m.SubjectId;
        p.PrimaryFacultyId = m.PrimaryFacultyId; p.Mrp = m.Mrp; p.SellingPrice = m.SellingPrice; p.EnableDefaultFranchiseShare = m.EnableDefaultFranchiseShare; p.DefaultFranchiseShareType = m.DefaultFranchiseShareType; p.DefaultFranchiseShareValue = m.DefaultFranchiseShareValue;
        p.EnableDefaultFacultyShare = m.EnableDefaultFacultyShare; p.DefaultFacultyShareType = m.DefaultFacultyShareType;
        // Zeroed when the default is off, so a disabled default can never be silently re-enabled by a
        // stale value sitting in the column.
        p.DefaultFacultyShareValue = m.EnableDefaultFacultyShare ? m.DefaultFacultyShareValue : 0m;
        p.GstRate = m.GstRate; p.GstInclusive = m.GstInclusive; p.TotalLectures = m.TotalLectures; p.TotalHours = m.TotalHours;
        p.BooksInfo = m.BooksInfo; p.AdditionalDetails = m.AdditionalDetails; p.ApplicableAttempts = attempts;
        p.Views = string.IsNullOrWhiteSpace(m.Views) ? null : m.Views.Trim();
        p.Validity = string.IsNullOrWhiteSpace(m.Validity) ? null : m.Validity.Trim();
        p.Language = string.IsNullOrWhiteSpace(m.Language) ? null : m.Language.Trim();
        p.CourseSchedule = string.IsNullOrWhiteSpace(m.CourseSchedule) ? null : m.CourseSchedule.Trim();
        p.Sku = string.IsNullOrWhiteSpace(m.Sku) ? null : m.Sku.Trim();
        p.Gtin = string.IsNullOrWhiteSpace(m.Gtin) ? null : m.Gtin.Trim();
        p.Tags = string.IsNullOrWhiteSpace(m.Tags) ? null : m.Tags.Trim();
        p.AdminComment = m.AdminComment; p.MarkAsNew = m.MarkAsNew;
        p.AvailableStartUtc = m.AvailableStartUtc; p.AvailableEndUtc = m.AvailableEndUtc;
        p.ProductCost = m.ProductCost; p.AllowReviews = m.AllowReviews;
        p.AllowCustomerPurchase = m.AllowCustomerPurchase;
        p.Badge = m.Badge; p.IsFeatured = m.IsFeatured; p.DisplayOrder = m.DisplayOrder; p.HomePageDisplayOrder = m.HomePageDisplayOrder; p.Status = m.Status;
        p.SeoTitle = m.SeoTitle; p.SeoDescription = m.SeoDescription;
        // HomeCardImageUrl is set via the dedicated upload/delete endpoints, but allow Save to clear it
        // when the admin pastes an empty URL (the editor can also bind directly to it).
        p.HomeCardImageUrl = string.IsNullOrWhiteSpace(m.HomeCardImageUrl) ? null : m.HomeCardImageUrl.Trim();
        p.OfferImageUrl = string.IsNullOrWhiteSpace(m.OfferImageUrl) ? null : m.OfferImageUrl.Trim();
        p.BatchStatus = m.BatchStatus;
        // ── 📦 Estimated Delivery Information — required strings are trimmed; default values fill
        //    in if the admin somehow submits an empty value so the entity's NOT-NULL invariant holds. ──
        p.LectureAccessTiming = string.IsNullOrWhiteSpace(m.LectureAccessTiming) ? "Within 24 Hours" : m.LectureAccessTiming.Trim();
        p.NotesDispatchTimeline = string.IsNullOrWhiteSpace(m.NotesDispatchTimeline) ? "Within 48 Hours" : m.NotesDispatchTimeline.Trim();
        p.EstimatedDeliveryMessage = string.IsNullOrWhiteSpace(m.EstimatedDeliveryMessage) ? null : m.EstimatedDeliveryMessage.Trim();
        p.LecturesVideoUrl = string.IsNullOrWhiteSpace(m.LecturesVideoUrl) ? null : m.LecturesVideoUrl.Trim();
        p.BookPreviewPdfUrl = string.IsNullOrWhiteSpace(m.BookPreviewPdfUrl) ? null : m.BookPreviewPdfUrl.Trim();
        p.TestimonialVideoUrls = string.IsNullOrWhiteSpace(m.TestimonialVideoUrls) ? null : m.TestimonialVideoUrls.Trim();
        p.RelatedProductSlugs = string.IsNullOrWhiteSpace(m.RelatedProductSlugs) ? null : m.RelatedProductSlugs.Trim();
        p.FaqsJson = SerializeFaqs(m.Faqs);
        if (m.Status == ProductStatus.Active && p.PublishedAt == null) p.PublishedAt = DateTime.UtcNow;

        // ── Special Price — audit every change ──────────────────────────────────────────
        // HTML datetime-local gives Kind=Unspecified; Npgsql requires UTC for timestamptz columns.
        var spStart = m.SpecialPriceStartDateUtc?.ToUniversalTime();
        var spEnd = m.SpecialPriceEndDateUtc?.ToUniversalTime();

        var spChanged = p.SpecialPrice != m.SpecialPrice
                     || p.SpecialPriceStartDateUtc != spStart
                     || p.SpecialPriceEndDateUtc != spEnd;
        if (spChanged && !isNew)
        {
            _db.SpecialPriceAudits.Add(new SpecialPriceAudit
            {
                ProductId = p.Id,
                ProductName = m.Title,
                OldPrice = p.SpecialPrice,
                NewPrice = m.SpecialPrice,
                OldStartDate = p.SpecialPriceStartDateUtc,
                NewStartDate = spStart,
                OldEndDate = p.SpecialPriceEndDateUtc,
                NewEndDate = spEnd,
                ModifiedByName = "admin",   // resolved to real name below if available
                Remarks = string.IsNullOrWhiteSpace(m.SpecialPriceRemarks) ? null : m.SpecialPriceRemarks.Trim()
            });
        }
        p.SpecialPrice = m.SpecialPrice;
        p.SpecialPriceStartDateUtc = spStart;
        p.SpecialPriceEndDateUtc = spEnd;

        // ── Multi-Category sync ─────────────────────────────────────────────────────────
        // Keep legacy CategoryId in sync: first selected = primary, or null when empty.
        if (m.CategoryIds.Count > 0) { p.CategoryId = m.CategoryIds[0]; }
        else if (m.CategoryId.HasValue) { /* admin used legacy single-select; keep it */ }
        else { p.CategoryId = null; }

        if (!isNew)
        {
            _db.ProductCategories.RemoveRange(p.ProductCategories);
            p.ProductCategories.Clear();
        }
        for (var ci = 0; ci < m.CategoryIds.Count; ci++)
        {
            p.ProductCategories.Add(new ProductCategory
            {
                ProductId = p.Id,
                CategoryId = m.CategoryIds[ci],
                IsPrimary = ci == 0,
                DisplayOrder = ci + 1
            });
        }

        // ── Multi-Faculty sync ──────────────────────────────────────────────────────────
        // Keep legacy PrimaryFacultyId in sync: first selected = primary, or null when empty.
        if (m.FacultyIds.Count > 0) { p.PrimaryFacultyId = m.FacultyIds[0]; }
        else if (m.PrimaryFacultyId.HasValue) { /* admin used legacy single-select; keep it */ }
        else { p.PrimaryFacultyId = null; }

        if (!isNew)
        {
            _db.ProductFaculty.RemoveRange(p.ProductFaculty);
            p.ProductFaculty.Clear();
        }
        for (var fi = 0; fi < m.FacultyIds.Count; fi++)
        {
            p.ProductFaculty.Add(new ProductFaculty
            {
                ProductId = p.Id,
                FacultyId = m.FacultyIds[fi],
                IsPrimary = fi == 0
            });
        }

        // ── Multi-Subject sync ──────────────────────────────────────────────────────────
        // Product.SubjectId stays the PRIMARY subject: the storefront card and every financial
        // report read it, so it must keep meaning exactly what it meant before. The first selected
        // subject is that primary; the rest exist only in the join table, for browsing.
        //
        // An admin who never touches the new picker sends an empty list — that must not silently
        // clear a subject the product already had, so the legacy column is left alone in that case.
        if (m.SubjectIds.Count > 0) { p.SubjectId = m.SubjectIds[0]; }

        if (m.SubjectIds.Count > 0 || !isNew)
        {
            if (!isNew)
            {
                _db.ProductSubjects.RemoveRange(p.ProductSubject);
                p.ProductSubject.Clear();
            }
            // Distinct(): the unique index would reject a repeat anyway, and failing the whole save
            // over a double-click in the picker would be a poor trade.
            var subjectIds = m.SubjectIds.Count > 0
                ? m.SubjectIds.Distinct().ToList()
                : (p.SubjectId is { } keep ? new List<Guid> { keep } : new List<Guid>());
            for (var si = 0; si < subjectIds.Count; si++)
            {
                p.ProductSubject.Add(new ProductSubject
                {
                    ProductId = p.Id,
                    SubjectId = subjectIds[si],
                    IsPrimary = si == 0
                });
            }
        }

        // ── Faculty share: optionally materialise an explicit rule per attached faculty ──────────
        // When FacultySettings.AutoCreateRuleOnAssign is on, a faculty added to a product with a
        // default share gets a real FacultySharingRule seeded from that default, so the agreement is
        // visible and individually editable instead of implicit. Existing rules are never touched —
        // overwriting a negotiated rate from a product-level default would silently change what
        // someone is paid.
        if (p.EnableDefaultFacultyShare && p.DefaultFacultyShareValue > 0m && m.FacultyIds.Count > 0)
        {
            var autoCreate = await _db.FacultySettings
                .Select(fs => fs.AutoCreateRuleOnAssign).FirstOrDefaultAsync();
            if (autoCreate)
            {
                var alreadyRuled = await _db.FacultySharingRules
                    .Where(r => r.ProductId == p.Id && m.FacultyIds.Contains(r.FacultyId))
                    .Select(r => r.FacultyId).ToListAsync();
                var ruledSet = alreadyRuled.ToHashSet();
                var now = DateTime.UtcNow;

                foreach (var fid in m.FacultyIds.Distinct())
                {
                    if (ruledSet.Contains(fid)) continue;
                    _db.FacultySharingRules.Add(new FacultySharingRule
                    {
                        ProductId = p.Id,
                        FacultyId = fid,
                        ShareType = p.DefaultFacultyShareType,
                        ShareValue = p.DefaultFacultyShareValue,
                        IsActive = true,
                        EffectiveFrom = now
                    });
                }
            }
        }

        // ── Modes ────────────────────────────────────────────────────────────────────────────────
        // New product → straight insert. Existing product → upsert in place keyed by ModeName
        // (the only stable identity the ProductModeEdit DTO carries). Existing rows are MUTATED,
        // so their primary key is preserved and every CartItem.ProductModeId / OrderItem.ProductModeId
        // FK stays valid. Rows the admin removed are deleted ONLY when nothing references them;
        // otherwise they are soft-disabled (IsEnabled=false) so cart and order history stay intact.
        if (isNew)
        {
            var nm = 1;
            p.Modes = m.Modes.Where(x => !string.IsNullOrWhiteSpace(x.ModeName))
                .Select(x => new ProductMode { ModeName = x.ModeName, ModeType = x.ModeType, Price = x.Price, IsEnabled = x.IsEnabled, DisplayOrder = nm++ })
                .ToList();
        }
        else
        {
            var incoming = m.Modes.Where(x => !string.IsNullOrWhiteSpace(x.ModeName))
                .GroupBy(x => x.ModeName.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());
            var seen = new HashSet<string>();
            var nm = 1;
            foreach (var existing in p.Modes.OrderBy(x => x.DisplayOrder).ToList())
            {
                var key = (existing.ModeName ?? "").Trim().ToLowerInvariant();
                if (incoming.TryGetValue(key, out var dto))
                {
                    existing.ModeName = dto.ModeName;
                    existing.ModeType = dto.ModeType;
                    existing.Price = dto.Price;
                    existing.IsEnabled = dto.IsEnabled;
                    existing.DisplayOrder = nm++;
                    seen.Add(key);
                }
                else
                {
                    var mid = existing.Id;
                    var referenced = await _db.CartItems.AnyAsync(c => c.ProductModeId == mid)
                                  || await _db.OrderItems.AnyAsync(o => o.ProductModeId == mid);
                    if (referenced)
                    {
                        existing.IsEnabled = false;       // hide from buybox, keep FK intact
                        existing.DisplayOrder = nm++;
                    }
                    else
                    {
                        p.Modes.Remove(existing);
                        _db.ProductModes.Remove(existing);
                    }
                }
            }
            foreach (var kv in incoming.Where(kv => !seen.Contains(kv.Key)))
            {
                var dto = kv.Value;
                p.Modes.Add(new ProductMode
                {
                    ModeName = dto.ModeName, ModeType = dto.ModeType,
                    Price = dto.Price, IsEnabled = dto.IsEnabled, DisplayOrder = nm++
                });
            }
        }

        // ── Configurable purchase-option groups ───────────────────────────────────────────────────
        // Orders/carts store selected option IDs as JSON (a snapshot), NOT as an FK, so groups/items
        // are safe to delete-and-recreate on save (no 23503 risk like ProductModes has). We upsert
        // by Id where present to keep IDs stable for products the customer may already have in cart.
        {
            var incomingGroups = (m.OptionGroups ?? new())
                .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                .ToList();

            // Remove groups no longer present (and their items cascade).
            var incomingGroupIds = incomingGroups.Where(g => g.Id.HasValue).Select(g => g.Id!.Value).ToHashSet();
            foreach (var existing in p.OptionGroups.ToList())
            {
                if (!incomingGroupIds.Contains(existing.Id))
                {
                    p.OptionGroups.Remove(existing);
                    _db.ProductOptionGroups.Remove(existing);
                }
            }

            int gOrder = 0;
            foreach (var g in incomingGroups)
            {
                var validItems = (g.Items ?? new()).Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
                ProductOptionGroup group;
                if (g.Id.HasValue && (group = p.OptionGroups.FirstOrDefault(x => x.Id == g.Id.Value)!) != null)
                {
                    group.Name = g.Name.Trim();
                    group.SortOrder = gOrder;
                    group.IsActive = g.IsActive;

                    // Upsert items within the group.
                    var incomingItemIds = validItems.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();
                    foreach (var exItem in group.Items.ToList())
                    {
                        if (!incomingItemIds.Contains(exItem.Id))
                        {
                            group.Items.Remove(exItem);
                            _db.ProductOptionGroupItems.Remove(exItem);
                        }
                    }
                    int iOrder = 0;
                    foreach (var i in validItems)
                    {
                        var item = i.Id.HasValue ? group.Items.FirstOrDefault(x => x.Id == i.Id.Value) : null;
                        if (item != null)
                        {
                            item.Name = i.Name.Trim(); item.PriceAddOn = i.PriceAddOn;
                            item.SortOrder = iOrder; item.IsActive = i.IsActive;
                        }
                        else
                        {
                            group.Items.Add(new ProductOptionGroupItem
                            {
                                Name = i.Name.Trim(), PriceAddOn = i.PriceAddOn,
                                SortOrder = iOrder, IsActive = i.IsActive
                            });
                        }
                        iOrder++;
                    }
                }
                else
                {
                    int iOrder = 0;
                    p.OptionGroups.Add(new ProductOptionGroup
                    {
                        Name = g.Name.Trim(), SortOrder = gOrder, IsActive = g.IsActive,
                        Items = validItems.Select(i => new ProductOptionGroupItem
                        {
                            Name = i.Name.Trim(), PriceAddOn = i.PriceAddOn,
                            SortOrder = iOrder++, IsActive = i.IsActive
                        }).ToList()
                    });
                }
                gOrder++;
            }
        }

        // ── Inclusions ──────────────────────────────────────────────────────────────────────────
        // Nothing FK-references inclusions, but we still upsert in place to keep IDs stable.
        if (isNew)
        {
            var ni = 1;
            p.Inclusions = m.Inclusions.Where(x => !string.IsNullOrWhiteSpace(x.Title))
                .Select(x => new ProductInclusion { Icon = x.Icon, Title = x.Title, DisplayOrder = ni++ })
                .ToList();
        }
        else
        {
            var incomingInc = m.Inclusions.Where(x => !string.IsNullOrWhiteSpace(x.Title))
                .GroupBy(x => x.Title.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());
            var seenInc = new HashSet<string>();
            var ni = 1;
            foreach (var existing in p.Inclusions.OrderBy(x => x.DisplayOrder).ToList())
            {
                var key = (existing.Title ?? "").Trim().ToLowerInvariant();
                if (incomingInc.TryGetValue(key, out var dto))
                {
                    existing.Icon = dto.Icon;
                    existing.Title = dto.Title;
                    existing.DisplayOrder = ni++;
                    seenInc.Add(key);
                }
                else
                {
                    p.Inclusions.Remove(existing);
                    _db.ProductInclusions.Remove(existing);
                }
            }
            foreach (var kv in incomingInc.Where(kv => !seenInc.Contains(kv.Key)))
            {
                var dto = kv.Value;
                p.Inclusions.Add(new ProductInclusion { Icon = dto.Icon, Title = dto.Title, DisplayOrder = ni++ });
            }
        }

        var order = 1;
        p.AttributeMappings = m.AttributeMappings
            .Where(am => am.ProductAttributeId != Guid.Empty)
            .Select(am =>
            {
                var vorder = 1;
                return new ProductAttributeMapping
                {
                    ProductAttributeId = am.ProductAttributeId,
                    TextPrompt = string.IsNullOrWhiteSpace(am.TextPrompt) ? null : am.TextPrompt.Trim(),
                    IsRequired = am.IsRequired,
                    ControlType = am.ControlType,
                    DisplayOrder = order++,
                    Values = am.Values.Where(v => !string.IsNullOrWhiteSpace(v.Name))
                        .Select(v => new ProductAttributeValue
                        {
                            Name = v.Name.Trim(), PriceAdjustment = v.PriceAdjustment,
                            PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage,
                            IsPreSelected = v.IsPreSelected, DisplayOrder = vorder++
                        }).ToList()
                };
            }).ToList();

        order = 1;
        p.SpecificationAttributes = m.SpecificationAttributes
            .Where(sa => sa.SpecificationAttributeOptionId != Guid.Empty)
            .Select(sa => new ProductSpecificationAttribute
            {
                SpecificationAttributeOptionId = sa.SpecificationAttributeOptionId,
                AllowFiltering = sa.AllowFiltering, ShowOnProductPage = sa.ShowOnProductPage, DisplayOrder = order++
            }).ToList();

        // Safety net: assert no ProductMode is marked Deleted while a CartItem/OrderItem still points at it.
        // Anything that slipped through above is converted to a soft-disable so the FK constraint cannot trip.
        var deletedModes = _db.ChangeTracker.Entries<ProductMode>()
            .Where(e => e.State == EntityState.Deleted).ToList();
        foreach (var entry in deletedModes)
        {
            var mid = entry.Entity.Id;
            var referenced = await _db.CartItems.AnyAsync(c => c.ProductModeId == mid)
                          || await _db.OrderItems.AnyAsync(o => o.ProductModeId == mid);
            if (referenced)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsEnabled = false;
            }
        }

        // ── Testimonials ── full replace. URL is the only required input now;
        // a blank YouTube URL means "an empty row the admin forgot about" and is dropped.
        var existingTestimonials = await _db.ProductTestimonials.Where(t => t.ProductId == p.Id).ToListAsync();
        if (existingTestimonials.Count > 0) _db.ProductTestimonials.RemoveRange(existingTestimonials);
        var tOrder = 1;
        foreach (var t in m.Testimonials.Where(x => !string.IsNullOrWhiteSpace(x.YoutubeUrl)))
        {
            _db.ProductTestimonials.Add(new ProductTestimonial
            {
                ProductId = p.Id,
                StudentName = string.IsNullOrWhiteSpace(t.StudentName) ? string.Empty : t.StudentName.Trim(),
                CourseName = string.IsNullOrWhiteSpace(t.CourseName) ? null : t.CourseName.Trim(),
                YoutubeUrl = t.YoutubeUrl.Trim(),
                ThumbnailUrl = string.IsNullOrWhiteSpace(t.ThumbnailUrl) ? null : t.ThumbnailUrl.Trim(),
                DisplayOrder = tOrder++,
                IsFeatured = false,
                IsActive = true,
            });
        }

        await _db.SaveChangesAsync();
        // Register/point this product's public URL in the global registry (old URL kept as a 301 on change).
        await _seo.RegisterOrUpdateAsync(SeoEntityTypes.Product, p.Id, p.Title, slug);
        return p.Id;
    }

    public async Task<List<SpecialPriceAuditItem>> GetSpecialPriceAuditAsync(Guid productId)
    {
        return await _db.SpecialPriceAudits.AsNoTracking()
            .Where(a => a.ProductId == productId)
            .OrderByDescending(a => a.ModifiedAt)
            .Select(a => new SpecialPriceAuditItem(
                a.Id, a.OldPrice, a.NewPrice,
                a.OldStartDate, a.NewStartDate,
                a.OldEndDate, a.NewEndDate,
                a.ModifiedByName, a.ModifiedAt, a.Remarks))
            .Take(50)
            .ToListAsync();
    }

    public async Task ToggleStatusAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return;
        p.Status = p.Status == ProductStatus.Active ? ProductStatus.Draft : ProductStatus.Active;
        if (p.Status == ProductStatus.Active && p.PublishedAt == null) p.PublishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Products.Include(x => x.Modes).Include(x => x.Inclusions).Include(x => x.Images).FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return;
        foreach (var img in p.Images) _files.Delete(img.ImageUrl);
        _db.ProductImages.RemoveRange(p.Images);
        _db.ProductModes.RemoveRange(p.Modes);
        _db.ProductInclusions.RemoveRange(p.Inclusions);
        _db.Products.Remove(p);
        await _db.SaveChangesAsync();
        await _seo.ReleaseAsync(SeoEntityTypes.Product, id);   // free the public URL
    }

    // ── Pictures ──
    public Task<List<ProductImageEdit>> ListImagesAsync(Guid productId) =>
        _db.ProductImages.Where(i => i.ProductId == productId).OrderBy(i => i.DisplayOrder)
            .Select(i => new ProductImageEdit(i.Id, i.ImageUrl, i.AltText, i.Title, i.DisplayOrder, i.IsPrimary)).ToListAsync();

    public async Task<Guid> AddImageAsync(Guid productId, string extension, Stream content, string? alt, string? title)
    {
        _db.ChangeTracker.Clear();   // self-contained write: never flush stale state left on the circuit's context
        var url = await _files.SaveAsync("products", extension, content);
        var maxOrder = await _db.ProductImages.Where(i => i.ProductId == productId).Select(i => (int?)i.DisplayOrder).MaxAsync() ?? -1;
        var hasPrimary = await _db.ProductImages.AnyAsync(i => i.ProductId == productId && i.IsPrimary);
        var img = new ProductImage
        {
            ProductId = productId, ImageUrl = url, AltText = alt, Title = title,
            DisplayOrder = maxOrder + 1, IsPrimary = !hasPrimary   // first image becomes primary
        };
        _db.ProductImages.Add(img);
        await _db.SaveChangesAsync();
        return img.Id;
    }

    public async Task DeleteImageAsync(Guid imageId)
    {
        _db.ChangeTracker.Clear();
        var img = await _db.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId);
        if (img == null) return;
        _files.Delete(img.ImageUrl);
        _db.ProductImages.Remove(img);
        await _db.SaveChangesAsync();
        if (img.IsPrimary)   // promote the next picture to primary
        {
            var next = await _db.ProductImages.Where(i => i.ProductId == img.ProductId).OrderBy(i => i.DisplayOrder).FirstOrDefaultAsync();
            if (next != null) { next.IsPrimary = true; await _db.SaveChangesAsync(); }
        }
    }

    // ── Home/Category card image ──
    // A single, dedicated landscape picture used only by storefront cards (Featured / Trending /
    // Category Listing). Stored as a plain URL on Product; doesn't touch the ProductImages gallery.
    public async Task<(bool ok, string? error, string? url)> UploadHomeCardImageAsync(Guid productId, string extension, Stream content)
    {
        _db.ChangeTracker.Clear();
        var ext = (extension ?? "").Trim().ToLowerInvariant().TrimStart('.');
        if (ext is not ("jpg" or "jpeg" or "png" or "webp"))
            return (false, "Unsupported file type. Use JPG, PNG or WEBP.", null);

        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return (false, "Product not found.", null);

        // Delete the previous file (if any) so we don't leave orphans in storage.
        if (!string.IsNullOrEmpty(p.HomeCardImageUrl)) { try { _files.Delete(p.HomeCardImageUrl); } catch { } }

        try
        {
            var url = await _files.SaveAsync("products/cards", ext, content);
            p.HomeCardImageUrl = url;
            await _db.SaveChangesAsync();
            return (true, null, url);
        }
        catch (Exception ex)
        {
            return (false, "Upload failed: " + ex.Message, null);
        }
    }

    public async Task<bool> DeleteHomeCardImageAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return false;
        if (!string.IsNullOrEmpty(p.HomeCardImageUrl)) { try { _files.Delete(p.HomeCardImageUrl); } catch { } }
        p.HomeCardImageUrl = null;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Offer / Profile picture (used by recommendation cards) — same plumbing as HomeCard, separate folder.
    public async Task<(bool ok, string? error, string? url)> UploadOfferImageAsync(Guid productId, string extension, Stream content)
    {
        _db.ChangeTracker.Clear();
        var ext = (extension ?? "").Trim().ToLowerInvariant().TrimStart('.');
        if (ext is not ("jpg" or "jpeg" or "png" or "webp"))
            return (false, "Unsupported file type. Use JPG, PNG or WEBP.", null);

        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return (false, "Product not found.", null);

        if (!string.IsNullOrEmpty(p.OfferImageUrl)) { try { _files.Delete(p.OfferImageUrl); } catch { } }

        try
        {
            var url = await _files.SaveAsync("products/offers", ext, content);
            p.OfferImageUrl = url;
            await _db.SaveChangesAsync();
            return (true, null, url);
        }
        catch (Exception ex)
        {
            return (false, "Upload failed: " + ex.Message, null);
        }
    }

    public async Task<bool> DeleteOfferImageAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (p == null) return false;
        if (!string.IsNullOrEmpty(p.OfferImageUrl)) { try { _files.Delete(p.OfferImageUrl); } catch { } }
        p.OfferImageUrl = null;
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Demo videos (YouTube) ──
    public Task<List<ProductVideoEdit>> ListVideosAsync(Guid productId) =>
        _db.ProductVideos.Where(v => v.ProductId == productId).OrderBy(v => v.DisplayOrder)
            .Select(v => new ProductVideoEdit(v.Id, v.YoutubeUrl, v.Title, v.DisplayOrder)).ToListAsync();

    public async Task<(bool ok, string? error, Guid? id)> AddVideoAsync(Guid productId, string youtubeUrl, string? title)
    {
        _db.ChangeTracker.Clear();
        var url = (youtubeUrl ?? "").Trim();
        if (string.IsNullOrEmpty(url)) return (false, "Enter a YouTube URL.", null);
        if (ExtractYouTubeId(url) == null) return (false, "That doesn't look like a valid YouTube link.", null);
        var maxOrder = await _db.ProductVideos.Where(v => v.ProductId == productId).Select(v => (int?)v.DisplayOrder).MaxAsync() ?? -1;
        var v = new ProductVideo { ProductId = productId, YoutubeUrl = url, Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), DisplayOrder = maxOrder + 1 };
        _db.ProductVideos.Add(v);
        await _db.SaveChangesAsync();
        return (true, null, v.Id);
    }

    public async Task DeleteVideoAsync(Guid videoId)
    {
        _db.ChangeTracker.Clear();
        var v = await _db.ProductVideos.FirstOrDefaultAsync(x => x.Id == videoId);
        if (v == null) return;
        _db.ProductVideos.Remove(v);
        await _db.SaveChangesAsync();
    }

    // Extract the 11-char video id from watch?v= / youtu.be/ / embed/ / shorts/ forms; null if not found.
    private static string? ExtractYouTubeId(string url)
    {
        var m = System.Text.RegularExpressions.Regex.Match(url,
            @"(?:youtu\.be/|youtube\.com/(?:watch\?(?:.*&)?v=|embed/|shorts/|live/))([A-Za-z0-9_-]{11})");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ── Duplicate a product (starts as Draft, SKU/GTIN cleared, pictures not copied to avoid shared files) ──
    public async Task<Guid> CopyProductAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var s = await _db.Products.Include(x => x.Modes).Include(x => x.Inclusions).Include(x => x.Videos)
            .Include(x => x.AttributeMappings).ThenInclude(am => am.Values)
            .Include(x => x.SpecificationAttributes)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Product not found.");

        var copy = new Product
        {
            Id = Guid.NewGuid(),
            Title = s.Title + " (copy)", Slug = await UniqueSlugAsync(Slugify(s.Title + "-copy")),
            ShortDesc = s.ShortDesc, FullDesc = s.FullDesc, Level = s.Level, CourseType = s.CourseType,
            CategoryId = s.CategoryId, SubjectId = s.SubjectId, PrimaryFacultyId = s.PrimaryFacultyId,
            Mrp = s.Mrp, SellingPrice = s.SellingPrice, EnableDefaultFranchiseShare = s.EnableDefaultFranchiseShare, DefaultFranchiseShareType = s.DefaultFranchiseShareType, DefaultFranchiseShareValue = s.DefaultFranchiseShareValue, GstRate = s.GstRate, GstInclusive = s.GstInclusive,
            // The faculty DEFAULT copies; the per-faculty rules deliberately do not. A duplicated
            // product starts with no explicit agreements — cloning someone else's negotiated rate onto
            // a new course would commit the institute to a payout nobody agreed to.
            EnableDefaultFacultyShare = s.EnableDefaultFacultyShare, DefaultFacultyShareType = s.DefaultFacultyShareType, DefaultFacultyShareValue = s.DefaultFacultyShareValue,
            SacCode = s.SacCode, ApplicableAttempts = s.ApplicableAttempts, TotalLectures = s.TotalLectures, TotalHours = s.TotalHours,
            BooksInfo = s.BooksInfo, ExamOrientedInfo = s.ExamOrientedInfo, AdditionalDetails = s.AdditionalDetails,
            Views = s.Views, Validity = s.Validity, Language = s.Language, CourseSchedule = s.CourseSchedule,
            Tags = s.Tags, AdminComment = s.AdminComment, MarkAsNew = s.MarkAsNew, ProductCost = s.ProductCost, AllowReviews = s.AllowReviews,
            AllowCustomerPurchase = s.AllowCustomerPurchase,
            AvailableStartUtc = s.AvailableStartUtc, AvailableEndUtc = s.AvailableEndUtc,
            SeoTitle = s.SeoTitle, SeoDescription = s.SeoDescription, Badge = s.Badge, DisplayOrder = s.DisplayOrder,
            HomePageDisplayOrder = s.HomePageDisplayOrder,
            Status = ProductStatus.Draft,
            Modes = s.Modes.Select(x => new ProductMode { ModeName = x.ModeName, ModeType = x.ModeType, Price = x.Price, IsEnabled = x.IsEnabled, DisplayOrder = x.DisplayOrder }).ToList(),
            Inclusions = s.Inclusions.Select(x => new ProductInclusion { Icon = x.Icon, Title = x.Title, DisplayOrder = x.DisplayOrder }).ToList(),
            Videos = s.Videos.Select(x => new ProductVideo { YoutubeUrl = x.YoutubeUrl, Title = x.Title, DisplayOrder = x.DisplayOrder }).ToList(),
            AttributeMappings = s.AttributeMappings.Select(am => new ProductAttributeMapping
            {
                ProductAttributeId = am.ProductAttributeId, TextPrompt = am.TextPrompt, IsRequired = am.IsRequired,
                ControlType = am.ControlType, DisplayOrder = am.DisplayOrder,
                Values = am.Values.Select(v => new ProductAttributeValue { Name = v.Name, PriceAdjustment = v.PriceAdjustment, PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage, IsPreSelected = v.IsPreSelected, DisplayOrder = v.DisplayOrder }).ToList()
            }).ToList(),
            SpecificationAttributes = s.SpecificationAttributes.Select(sa => new ProductSpecificationAttribute { SpecificationAttributeOptionId = sa.SpecificationAttributeOptionId, AllowFiltering = sa.AllowFiltering, ShowOnProductPage = sa.ShowOnProductPage, DisplayOrder = sa.DisplayOrder }).ToList()
        };
        _db.Products.Add(copy);
        await _db.SaveChangesAsync();
        return copy.Id;
    }

    public Task<List<PurchasedOrderRow>> PurchasedWithOrdersAsync(Guid productId, int take = 50) =>
        _db.Orders.Where(o => o.Items.Any(i => i.ProductId == productId))
            .OrderByDescending(o => o.CreatedAt).Take(take)
            .Select(o => new PurchasedOrderRow(o.Id, o.OrderNumber, o.StudentEmail, o.Status.ToString(), o.PaymentStatus.ToString(), o.CreatedAt))
            .ToListAsync();

    private async Task<string> UniqueSlugAsync(string baseSlug)
    {
        var slug = baseSlug; var n = 1;
        while (await _db.Products.AnyAsync(p => p.Slug == slug)) slug = $"{baseSlug}-{++n}";
        return slug;
    }

    private static readonly System.Text.Json.JsonSerializerOptions _faqJson =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    private static List<FaqEdit> DeserializeFaqs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<FaqEdit>>(json, _faqJson) ?? new(); }
        catch { return new(); }
    }

    private static string? SerializeFaqs(List<FaqEdit>? items)
    {
        var cleaned = (items ?? new())
            .Where(f => !string.IsNullOrWhiteSpace(f.Question) && !string.IsNullOrWhiteSpace(f.Answer))
            .Select(f => new FaqEdit { Question = f.Question.Trim(), Answer = f.Answer.Trim() })
            .ToList();
        return cleaned.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(cleaned, _faqJson);
    }

    private static string Slugify(string s)
    {
        var chars = s.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : (c is ' ' or '-' or '_' ? '-' : '\0'))
            .Where(c => c != '\0').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
