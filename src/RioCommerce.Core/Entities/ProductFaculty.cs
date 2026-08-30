namespace RioCommerce.Core.Entities;
public class ProductFaculty : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid FacultyId { get; set; }
    public bool IsPrimary { get; set; }
    public Product Product { get; set; } = null!;
    public Faculty Faculty { get; set; } = null!;
}
