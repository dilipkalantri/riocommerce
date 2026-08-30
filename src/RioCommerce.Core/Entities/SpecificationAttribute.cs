namespace RioCommerce.Core.Entities;

// A specification attribute definition (e.g. "Medium", "Faculty", "Validity") shown on product pages / used for filtering.
public class SpecificationAttribute : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public Guid? SpecificationAttributeGroupId { get; set; }
    public SpecificationAttributeGroup? Group { get; set; }
    public ICollection<SpecificationAttributeOption> Options { get; set; } = new List<SpecificationAttributeOption>();
}
