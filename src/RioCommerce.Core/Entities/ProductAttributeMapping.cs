using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// Links a ProductAttribute to a specific Product, with the control type and per-product values/prompt.
public class ProductAttributeMapping : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid ProductAttributeId { get; set; }
    public string? TextPrompt { get; set; }
    public bool IsRequired { get; set; }
    public AttributeControlType ControlType { get; set; } = AttributeControlType.DropdownList;
    public string? DefaultValue { get; set; }   // used by TextBox/MultilineTextBox/Datepicker controls
    public int DisplayOrder { get; set; }
    public Product Product { get; set; } = null!;
    public ProductAttribute ProductAttribute { get; set; } = null!;
    public ICollection<ProductAttributeValue> Values { get; set; } = new List<ProductAttributeValue>();
}
