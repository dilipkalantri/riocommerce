namespace RioCommerce.Core.Entities;
public class ProductInclusion : BaseEntity
{
    public Guid ProductId { get; set; }
    public string? Icon { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
}
