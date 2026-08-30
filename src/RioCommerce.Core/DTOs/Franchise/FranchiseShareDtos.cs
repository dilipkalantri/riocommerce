using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Franchise;

/// <summary>
/// Where a resolved franchise share came from. Drives the "Share Source" column in both the
/// admin assigned-products grid and the franchise portal product-shares listing.
/// </summary>
public enum ShareSource
{
    /// <summary>No share applies (neither an active override nor an enabled product default).</summary>
    None = 0,
    /// <summary>The product's own Default Franchise Share was used.</summary>
    ProductDefault = 1,
    /// <summary>A per-franchise override (FranchiseCommission row, set via Bulk Assign) was used.</summary>
    FranchiseOverride = 2
}

/// <summary>
/// The complete, self-contained result of pricing + share resolution for ONE (product, franchise)
/// pair at a point in time. Produced by <c>IFranchiseShareCalculator</c> and surfaced verbatim by
/// the franchise portal API. Every value here is safe to show a franchisee — it deliberately
/// excludes MRP, cost, admin comments and any internal pricing maths.
/// </summary>
public sealed record FranchiseShareResult
{
    /// <summary>Regular selling price (the catalogue "Regular Price").</summary>
    public decimal ProductPrice { get; init; }

    /// <summary>The special price when one is configured (regardless of whether it's currently active); null otherwise.</summary>
    public decimal? SpecialPrice { get; init; }

    /// <summary>The price that actually applies right now — special price when active, else regular price.</summary>
    public decimal EffectivePrice { get; init; }

    /// <summary>True when a special price is set and the current UTC time is inside its window.</summary>
    public bool IsSpecialPriceActive { get; init; }

    /// <summary>The resolved share type (Percent / Fixed). Meaningless when <see cref="Source"/> is None.</summary>
    public CommissionType ShareType { get; init; }

    /// <summary>The resolved share value: a percentage (e.g. 15 = 15%) or a fixed ₹ per unit.</summary>
    public decimal ShareValue { get; init; }

    /// <summary>
    /// The TOTAL franchise earning per unit — commission plus any GST on it. This is what the
    /// franchisee actually keeps, and what gets deducted from what they pay, so it stays the
    /// single figure every existing consumer reads.
    /// </summary>
    public decimal CalculatedFranchiseAmount { get; init; }

    // ── Commission decomposition (Franchisee Share Calculation Logic, Steps 1–3) ──────────────
    // CommissionAmount + GstOnCommission == CalculatedFranchiseAmount, always.

    /// <summary>The product's GST rate used to reverse out the base and tax the commission.</summary>
    public decimal GstRate { get; init; }

    /// <summary>True when the share was computed for a GST-registered franchisee (Scenario A).</summary>
    public bool FranchiseeIsGstRegistered { get; init; }

    /// <summary>Step 1 — the pre-GST (taxable) value inside <see cref="EffectivePrice"/>.
    /// ₹100 for a ₹118 product at 18%. Informational for fixed ₹ shares.</summary>
    public decimal TaxableBase { get; init; }

    /// <summary>Step 2 — the bare commission per unit, before any tax on it (spec: ₹20).
    /// This is the taxable value of the franchisee's own supply to the institute.</summary>
    public decimal CommissionAmount { get; init; }

    /// <summary>Step 3 — GST charged on the commission (spec: ₹3.60). Zero when the franchisee
    /// holds no GSTIN, in which case they issue a bill of supply rather than a tax invoice.</summary>
    public decimal GstOnCommission { get; init; }

    /// <summary>Whether the share came from the product default, a franchise override, or nothing.</summary>
    public ShareSource Source { get; init; }

    /// <summary>Convenience flag — true when any share applies.</summary>
    public bool HasShare => Source != ShareSource.None;

    public static FranchiseShareResult NoShare(
        decimal regular, decimal? special, decimal effective, bool specialActive, decimal gstRate = 0m) => new()
    {
        ProductPrice = regular,
        SpecialPrice = special,
        EffectivePrice = effective,
        IsSpecialPriceActive = specialActive,
        ShareType = CommissionType.Percent,
        ShareValue = 0m,
        CalculatedFranchiseAmount = 0m,
        GstRate = gstRate,
        TaxableBase = 0m,
        CommissionAmount = 0m,
        GstOnCommission = 0m,
        Source = ShareSource.None
    };
}

/// <summary>
/// One row of the franchise portal "My Product Shares" listing. Wraps a <see cref="FranchiseShareResult"/>
/// with the product identity the franchisee needs (name, level, faculty). Contains nothing the
/// franchisee shouldn't see.
/// </summary>
public sealed record FranchiseProductShareRow
{
    public Guid ProductId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public CourseLevel Level { get; init; }
    public string? FacultyName { get; init; }

    public decimal ProductPrice { get; init; }
    public decimal? SpecialPrice { get; init; }
    public decimal EffectivePrice { get; init; }
    public bool IsSpecialPriceActive { get; init; }
    public CommissionType ShareType { get; init; }
    public decimal ShareValue { get; init; }
    /// <summary>Total per-unit earning — commission + GST on commission.</summary>
    public decimal CalculatedFranchiseAmount { get; init; }
    public ShareSource ShareSource { get; init; }

    // Commission decomposition, so a franchisee sees exactly what their invoice to us will say.
    public decimal GstRate { get; init; }
    public bool FranchiseeIsGstRegistered { get; init; }
    public decimal TaxableBase { get; init; }
    public decimal CommissionAmount { get; init; }
    public decimal GstOnCommission { get; init; }
}
