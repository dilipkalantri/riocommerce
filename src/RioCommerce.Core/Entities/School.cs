using RioCommerce.Core.Enums;

namespace RioCommerce.Core.Entities;

public class School : BaseEntity
{
    public string UdiseCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public SchoolType SchoolType { get; set; }
    public int LowestClass { get; set; }
    public int HighestClass { get; set; }
    public string? CityOrVillage { get; set; }
    public Guid? TalukaId { get; set; }
    public Guid? DistrictId { get; set; }
    public Guid? StateId { get; set; }
    public string? PinCode { get; set; }
    public bool IsActive { get; set; } = true;

    public Taluka? Taluka { get; set; }
    public District? District { get; set; }
    public State? State { get; set; }
    public ICollection<SchoolUser> SchoolUsers { get; set; } = new List<SchoolUser>();
}
