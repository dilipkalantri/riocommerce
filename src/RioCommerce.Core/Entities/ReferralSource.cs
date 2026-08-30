using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;

/// <summary>
/// A "Referred By" option managed under Admin → Promotions. Customers pick one on the checkout page;
/// the chosen name + type + custom text are snapshotted onto each Order so historical reporting stays
/// stable even if the admin later renames or deletes the option.
/// </summary>
public class ReferralSource : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public ReferralSourceType Type { get; set; } = ReferralSourceType.Other;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    /// <summary>Optional CSS colour (hex) the admin can pick to brand the badge later.</summary>
    public string? ColorBadge { get; set; }
    /// <summary>Optional icon hint (Lucide name or emoji) the admin can set; UI-only.</summary>
    public string? Icon { get; set; }
    public string? Description { get; set; }
    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }
    /// <summary>Soft-deleted rows are excluded from queries by a global filter on the DbContext.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
