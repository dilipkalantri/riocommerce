using RioCommerce.Core.DTOs.Content;
namespace RioCommerce.Core.Interfaces;

// Builds and serves the storefront mega menu — a self-referencing tree of unlimited depth.
public interface IMenuAdminService
{
    Task<List<MenuItemNode>> GetTreeAsync(bool enabledOnly = false);
    Task<List<MenuPagePick>> AvailablePagesAsync();
    Task<List<MenuPagePick>> AvailableCategoriesAsync();

    Task AddPageItemAsync(Guid pageId, Guid? parentId);
    Task AddCategoryItemAsync(Guid categoryId, Guid? parentId);
    Task AddCustomItemAsync(string title, string url, Guid? parentId);
    Task<(bool ok, string? error)> UpdateItemAsync(Guid id, string title, string url, bool isEnabled, bool openInNewTab);
    Task MoveAsync(Guid id, bool up);
    Task IndentAsync(Guid id);    // top-level item → child of the previous top-level item
    Task OutdentAsync(Guid id);   // child → top-level
    Task DeleteAsync(Guid id);    // also removes ALL descendants (recursive)

    // Bulk reorder/reparent for the whole tree. Transactional; validates parents exist, no cycles,
    // no self-parenting, no duplicates. Rolls everything back if any check fails.
    Task<(bool ok, string? error)> SaveTreeOrderAsync(IReadOnlyList<MenuOrderItem> items);
}
