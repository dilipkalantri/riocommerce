namespace RioCommerce.Core.Entities;

public class State : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public ICollection<District> Districts { get; set; } = new List<District>();
}
