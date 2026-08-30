using RioCommerce.Core.Enums;
namespace RioCommerce.Core.Entities;
public class FacultySharingRule : BaseEntity
{
    public Guid ProductId { get; set; }
    public Guid FacultyId { get; set; }
    public SharingType ShareType { get; set; }
    public decimal ShareValue { get; set; }
    public decimal? EffectiveAmount { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    /// <summary>When this share configuration takes effect. Optional — null means the rule
    /// has always applied. Honoured by <c>IFacultyShareCalculator</c>, so a rule dated into the
    /// future is configured but not yet earning.</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>When this share configuration stops applying (exclusive). Optional — null means it
    /// runs indefinitely. Lets a rate change be dated without deleting the previous agreement, so a
    /// payout run for an earlier period still resolves the rule that was actually in force.</summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>True when the rule is active AND <paramref name="asOfUtc"/> falls inside its
    /// effective window. This is the single test for "does this rule apply right now" — the payout,
    /// admin grid and product editor all go through it so none of them can disagree.</summary>
    public bool AppliesAt(DateTime asOfUtc) =>
        IsActive
        && (EffectiveFrom is null || asOfUtc >= EffectiveFrom.Value)
        && (EffectiveTo is null || asOfUtc < EffectiveTo.Value);

    public Product Product { get; set; } = null!;
    public Faculty Faculty { get; set; } = null!;
}
