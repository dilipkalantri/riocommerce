namespace RioCommerce.Core.Entities;

// A YouTube demo/preview video attached to a product, shown in the storefront media gallery
// after the product images. URL-based (no upload); the id is extracted at display time.
public class ProductVideo : BaseEntity
{
    public Guid ProductId { get; set; }
    public string YoutubeUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
}
