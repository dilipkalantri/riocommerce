namespace RioCommerce.Core.Entities;

// Optional grouping for specification attributes (e.g. "Course Details", "Delivery"). nopCommerce parity.
public class SpecificationAttributeGroup : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public ICollection<SpecificationAttribute> SpecificationAttributes { get; set; } = new List<SpecificationAttribute>();
}
