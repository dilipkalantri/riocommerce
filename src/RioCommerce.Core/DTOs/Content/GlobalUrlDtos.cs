namespace RioCommerce.Core.DTOs.Content;

/// <summary>Known public URL-owning entity kinds. Stored as text in the registry.</summary>
public static class SeoEntityTypes
{
    public const string Category = "Category";
    public const string Product = "Product";
    public const string BlogPost = "BlogPost";
    public const string Faculty = "Faculty";
    public const string CmsPage = "CmsPage";
    public const string Redirect = "Redirect";
}

/// <summary>
/// Result of a global URL availability check — tells the admin exactly whether a slug can be
/// used and, if not, who owns it plus the next free suggestion.
/// </summary>
public record GlobalUrlLookupResult(
    bool IsAvailable,
    bool IsReserved,
    string NormalizedSlug,
    string? ExistingEntityType = null,
    Guid? ExistingEntityId = null,
    string? ExistingEntityName = null,
    string? ExistingPublicUrl = null,
    string? ExistingAdminEditUrl = null,
    string? SuggestedSlug = null);

/// <summary>What a resolved root URL points at (used by the central slug resolver).</summary>
public record SeoUrlResolution(string EntityType, Guid EntityId, string NormalizedSlug);
