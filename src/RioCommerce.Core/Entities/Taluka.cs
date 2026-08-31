namespace RioCommerce.Core.Entities;

public class Taluka : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid DistrictId { get; set; }
    public bool IsActive { get; set; } = true;
    public District District { get; set; } = null!;
}
