namespace RioCommerce.Core.Entities;

// A predefined option of a SpecificationAttribute (e.g. for "Medium": "Hindi", "English").
public class SpecificationAttributeOption : BaseEntity
{
    public Guid SpecificationAttributeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorSquaresRgb { get; set; }
    public int DisplayOrder { get; set; }
    public SpecificationAttribute SpecificationAttribute { get; set; } = null!;
    public ICollection<ProductSpecificationAttribute> ProductMappings { get; set; } = new List<ProductSpecificationAttribute>();
}
