using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces.Repositories;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace RioCommerce.Infrastructure.Repositories;
public class ProductRepository : GenericRepository<Product>, IProductRepository
{
    public ProductRepository(RioCommerceDbContext context) : base(context) { }
    public async Task<Product?> GetBySlugAsync(string slug) => await _dbSet.Include(p => p.PrimaryFaculty).Include(p => p.Modes).Include(p => p.Inclusions).Include(p => p.Images).Include(p => p.OptionGroups).ThenInclude(g => g.Items).FirstOrDefaultAsync(p => p.Slug == slug && p.Status == ProductStatus.Active);
    public async Task<PagedResult<ProductListItem>> GetFilteredAsync(ProductFilterRequest filter)
    {
        var q = _dbSet.Include(p => p.PrimaryFaculty).Include(p => p.Inclusions).Where(p => p.Status == ProductStatus.Active);
        if (filter.Level.HasValue) q = q.Where(p => p.Level == filter.Level);
        if (filter.CourseType.HasValue) q = q.Where(p => p.CourseType == filter.CourseType);
        // Faculty matches through ProductFaculty, not PrimaryFacultyId: a course taught by several
        // teachers must appear for ANY of them, and this is the same relationship Reports, Orders
        // and the Products admin filter by. Matching only the primary hid co-taught courses from
        // every co-teacher on the storefront.
        //
        // PrimaryFacultyId keeps its other jobs untouched — it is still the course's headline
        // teacher for display, still the faculty-delete guard, and still what the faculty cards
        // count. Only "does this product match the selected faculty filter" changed.
        if (filter.FacultyId.HasValue)
            q = q.Where(p => _context.Set<ProductFaculty>().Any(pf => pf.ProductId == p.Id && pf.FacultyId == filter.FacultyId));
        // Read through ProductSubject for exactly the reason faculty is, just above: a combo course
        // covers several subjects, and matching only the primary hid it from every subject but one.
        // Product.SubjectId keeps its other jobs — it is still the subject shown on the card and
        // still what financial reporting groups by. Only "does this product match the filter" changed.
        if (filter.SubjectId.HasValue)
            q = q.Where(p => _context.Set<ProductSubject>().Any(ps => ps.ProductId == p.Id && ps.SubjectId == filter.SubjectId)
                          || p.SubjectId == filter.SubjectId);
        if (filter.BatchStatus.HasValue) q = q.Where(p => p.BatchStatus == filter.BatchStatus);
        // Category filtering + ordering is driven by the ProductCategory mapping so each category
        // carries its OWN display order (a product can sit at a different position per category).
        List<Guid>? categoryScope = null;
        if (filter.CategoryId.HasValue)
        {
            categoryScope = filter.SearchSubcategories
                ? (await GetCategoryAndDescendantIdsAsync(filter.CategoryId.Value)).ToList()
                : new List<Guid> { filter.CategoryId.Value };
            q = q.Where(p => p.ProductCategories.Any(pc => categoryScope!.Contains(pc.CategoryId)));
        }
        // Case-insensitive partial match across name, short description, tags, subject name,
        // primary faculty name, and applicable attempts. Runs in the database (before Skip/Take).
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            q = q.Where(p =>
                EF.Functions.ILike(p.Title, pattern)
                || (p.ShortDesc != null && EF.Functions.ILike(p.ShortDesc, pattern))
                || (p.Tags != null && EF.Functions.ILike(p.Tags, pattern))
                || (p.Subject != null && EF.Functions.ILike(p.Subject.Name, pattern))
                || (p.PrimaryFaculty != null && EF.Functions.ILike(p.PrimaryFaculty.DisplayName, pattern))
                || (p.ApplicableAttempts != null && p.ApplicableAttempts.Any(a => EF.Functions.ILike(a, pattern))));
        }

        // Spec facets: OR within a spec attribute, AND across attributes (standard faceted filtering).
        if (filter.SpecOptionIds.Count > 0)
        {
            var groups = await _context.Set<SpecificationAttributeOption>()
                .Where(o => filter.SpecOptionIds.Contains(o.Id))
                .GroupBy(o => o.SpecificationAttributeId)
                .Select(g => g.Select(o => o.Id).ToList())
                .ToListAsync();
            foreach (var optionIds in groups)
                q = q.Where(p => p.SpecificationAttributes.Any(psa => psa.AllowFiltering && optionIds.Contains(psa.SpecificationAttributeOptionId)));
        }

        var total = await q.CountAsync();

        // Category page → order strictly by the current category's ProductCategory.DisplayOrder ASC,
        // then Product.Name ASC (ties allowed). Non-category listings keep the global product order.
        // Ordering happens in the database BEFORE Skip/Take so pagination is correct.
        var ordered = categoryScope != null
            ? q.OrderBy(p => p.ProductCategories.Where(pc => categoryScope!.Contains(pc.CategoryId)).Min(pc => (int?)pc.DisplayOrder))
                .ThenBy(p => p.Title)
            : q.OrderBy(p => p.DisplayOrder);

        var items = await ordered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(p => new ProductListItem
            {
                Id = p.Id, Title = p.Title, Slug = p.Slug, Level = p.Level, CourseType = p.CourseType,
                FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                Mrp = p.Mrp, SellingPrice = p.SellingPrice,
                SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
                Badge = p.Badge,
                TotalOrders = p.TotalOrders, AvgRating = p.AvgRating, RatingCount = p.RatingCount, Status = p.Status,
                BatchStatus = p.BatchStatus,
                HomeCardImageUrl = p.HomeCardImageUrl,
                PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                  ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
                InclusionTitles = p.Inclusions.OrderBy(i => i.DisplayOrder).Select(i => i.Title).ToList()
            }).ToListAsync();
        return new PagedResult<ProductListItem> { Items = items, TotalCount = total, Page = filter.Page, PageSize = filter.PageSize };
    }
    public async Task<ProductDetailResponse?> GetDetailBySlugAsync(string slug)
    {
        var fetched = await _dbSet.Where(p => p.Slug == slug && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                Dto = new ProductDetailResponse { Id = p.Id, Title = p.Title, Slug = p.Slug, Level = p.Level, CourseType = p.CourseType, ShortDesc = p.ShortDesc, FullDesc = p.FullDesc, FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null, FacultyBio = p.PrimaryFaculty != null ? p.PrimaryFaculty.Bio : null,
                    FacultyCode               = p.PrimaryFaculty != null ? p.PrimaryFaculty.ShortCode           : null,
                    FacultyPhotoUrl           = p.PrimaryFaculty != null ? p.PrimaryFaculty.PhotoUrl            : null,
                    FacultyDesignation        = p.PrimaryFaculty != null ? p.PrimaryFaculty.Designation         : null,
                    FacultyShortDescription   = p.PrimaryFaculty != null ? p.PrimaryFaculty.ShortDescription    : null,
                    FacultyYearsOfExperience  = p.PrimaryFaculty != null ? p.PrimaryFaculty.YearsOfExperience   : null,
                    FacultyStudentsTaught     = p.PrimaryFaculty != null ? p.PrimaryFaculty.StudentsTaught      : null, Mrp = p.Mrp, SellingPrice = p.SellingPrice,
                    SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
                    GstRate = p.GstRate, Badge = p.Badge, TotalOrders = p.TotalOrders, AvgRating = p.AvgRating, RatingCount = p.RatingCount, TotalLectures = p.TotalLectures, TotalHours = p.TotalHours, BooksInfo = p.BooksInfo, AdditionalDetails = p.AdditionalDetails, Views = p.Views, Validity = p.Validity, Language = p.Language, CourseSchedule = p.CourseSchedule, ApplicableAttempts = p.ApplicableAttempts, BatchStartDate = p.BatchStartDate, SeoTitle = p.SeoTitle, SeoDescription = p.SeoDescription, LecturesVideoUrl = p.LecturesVideoUrl, BookPreviewPdfUrl = p.BookPreviewPdfUrl, BatchStatus = p.BatchStatus, AllowCustomerPurchase = p.AllowCustomerPurchase, LectureAccessTiming = p.LectureAccessTiming, NotesDispatchTimeline = p.NotesDispatchTimeline, EstimatedDeliveryMessage = p.EstimatedDeliveryMessage,
                    Modes = p.Modes.Where(m => m.IsEnabled).Select(m => new ProductModeDto(m.Id, m.ModeName, m.ModeType.ToString(), m.Price, m.IsEnabled)).ToList(),
                    OptionGroups = p.OptionGroups.Where(g => g.IsActive).OrderBy(g => g.SortOrder)
                        .Select(g => new ProductOptionGroupDto(g.Id, g.Name,
                            g.Items.Where(i => i.IsActive).OrderBy(i => i.SortOrder)
                                .Select(i => new ProductOptionItemDto(i.Id, i.Name, i.PriceAddOn)).ToList()))
                        .ToList(),
                    Inclusions = p.Inclusions.OrderBy(i => i.DisplayOrder).Select(i => new ProductInclusionDto(i.Icon, i.Title)).ToList(),
                    Images = p.Images.OrderBy(i => i.DisplayOrder).Select(i => new ProductImageDto(i.ImageUrl, i.AltText ?? "", i.IsPrimary)).ToList(),
                    Videos = p.Videos.OrderBy(v => v.DisplayOrder).Select(v => new ProductVideoDto(v.YoutubeUrl, v.Title)).ToList(),
                    Attributes = p.AttributeMappings.OrderBy(am => am.DisplayOrder).Select(am => new ProductAttributeDto(
                        am.Id, am.ProductAttribute.Name, am.ControlType.ToString(), am.TextPrompt, am.IsRequired,
                        am.Values.OrderBy(v => v.DisplayOrder).Select(v => new ProductAttributeValueDto(v.Id, v.Name, v.PriceAdjustment, v.PriceAdjustmentUsePercentage, v.IsPreSelected)).ToList())).ToList(),
                    Specifications = p.SpecificationAttributes.Where(sa => sa.ShowOnProductPage).OrderBy(sa => sa.DisplayOrder).Select(sa => new ProductSpecDto(
                        sa.SpecificationAttributeOption.SpecificationAttribute.Group != null ? sa.SpecificationAttributeOption.SpecificationAttribute.Group.Name : null,
                        sa.SpecificationAttributeOption.SpecificationAttribute.Name, sa.SpecificationAttributeOption.Name, sa.SpecificationAttributeOption.ColorSquaresRgb)).ToList()
                },
                TestimonialsRaw = p.TestimonialVideoUrls,
                FaqsRaw = p.FaqsJson,
                RelatedSlugsRaw = p.RelatedProductSlugs
            })
            .FirstOrDefaultAsync();
        if (fetched == null) return null;
        var dto = fetched.Dto;

        // Parse newline-/comma-separated testimonial YouTube URLs.
        if (!string.IsNullOrWhiteSpace(fetched.TestimonialsRaw))
            dto.TestimonialVideoUrls = fetched.TestimonialsRaw
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // Parse FAQs JSON array of {question, answer}.
        if (!string.IsNullOrWhiteSpace(fetched.FaqsRaw))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(fetched.FaqsRaw);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var q = el.TryGetProperty("question", out var qe) ? qe.GetString() : null;
                        var a = el.TryGetProperty("answer", out var ae) ? ae.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(a))
                            dto.Faqs.Add(new FaqItem(q!, a!));
                    }
            }
            catch { /* malformed JSON → no FAQs (don't fail the page) */ }
        }

        // Resolve related products by slug (skip self, only Active).
        if (!string.IsNullOrWhiteSpace(fetched.RelatedSlugsRaw))
        {
            var slugs = fetched.RelatedSlugsRaw
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.Equals(s, slug, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (slugs.Length > 0)
                dto.RelatedProducts = await _dbSet
                    .Where(p => slugs.Contains(p.Slug) && p.Status == ProductStatus.Active)
                    .Select(p => new ProductListItem
                    {
                        Id = p.Id, Title = p.Title, Slug = p.Slug, Level = p.Level, CourseType = p.CourseType,
                        FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
                        Mrp = p.Mrp, SellingPrice = p.SellingPrice,
                        SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
                        Badge = p.Badge,
                        TotalOrders = p.TotalOrders, AvgRating = p.AvgRating, RatingCount = p.RatingCount, Status = p.Status,
                        BatchStatus = p.BatchStatus,
                        HomeCardImageUrl = p.HomeCardImageUrl,
                        PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                          ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault()
                    }).ToListAsync();
        }

        // Premium testimonials — featured first, then by display order.
        var testimonialRows = await _context.Set<ProductTestimonial>()
            .Where(t => t.ProductId == dto.Id && t.IsActive)
            .OrderByDescending(t => t.IsFeatured)
            .ThenBy(t => t.DisplayOrder)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
        foreach (var t in testimonialRows)
        {
            var (vid, isShorts) = YouTubeUrlHelpers.Parse(t.YoutubeUrl);
            if (vid == null) continue;
            dto.Testimonials.Add(new ProductTestimonialPublic(
                t.Id, t.StudentName, t.CourseName, vid, isShorts,
                YouTubeUrlHelpers.ResolveThumbnail(t.ThumbnailUrl, vid),
                YouTubeUrlHelpers.FallbackThumbnail(vid),
                t.IsFeatured));
        }

        return dto;
    }
    // Homepage "Trending Courses": Published (Active) + Show-on-home-page (IsFeatured), ordered strictly by
    // HomePageDisplayOrder ASC then Name ASC — applied BEFORE Take() so the configured order wins the cut.
    public async Task<List<ProductListItem>> GetFeaturedAsync(int count = 8) => await _dbSet.Where(p => p.IsFeatured && p.Status == ProductStatus.Active).OrderBy(p => p.HomePageDisplayOrder).ThenBy(p => p.Title).Take(count)
        .Select(p => new ProductListItem
        {
            Id = p.Id, Title = p.Title, Slug = p.Slug, Level = p.Level, CourseType = p.CourseType,
            FacultyName = p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null,
            Mrp = p.Mrp, SellingPrice = p.SellingPrice,
            SpecialPrice = p.SpecialPrice, SpecialPriceStartDateUtc = p.SpecialPriceStartDateUtc, SpecialPriceEndDateUtc = p.SpecialPriceEndDateUtc,
            Badge = p.Badge,
            TotalOrders = p.TotalOrders, AvgRating = p.AvgRating, RatingCount = p.RatingCount, Status = p.Status,
            BatchStatus = p.BatchStatus,
            HomeCardImageUrl = p.HomeCardImageUrl,
            PrimaryImageUrl = p.Images.Where(i => i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                              ?? p.Images.OrderBy(i => i.DisplayOrder).Select(i => i.ImageUrl).FirstOrDefault(),
            InclusionTitles = p.Inclusions.OrderBy(i => i.DisplayOrder).Select(i => i.Title).ToList()
        }).ToListAsync();

    // Returns the category id plus every descendant id (categories form a shallow tree, so an in-memory walk is fine).
    private async Task<HashSet<Guid>> GetCategoryAndDescendantIdsAsync(Guid rootId)
    {
        var edges = await _context.Set<Category>().AsNoTracking()
            .Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var childrenOf = edges.ToLookup(e => e.ParentId);
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in childrenOf[current])
                if (result.Add(child.Id)) queue.Enqueue(child.Id);
        }
        return result;
    }

    public async Task<List<ProductSuggestion>> SuggestAsync(string q, int take = 6)
    {
        if (string.IsNullOrWhiteSpace(q)) return new();
        return await _dbSet.Where(p => p.Status == ProductStatus.Active && EF.Functions.ILike(p.Title, $"%{q.Trim()}%"))
            .OrderByDescending(p => p.IsFeatured).ThenBy(p => p.DisplayOrder).Take(take)
            .Select(p => new ProductSuggestion(p.Id, p.Title, p.Slug, p.Level, p.PrimaryFaculty != null ? p.PrimaryFaculty.DisplayName : null))
            .ToListAsync();
    }
}
