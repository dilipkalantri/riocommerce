namespace RioCommerce.Core.Entities;

public class District : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid StateId { get; set; }
    public bool IsActive { get; set; } = true;
    public State State { get; set; } = null!;
    public ICollection<Taluka> Talukas { get; set; } = new List<Taluka>();
}
