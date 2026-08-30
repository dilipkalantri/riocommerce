using RioCommerce.Core.DTOs.Franchise;
using RioCommerce.Core.Entities;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Single source of truth for franchise pricing + share calculation.
///
/// <para>This consolidates rules that were previously expressed as ad-hoc static helpers on
/// <c>FranchiseService</c> so that order commission, settlements, invoices, the admin bulk-assign
/// grid AND the franchise portal listing all compute identical numbers. Everything is pure and
/// date-driven, so a special price that turns on/off simply changes the result on the next call —
/// no stored value to refresh.</para>
///
/// Resolution priority (matches the business rule):
/// <list type="number">
///   <item>An active per-franchise override (a <see cref="FranchiseCommission"/> row from Bulk Assign),
///         when <c>FranchiseSettings.AllowFranchiseSpecificOverride</c> is on → <see cref="ShareSource.FranchiseOverride"/>.</item>
///   <item>The product's enabled Default Franchise Share → <see cref="ShareSource.ProductDefault"/>.</item>
///   <item>Otherwise no share → <see cref="ShareSource.None"/>.</item>
/// </list>
/// </summary>
public interface IFranchiseShareCalculator
{
    /// <summary>
    /// The price that applies right now: the special price when it's configured AND the current UTC
    /// time falls inside its window, otherwise the regular selling price.
    /// </summary>
    decimal EffectivePrice(Product product, DateTime? asOfUtc = null);

    /// <summary>True when the product has a special price that is active at <paramref name="asOfUtc"/>.</summary>
    bool IsSpecialPriceActive(Product product, DateTime? asOfUtc = null);

    /// <summary>
    /// Computes the full pricing + share breakdown for one (product, franchise-rule) pair.
    /// Pass the franchise's override row (or null) and the global settings. Pure — no DB access.
    /// </summary>
    /// <param name="franchiseeIsGstRegistered">
    /// Whether the franchisee holds a GSTIN (<c>!string.IsNullOrWhiteSpace(franchise.Gstin)</c> —
    /// the same test used for B2B/B2C classification everywhere else). This is REQUIRED rather
    /// than defaulted: it changes how much the franchisee is paid, so every call site must state
    /// it deliberately. A registered franchisee's commission attracts GST (Scenario A); an
    /// unregistered one's does not (Scenario B). See <c>FranchiseCommissionMath</c>.
    /// </param>
    FranchiseShareResult Calculate(
        Product product,
        FranchiseCommission? overrideRule,
        FranchiseSettings settings,
        bool franchiseeIsGstRegistered,
        DateTime? asOfUtc = null);

    /// <summary>
    /// Loads the products + the franchise's override rows + settings and returns one
    /// <see cref="FranchiseProductShareRow"/> per active product. This is what the portal listing
    /// (and its API) renders. Excludes products with no applicable share when
    /// <paramref name="onlyWithShare"/> is true.
    /// </summary>
    Task<List<FranchiseProductShareRow>> GetProductSharesAsync(
        Guid franchiseId, bool onlyWithShare = false, CancellationToken ct = default);
}
