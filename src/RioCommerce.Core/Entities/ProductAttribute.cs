namespace RioCommerce.Core.Entities;

// A reusable product attribute definition (e.g. "Validity", "Language"). Catalog → Attributes → Product Attributes.
public class ProductAttribute : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Optional predefined values that can be quickly copied onto a product when mapping this attribute.
    public ICollection<PredefinedProductAttributeValue> PredefinedValues { get; set; } = new List<PredefinedProductAttributeValue>();
}
