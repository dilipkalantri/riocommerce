using RioCommerce.Core.Enums;

namespace RioCommerce.Core.DTOs.Faculty;

/// <summary>
/// Where a resolved faculty share came from. Drives the "Source" column in the product editor's
/// faculty share grid and the admin sharing list. Mirrors <c>Franchise.ShareSource</c>.
/// </summary>
public enum FacultyShareSource
{
    /// <summary>No share applies (no active in-window rule and no enabled product default).</summary>
    None = 0,
    /// <summary>The product's own Default Faculty Share was used as the fallback rate.</summary>
    ProductDefault = 1,
    /// <summary>An explicit per-faculty <c>FacultySharingRule</c> was used.</summary>
    FacultyRule = 2
}

/// <summary>
/// One faculty member's resolved share on one unit of one product, at a point in time.
/// <see cref="ShareAmount"/> + <see cref="GstOnShare"/> == <see cref="TotalPayout"/>, always.
/// </summary>
public sealed record FacultyShareLine
{
    public Guid FacultyId { get; init; }
    public string FacultyName { get; init; } = string.Empty;
    public string FacultyShortCode { get; init; } = string.Empty;

    /// <summary>The rule row this came from, when the source is <see cref="FacultyShareSource.FacultyRule"/>.</summary>
    public Guid? RuleId { get; init; }

    public SharingType ShareType { get; init; }

    /// <summary>The configured share: a percentage (e.g. 15 = 15%) or a fixed ₹ per unit.</summary>
    public decimal ShareValue { get; init; }

    /// <summary>True when this faculty's share was computed as GST-attracting (Scenario A).</summary>
    public bool FacultyIsGstRegistered { get; init; }

    /// <summary>Step 2 — the bare share per unit, before any tax on it. Taxable value of the
    /// faculty's own supply to the institute, and the figure earnings totals sum.</summary>
    public decimal ShareAmount { get; init; }

    /// <summary>Step 3 — GST charged on that share. Zero when the faculty is not registered, in
    /// which case they raise a bill of supply rather than a tax invoice.</summary>
    public decimal GstOnShare { get; init; }

    /// <summary>What this faculty is actually paid per unit — share + GST on it.</summary>
    public decimal TotalPayout { get; init; }

    public FacultyShareSource Source { get; init; }

    /// <summary>True when the product's combined faculty share exceeded the taxable base and this
    /// line was scaled down proportionally. Surfaced in the UI so the reduced figure is
    /// self-explanatory.</summary>
    public bool WasCapped { get; init; }

    /// <summary>This line's share as a percentage of the taxable base — the comparable figure when
    /// the product mixes percentage and fixed ₹ rules.</summary>
    public decimal SharePctOfBase { get; init; }
}

/// <summary>
/// The complete faculty-share picture for ONE product at a point in time: every faculty who earns
/// from it, what each earns, and whether the combined total is within bounds.
///
/// <para>This is the shape the product editor's grid, the admin sharing list and the order-time
/// ledger writer all consume, so a share previewed in the editor is arithmetically the same share
/// that gets earned.</para>
/// </summary>
public sealed record ProductFacultyShareResult
{
    public Guid ProductId { get; init; }
    public string ProductTitle { get; init; } = string.Empty;
    public CourseLevel Level { get; init; }

    /// <summary>Regular selling price (the catalogue "Regular Price").</summary>
    public decimal ProductPrice { get; init; }

    /// <summary>The special price when one is configured, regardless of whether it's active now.</summary>
    public decimal? SpecialPrice { get; init; }

    /// <summary>The price that actually applies right now — special when active, else regular.</summary>
    public decimal EffectivePrice { get; init; }

    public bool IsSpecialPriceActive { get; init; }

    /// <summary>The product's GST rate, used to reverse out the base and tax each share.</summary>
    public decimal GstRate { get; init; }

    /// <summary>Step 1 — the pre-GST (taxable) value inside the price the share applies to.
    /// ₹100 for a ₹118 product at 18%. This is the ceiling the combined share is capped at.</summary>
    public decimal TaxableBase { get; init; }

    /// <summary>One line per earning faculty, ordered by descending payout.</summary>
    public List<FacultyShareLine> Lines { get; init; } = new();

    /// <summary>Sum of the bare shares — the institute's actual cost per unit.</summary>
    public decimal TotalShareAmount { get; init; }

    /// <summary>Sum of the GST on those shares — recoverable as input tax credit, subject to each
    /// registered faculty actually filing their invoice.</summary>
    public decimal TotalGstOnShare { get; init; }

    /// <summary>Total cash paid out to all faculty per unit.</summary>
    public decimal TotalPayout { get; init; }

    /// <summary>Combined share as a percentage of the taxable base. This is the number the admin UI
    /// shows as "allocated", and what <see cref="MaxTotalSharePct"/> is checked against.</summary>
    public decimal TotalSharePctOfBase { get; init; }

    /// <summary>The configured ceiling from <c>FacultySettings.MaxTotalSharePct</c>.</summary>
    public decimal MaxTotalSharePct { get; init; }

    /// <summary>True when <see cref="TotalSharePctOfBase"/> exceeds <see cref="MaxTotalSharePct"/>.
    /// A business-policy breach — reported so the UI can block or warn per
    /// <c>FacultySettings.EnforceMaxTotalShare</c>.</summary>
    public bool ExceedsConfiguredCap { get; init; }

    /// <summary>True when the combined share exceeded the taxable base and every line was scaled
    /// down. This is the hard money guard having fired, which is always a misconfiguration.</summary>
    public bool WasCappedToBase { get; init; }

    /// <summary>Headroom left before the configured ceiling, floored at zero. What the product
    /// editor shows as "% remaining".</summary>
    public decimal RemainingSharePct =>
        Math.Max(0m, MaxTotalSharePct - TotalSharePctOfBase);

    public bool HasShare => Lines.Count > 0;

    public static ProductFacultyShareResult NoShare(
        Guid productId, string title, CourseLevel level, decimal regular, decimal? special,
        decimal effective, bool specialActive, decimal gstRate, decimal taxableBase, decimal maxPct) => new()
        {
            ProductId = productId,
            ProductTitle = title,
            Level = level,
            ProductPrice = regular,
            SpecialPrice = special,
            EffectivePrice = effective,
            IsSpecialPriceActive = specialActive,
            GstRate = gstRate,
            TaxableBase = taxableBase,
            MaxTotalSharePct = maxPct
        };
}

/// <summary>
/// Editable row for the product editor's faculty share grid — one per faculty attached to the
/// product. Carries both the configuration being edited and the calculated preview, so the admin
/// sees the money implication of a rate as they type it.
/// </summary>
public sealed class ProductFacultyShareEditRow
{
    /// <summary>The existing rule's id; null for a faculty who has no explicit rule yet.</summary>
    public Guid? RuleId { get; set; }
    public Guid FacultyId { get; set; }
    public string FacultyName { get; set; } = string.Empty;
    public string FacultyShortCode { get; set; } = string.Empty;

    /// <summary>True when this faculty's share attracts GST — display only; the calculator reads it
    /// from the Faculty record so it can't be spoofed from the form.</summary>
    public bool FacultyIsGstRegistered { get; set; }

    /// <summary>False when the row is only present because the product default applies. Ticking it
    /// materialises an explicit rule at the current effective rate.</summary>
    public bool HasExplicitRule { get; set; }

    public SharingType ShareType { get; set; } = SharingType.Percentage;
    public decimal ShareValue { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }

    // ── Calculated preview (server-computed; ignored on save) ──
    public decimal ShareAmount { get; set; }
    public decimal GstOnShare { get; set; }
    public decimal TotalPayout { get; set; }
    public decimal SharePctOfBase { get; set; }
    public FacultyShareSource Source { get; set; }

    /// <summary>True when the product's combined faculty share exceeded the taxable base and this
    /// row was scaled down. Shown in the grid so a payout smaller than the configured rate is
    /// self-explanatory rather than looking like a bug.</summary>
    public bool WasCapped { get; set; }
}

/// <summary>
/// What the product editor loads and saves for the Faculty Share section: the product-level default,
/// the per-faculty rows, and the calculated totals used for the allocation meter.
/// </summary>
public sealed class ProductFacultyShareEditModel
{
    public bool EnableDefaultFacultyShare { get; set; }
    public SharingType DefaultFacultyShareType { get; set; } = SharingType.Percentage;
    public decimal DefaultFacultyShareValue { get; set; }

    public List<ProductFacultyShareEditRow> Rows { get; set; } = new();

    // ── Read-only context for the meter / hints ──
    public decimal TaxableBase { get; set; }
    public decimal EffectivePrice { get; set; }
    public decimal GstRate { get; set; }
    public decimal TotalSharePctOfBase { get; set; }
    public decimal TotalPayout { get; set; }
    public decimal MaxTotalSharePct { get; set; } = 100m;
    public bool EnforceMaxTotalShare { get; set; } = true;
    public bool ExceedsConfiguredCap { get; set; }
}

/// <summary>One row of the admin "Faculty Revenue Sharing" list, grouped per product.</summary>
public sealed record ProductFacultyShareSummary
{
    public Guid ProductId { get; init; }
    public string ProductTitle { get; init; } = string.Empty;
    public string? Sku { get; init; }
    public CourseLevel Level { get; init; }
    public decimal EffectivePrice { get; init; }
    public decimal TaxableBase { get; init; }
    public decimal GstRate { get; init; }
    public int FacultyCount { get; init; }
    public decimal TotalShareAmount { get; init; }
    public decimal TotalGstOnShare { get; init; }
    public decimal TotalPayout { get; init; }
    public decimal TotalSharePctOfBase { get; init; }
    public bool ExceedsConfiguredCap { get; init; }
    public bool WasCappedToBase { get; init; }
    public List<FacultyShareLine> Lines { get; init; } = new();
}

/// <summary>
/// A faculty member's earnings statement line, read from the earned-share ledger.
/// </summary>
public sealed record FacultyEarningRow
{
    public DateTime EarnedAt { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public string ProductTitle { get; init; } = string.Empty;
    public decimal BaseAmount { get; init; }
    public SharingType ShareType { get; init; }
    public decimal ShareValue { get; init; }
    public decimal ShareAmount { get; init; }
    public decimal GstOnShare { get; init; }
    public decimal TotalPayout { get; init; }
    public bool WasGstRegistered { get; init; }
    public bool WasCapped { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Bulk assign — the faculty counterpart of Franchise → Bulk Assign
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies one share rate to many (product × faculty) pairs at once.
///
/// <para><b>Why this is riskier than the franchise equivalent.</b> A product has exactly one
/// franchisee, so bulk-assigning 30% to 50 franchisees sets 50 independent 30%s. A product can have
/// many faculty, so bulk-assigning 30% to 3 faculty across 10 products pushes every one of those
/// products to 90% of its taxable base. The combined allocation is therefore checked per product
/// before anything is written — see <see cref="SkipProductsOverCap"/>.</para>
///
/// <para><b>Which pairs are eligible</b> is a fixed rule, not an option: a rate is CREATED only where
/// the faculty is on the product's faculty list, and UPDATED wherever a rule already exists (attached
/// or not). Other pairs are skipped and counted. There is no toggle, because the alternative —
/// creating rates for unattached faculty — means "all faculty × all products" would hand every faculty
/// a share of every course. An already-detached rule still updates so a bulk rate change doesn't
/// silently pass over a legacy agreement.</para>
/// </summary>
public class BulkAssignFacultySharesRequest
{
    /// <summary>Target faculty. Ignored when <see cref="AllFaculty"/> is true.</summary>
    public List<Guid> FacultyIds { get; set; } = new();

    /// <summary>When true, targets every active faculty (<see cref="FacultyIds"/> is ignored).</summary>
    public bool AllFaculty { get; set; }

    /// <summary>Products to set rates on. At least one required.</summary>
    public List<Guid> ProductIds { get; set; } = new();

    public SharingType Type { get; set; } = SharingType.Percentage;
    public decimal Value { get; set; }

    /// <summary>Optional effective window for every rule created or updated.</summary>
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    /// <summary>
    /// When true, products whose combined faculty share would exceed
    /// <c>FacultySettings.MaxTotalSharePct</c> are skipped and reported, and the rest are applied.
    /// When false the whole operation is rejected if any product would breach — safer default for a
    /// bulk write, because a partial apply is hard to reason about after the fact.
    /// </summary>
    public bool SkipProductsOverCap { get; set; }
}

/// <summary>
/// What a bulk assign would do to one product — the dry-run row. Lets an admin see that a 30% rate
/// across three faculty takes a course from 20% to 90% <em>before</em> committing it.
/// </summary>
public sealed record FacultyBulkAssignPreviewRow
{
    public Guid ProductId { get; init; }
    public string ProductTitle { get; init; } = string.Empty;
    public string? Sku { get; init; }

    /// <summary>Pairs that will actually be written for this product.</summary>
    public int RulesCreated { get; init; }
    public int RulesUpdated { get; init; }

    /// <summary>Selected faculty skipped because they are not attached to this product.</summary>
    public int SkippedNotAttached { get; init; }

    /// <summary>Combined faculty share as a percentage of the taxable base, before and after.</summary>
    public decimal CurrentSharePct { get; init; }
    public decimal ResultingSharePct { get; init; }
    public decimal MaxTotalSharePct { get; init; }

    /// <summary>True when <see cref="ResultingSharePct"/> breaches the configured ceiling.</summary>
    public bool ExceedsCap { get; init; }

    /// <summary>Resulting total payout per unit across all this product's faculty.</summary>
    public decimal ResultingPayoutPerUnit { get; init; }

    /// <summary>Set when this product will not be written — the reason why.</summary>
    public string? SkipReason { get; init; }

    public bool WillApply => SkipReason == null && (RulesCreated + RulesUpdated) > 0;
}

/// <summary>The outcome of a bulk assign (or its dry run).</summary>
public sealed record FacultyBulkAssignResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public bool DryRun { get; init; }

    /// <summary>Pairs written (0 on a dry run or a rejected apply).</summary>
    public int PairsApplied { get; init; }
    public int ProductsApplied { get; init; }
    public int ProductsSkipped { get; init; }

    public List<FacultyBulkAssignPreviewRow> Rows { get; init; } = new();
}

/// <summary>
/// One existing (product, faculty) rate for the assignments management grid. Carries the calculated
/// money and the product's combined allocation, so over-allocation is visible from the list rather
/// than only discoverable by opening each product.
/// </summary>
public class FacultyAssignmentRow
{
    public Guid RuleId { get; set; }
    public Guid ProductId { get; set; }
    public Guid FacultyId { get; set; }
    public string ProductTitle { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public CourseLevel Level { get; set; }
    public string FacultyName { get; set; } = string.Empty;
    public string FacultyShortCode { get; set; } = string.Empty;

    public SharingType Type { get; set; } = SharingType.Percentage;

    /// <summary>The assigned rate on this product↔faculty rule.</summary>
    public decimal AssignedValue { get; set; }

    // The product's own default, for the Default/Custom badge.
    public bool ProductDefaultEnabled { get; set; }
    public SharingType ProductDefaultType { get; set; } = SharingType.Percentage;
    public decimal ProductDefaultValue { get; set; }

    /// <summary>"Custom" when the assigned rate differs from the product default, else "Default".</summary>
    public bool IsCustom { get; set; }

    public bool IsActive { get; set; }

    /// <summary>False when the rule exists but its effective window has not opened or has closed.</summary>
    public bool IsInEffect { get; set; }

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime AssignedAt { get; set; }

    /// <summary>True when the faculty is attached to the product via ProductFaculty. A rule without an
    /// attachment still pays out — it is a live agreement — so this is surfaced, not hidden.</summary>
    public bool IsAttachedToProduct { get; set; }

    // ── Calculated ──
    public bool FacultyIsGstRegistered { get; set; }
    public decimal ShareAmount { get; set; }
    public decimal GstOnShare { get; set; }
    public decimal TotalPayout { get; set; }
    public decimal SharePctOfBase { get; set; }
    public decimal ProductTotalSharePct { get; set; }
    public bool ProductExceedsCap { get; set; }
    public bool WasCapped { get; set; }
}

public class UpdateFacultyAssignmentRequest
{
    public Guid ProductId { get; set; }
    public Guid FacultyId { get; set; }
    public SharingType Type { get; set; } = SharingType.Percentage;
    public decimal Value { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

/// <summary>
/// One faculty share recomputed from current rules for an order that has no ledger entry (i.e. one
/// placed before the earned-share ledger existed). Carries the same identifiers a ledger row would,
/// so payout dedupe and line attribution work identically for both sources.
/// </summary>
public sealed record FacultyShareRecomputedLine(
    Guid FacultyId, string Name, Guid OrderId, string OrderNumber, Guid OrderItemId,
    decimal Share, decimal Gst);

/// <summary>Aggregate earnings for one faculty over a period, split the way a fee invoice states it.</summary>
public sealed record FacultyEarningsSummary(
    Guid FacultyId,
    string FacultyName,
    int Orders,
    decimal ShareAmount,
    decimal GstOnShare,
    decimal TotalPayout);
