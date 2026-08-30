namespace RioCommerce.Core.DTOs.Content;

// A node in the mega-menu tree (top-level items have children; children render as a dropdown).
public record MenuItemNode(
    Guid Id, Guid? ParentId, string Title, string Url, bool IsEnabled, bool OpenInNewTab,
    int DisplayOrder, List<MenuItemNode> Children);

// A CMS page available to add as a menu item.
public record MenuPagePick(Guid Id, string Title, string Slug);

// One node in a bulk reorder payload: where it now sits (ParentId) and its position among siblings.
public record MenuOrderItem(Guid Id, Guid? ParentId, int DisplayOrder);
