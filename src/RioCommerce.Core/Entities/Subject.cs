using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class Subject : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public CourseLevel? Level { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
