namespace RioCommerce.Core.Entities;

/// <summary>Maps an old nopCommerce Product.Id to the new product Guid. Permanent cross-system
/// trace + idempotency key for re-running the importer.</summary>
public class LegacyProductMap : BaseEntity
{
    public int LegacyId { get; set; }
    public Guid NewId { get; set; }
    public string? LegacyKey { get; set; }   // old Sku
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Maps an old nopCommerce Customer.Id to the new user Guid.</summary>
public class LegacyUserMap : BaseEntity
{
    public int LegacyId { get; set; }
    public Guid NewId { get; set; }
    public string? LegacyKey { get; set; }   // old email
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
