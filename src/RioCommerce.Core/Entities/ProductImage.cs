namespace RioCommerce.Core.Entities;
public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? AltText { get; set; }
    public string? Title { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
    public Product Product { get; set; } = null!;
}
