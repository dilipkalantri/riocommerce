using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.Entities;

namespace RioCommerce.Core.Interfaces;

/// <summary>Identity + GST registration for one faculty, as the calculator needs it.</summary>
/// <param name="IsGstRegistered">
/// Read from <see cref="Faculty.IsGstRegisteredForShare"/>. Passed in rather than looked up so the
/// calculation stays pure and a caller replaying a historical order can supply the status that
/// applied AT THAT TIME instead of today's.
/// </param>
public readonly record struct FacultyGstInfo(
    Guid FacultyId, string DisplayName, string ShortCode, bool IsGstRegistered);

/// <summary>
/// Single source of truth for product faculty share calculation — the faculty counterpart of
/// <see cref="IFranchiseShareCalculator"/>.
///
/// <para>Consolidates share arithmetic that was previously reimplemented in three places
/// (<c>FacultySharingService.ListRulesAsync</c>, <c>FacultySharingService.PayoutAsync</c> and
/// <c>PayoutCalculationService.FacultyPreviewAsync</c>), each rounding independently. Everything is
/// pure and date-driven, so a special price or a dated rate change simply changes the result on the
/// next call — there is no stored value to refresh.</para>
///
/// <para><b>The structural difference from the franchise calculator:</b> a product has one
/// franchisee but MANY faculty, each potentially on a different share. So this returns a
/// <see cref="ProductFacultyShareResult"/> — a list of per-faculty lines plus the combined totals —
/// rather than one figure, and it owns the cap that keeps the combined share inside the sale's
/// taxable value.</para>
///
/// Resolution priority, applied per faculty:
/// <list type="number">
///   <item>An explicit <see cref="FacultySharingRule"/> that is active AND inside its effective
///         window, when <c>FacultySettings.AllowFacultyRuleOverride</c> is on
///         → <see cref="FacultyShareSource.FacultyRule"/>.</item>
///   <item>The product's enabled Default Faculty Share, for any faculty attached to the product
///         via <c>ProductFaculty</c> → <see cref="FacultyShareSource.ProductDefault"/>.</item>
///   <item>Otherwise that faculty earns nothing and gets no line.</item>
/// </list>
/// </summary>
public interface IFacultyShareCalculator
{
    /// <summary>
    /// The price that applies right now: the special price when configured AND the current UTC time
    /// is inside its window, otherwise the regular selling price.
    /// </summary>
    decimal EffectivePrice(Product product, DateTime? asOfUtc = null);

    /// <summary>True when the product has a special price active at <paramref name="asOfUtc"/>.</summary>
    bool IsSpecialPriceActive(Product product, DateTime? asOfUtc = null);

    /// <summary>
    /// Computes the full faculty share breakdown for one product. Pure — no DB access.
    /// </summary>
    /// <param name="rules">
    /// Every <see cref="FacultySharingRule"/> for this product, active or not. Window and active
    /// filtering happens inside, so callers can pass what they have without pre-filtering
    /// differently from each other.
    /// </param>
    /// <param name="faculty">
    /// Every faculty relevant to this product — those attached to it AND anyone holding a rule on it —
    /// keyed by id. Supplies display names and, critically, each one's GST registration, which decides
    /// how much they are paid. Any faculty referenced by a rule but absent here is skipped rather than
    /// guessed at.
    /// </param>
    /// <param name="attachedFacultyIds">
    /// Only those actually attached via <c>ProductFaculty</c>. REQUIRED and deliberately separate from
    /// <paramref name="faculty"/>: the product default reaches attached faculty only, so conflating the
    /// two sets makes an unattached rule-holder inherit the default the moment their own rule stops
    /// applying — paying someone for a course they were removed from. An explicit in-force rule still
    /// earns without an attachment; a lapsed one must not.
    /// </param>
    ProductFacultyShareResult Calculate(
        Product product,
        IReadOnlyList<FacultySharingRule> rules,
        IReadOnlyDictionary<Guid, FacultyGstInfo> faculty,
        IReadOnlySet<Guid> attachedFacultyIds,
        FacultySettings settings,
        DateTime? asOfUtc = null);

    /// <summary>Loads a single product with its rules, faculty and settings, then calculates.</summary>
    Task<ProductFacultyShareResult?> GetForProductAsync(
        Guid productId, DateTime? asOfUtc = null, CancellationToken ct = default);

    /// <summary>
    /// One summary per active product for the admin listing. Excludes products where no faculty
    /// earns anything when <paramref name="onlyWithShare"/> is true.
    /// </summary>
    Task<List<ProductFacultyShareSummary>> GetProductSharesAsync(
        bool onlyWithShare = false, CancellationToken ct = default);

    /// <summary>Loads the singleton faculty settings, creating defaults if missing.</summary>
    Task<FacultySettings> GetSettingsAsync(CancellationToken ct = default);
}
