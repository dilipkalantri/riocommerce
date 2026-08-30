using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Meta;
namespace RioCommerce.Core.Interfaces;

public interface ICatalogAdminService
{
    // Categories
    Task<List<CategoryAdminItem>> ListCategoriesAsync();
    Task<CategoryEditModel?> GetCategoryAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveCategoryAsync(CategoryEditModel model);
    Task ToggleCategoryAsync(Guid id);
    Task<(bool ok, string? error)> DeleteCategoryAsync(Guid id);

    // Category editor support
    Task<List<IdName>> GetCategoryParentOptionsAsync(Guid? excludeId);   // valid parents (no self/descendants)
    Task<string> UploadCategoryImageAsync(string extension, Stream content);
    Task<List<CategoryProductRow>> ListCategoryProductsAsync(Guid categoryId);
    Task<List<IdName>> SearchUnassignedProductsAsync(Guid categoryId, string? query, int take = 20);
    Task AssignProductAsync(Guid categoryId, Guid productId);
    Task UnassignProductAsync(Guid categoryId, Guid productId);
    // Persists the per-category display order onto the ProductCategory mapping rows for this category only.
    Task SaveCategoryProductOrderAsync(Guid categoryId, IReadOnlyList<CategoryProductOrder> orders);

    // Faculty
    Task<List<FacultyAdminItem>> ListFacultyAsync();
    Task<FacultyEditModel?> GetFacultyAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveFacultyAsync(FacultyEditModel model);
    Task ToggleFacultyAsync(Guid id);
    Task<(bool ok, string? error)> DeleteFacultyAsync(Guid id);
    /// <summary>Saves an uploaded faculty photo to public file storage and returns its public URL.</summary>
    Task<(bool ok, string? error, string? url)> UploadFacultyPhotoAsync(string extension, Stream content);
}
