using RioCommerce.Core.Enums;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Products;
namespace RioCommerce.Core.Interfaces;

public interface IProductAdminService
{
    Task<PagedResult<ProductListItem>> ListAsync(ProductFilterRequest filter);   // all statuses

    /// <summary>
    /// Every field of every product the filter selects, as a .xlsx workbook.
    ///
    /// <para>The same <paramref name="filter"/> the grid is showing, so a download always matches
    /// what is on screen — but unpaged: filtering to 60 products and exporting gives all 60, not
    /// the 20 on the current page.</para>
    ///
    /// <para>Read-only. Nothing about the products is changed by exporting them.</para>
    /// </summary>
    Task<byte[]> ExportExcelAsync(ProductFilterRequest filter, CancellationToken ct = default);
    Task<AdminProductStats> StatsAsync();
    /// <summary>
    /// Dropdown data for the product search panel.
    ///
    /// <para>Pass what is already picked and Category, Subject and Faculty each narrow to what the
    /// OTHER selections allow — no dimension applies its own, so a different value can always be
    /// chosen. Faculty resolves through <c>ProductFaculty</c>, so a co-taught course keeps every one
    /// of its teachers on the list. Pass nothing and the lists are exactly what they always were.</para>
    ///
    /// <para>This narrows the SEARCH panel only. What a product may be assigned to when editing it
    /// is untouched — the editor calls this too, and always without arguments.</para>
    /// </summary>
    Task<ProductFilterMeta> GetFilterMetaAsync(
        Guid? categoryId = null, Guid? subjectId = null, Guid? facultyId = null, CourseLevel? level = null);
    Task<Guid?> FindBySkuAsync(string sku);                       // exact SKU lookup → product id
    Task<ProductEditModel?> GetForEditAsync(Guid id);
    Task<ProductAttributeMeta> GetAttributeMetaAsync();   // dropdown data for the Attributes tab
    Task<Guid> SaveAsync(ProductEditModel model);   // create when Id is null, else update
    /// <summary>Special price audit trail for a single product.</summary>
    Task<List<SpecialPriceAuditItem>> GetSpecialPriceAuditAsync(Guid productId);
    Task ToggleStatusAsync(Guid id);
    Task DeleteAsync(Guid id);

    // ── Pictures ──
    Task<List<ProductImageEdit>> ListImagesAsync(Guid productId);
    Task<Guid> AddImageAsync(Guid productId, string extension, Stream content, string? alt, string? title);
    Task DeleteImageAsync(Guid imageId);

    // ── Home/Category card image (single dedicated picture; orthogonal to the gallery) ──
    Task<(bool ok, string? error, string? url)> UploadHomeCardImageAsync(Guid productId, string extension, Stream content);
    Task<bool> DeleteHomeCardImageAsync(Guid productId);
    // ── Offer/Profile picture (used by combo recommendations) ──
    Task<(bool ok, string? error, string? url)> UploadOfferImageAsync(Guid productId, string extension, Stream content);
    Task<bool> DeleteOfferImageAsync(Guid productId);

    // ── Demo videos (YouTube) ──
    Task<List<ProductVideoEdit>> ListVideosAsync(Guid productId);
    Task<(bool ok, string? error, Guid? id)> AddVideoAsync(Guid productId, string youtubeUrl, string? title);
    Task DeleteVideoAsync(Guid videoId);

    // ── Operations ──
    Task<Guid> CopyProductAsync(Guid id);
    Task<List<PurchasedOrderRow>> PurchasedWithOrdersAsync(Guid productId, int take = 50);
}
