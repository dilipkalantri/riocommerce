using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

// An attribute collected during checkout (e.g. "Need printed notes shipped?", "GST invoice name").
public class CheckoutAttribute : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? TextPrompt { get; set; }
    public bool IsRequired { get; set; }
    public AttributeControlType ControlType { get; set; } = AttributeControlType.DropdownList;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CheckoutAttributeValue> Values { get; set; } = new List<CheckoutAttributeValue>();
}
