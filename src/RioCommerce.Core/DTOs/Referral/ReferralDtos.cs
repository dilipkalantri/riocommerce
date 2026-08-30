using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Referral;

/// <summary>Admin grid row.</summary>
public record ReferralSourceAdminItem(
    Guid Id, string Name, ReferralSourceType Type, int DisplayOrder, bool IsActive, bool IsDefault,
    string? ColorBadge, DateTime CreatedAt);

/// <summary>Edit form model — same shape for create and update.</summary>
public class ReferralSourceEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ReferralSourceType Type { get; set; } = ReferralSourceType.Other;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public string? ColorBadge { get; set; }
    public string? Icon { get; set; }
    public string? Description { get; set; }
}

/// <summary>Public-facing option used by the storefront checkout dropdown.</summary>
public record ReferralSourceOption(Guid Id, string Name, ReferralSourceType Type, string? Icon, string? ColorBadge);
