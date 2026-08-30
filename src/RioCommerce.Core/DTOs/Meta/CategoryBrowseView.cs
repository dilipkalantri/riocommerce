namespace RioCommerce.Core.DTOs.Meta;

// Lightweight public view of a category for the storefront /category/{slug} browse page.
// Subjects/Faculty are the filter options available for THIS category — derived from the
// category's full active product set (not from any current search/filter/paginated slice).
// Children are the direct active sub-categories used for the on-page sub-category navigation.
public record CategoryBrowseView(
    Guid Id, string Name, string Slug, string? Description, string? ImageUrl,
    List<IdName> Subjects, List<IdName> Faculty,
    Guid? ParentId = null,
    List<CategoryChildView>? Children = null,
    // When the current page is a leaf child rendering sibling navigation, this is the sibling to highlight.
    Guid? ActiveChildId = null);

// Minimal storefront projection of a direct child category (no admin-only fields) for the
// sub-category navigation cards. Slug feeds the flat global /{slug} route.
public record CategoryChildView(Guid Id, string Name, string Slug, string? Description, int DisplayOrder);
