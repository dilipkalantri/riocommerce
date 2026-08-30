namespace RioCommerce.Core.Entities;

/// <summary>
/// Global, platform-wide faculty revenue-share behaviour (a single row). The faculty counterpart of
/// <see cref="FranchiseSettings"/>.
///
/// <para>The one structural difference from the franchise side: a product can carry MANY faculty,
/// each on their own share, so these settings also govern how the COMBINED share across a product's
/// faculty is bounded. <see cref="MaxTotalSharePct"/> is the configurable business ceiling checked
/// when an admin saves; separately and unconditionally, the calculator caps the sum of the bare
/// shares at the product's taxable base, because the institute cannot pay out more than the sale is
/// worth no matter what the rules say.</para>
/// </summary>
public class FacultySettings : BaseEntity
{
    /// <summary>Apply the share on the special price while it's active (else always on the regular
    /// price). Same meaning as <see cref="FranchiseSettings.ApplyShareOnSpecialPrice"/>.</summary>
    public bool ApplyShareOnSpecialPrice { get; set; } = true;

    /// <summary>Allow an explicit per-faculty <see cref="FacultySharingRule"/> to take precedence
    /// over the product's default faculty share. When false the product default always wins,
    /// regardless of any rule row — the mirror of
    /// <see cref="FranchiseSettings.AllowFranchiseSpecificOverride"/>.</summary>
    public bool AllowFacultyRuleOverride { get; set; } = true;

    /// <summary>Ceiling on the SUM of all active percentage shares for one product, checked at save
    /// time so an admin cannot commit 3 faculty × 60%. Fixed ₹ shares are converted to their
    /// equivalent percentage of the taxable base for this check.</summary>
    public decimal MaxTotalSharePct { get; set; } = 100.00m;

    /// <summary>When false, exceeding <see cref="MaxTotalSharePct"/> is reported as a warning
    /// instead of blocking the save. The taxable-base cap in the calculator still applies.</summary>
    public bool EnforceMaxTotalShare { get; set; } = true;

    /// <summary>When a faculty is attached to a product and that product has a default faculty share
    /// enabled, materialise an explicit rule row so the share is visible and individually editable
    /// rather than implicit.</summary>
    public bool AutoCreateRuleOnAssign { get; set; } = false;

    /// <summary>TDS (s.194J) is deducted on the professional fee only, not on the GST charged on top
    /// of it — the standard treatment when tax is shown separately on the invoice. Turning this off
    /// deducts TDS from the gross payout instead.</summary>
    public bool TdsOnShareExcludingGst { get; set; } = true;
}
