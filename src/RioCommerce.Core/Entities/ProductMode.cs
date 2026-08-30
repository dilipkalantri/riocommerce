using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class ProductMode : BaseEntity
{
    public Guid ProductId { get; set; }
    public string ModeName { get; set; } = string.Empty;
    public LectureMode ModeType { get; set; }
    public decimal Price { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
}
