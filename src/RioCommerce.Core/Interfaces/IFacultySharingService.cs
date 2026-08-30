using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.Entities;

namespace RioCommerce.Core.Interfaces;

public interface IFacultySharingService
{
    Task<List<SharingRuleRow>> ListRulesAsync();
    Task<SharingOptions> OptionsAsync();

    /// <summary>
    /// Upserts one (product, faculty) rule. Rejects the save when it would push the product's
    /// combined faculty share past <c>FacultySettings.MaxTotalSharePct</c> and enforcement is on —
    /// validation lives here rather than only in the UI so the API and the Excel importer cannot
    /// bypass it.
    /// </summary>
    Task<(bool ok, string? error)> SaveRuleAsync(SharingRuleEdit rule);

    Task DeleteRuleAsync(Guid id);

    /// <summary>Faculty payouts for revenue orders created in [from, to).</summary>
    Task<List<FacultyPayout>> PayoutAsync(DateTime fromUtc, DateTime toUtc);

    // ── Product faculty share (per-product, multi-faculty) ──────────────────────────────────

    /// <summary>
    /// Everything the product editor's Faculty Share section needs: the product default, one row per
    /// attached faculty (explicit rule or falling back to the default), and the calculated totals for
    /// the allocation meter.
    /// </summary>
    Task<ProductFacultyShareEditModel?> GetProductShareEditModelAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Saves the product default plus the whole set of per-faculty rules in one go. Rows without an
    /// explicit rule are not persisted (they keep inheriting the default); rows whose explicit rule
    /// was un-ticked are deleted. Returns the cap violation as an error when enforcement is on.
    /// </summary>
    Task<(bool ok, string? error)> SaveProductSharesAsync(
        Guid productId, ProductFacultyShareEditModel model, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Recalculates the model's amounts and totals from the SUBMITTED (unsaved) rows without
    /// persisting anything. Lets the product editor show the real money implication of an edit —
    /// including the combined allocation — before the admin commits it.
    /// </summary>
    Task<ProductFacultyShareEditModel?> PreviewProductSharesAsync(
        Guid productId, ProductFacultyShareEditModel model, CancellationToken ct = default);

    /// <summary>One summary per active product for the admin listing, grouped per product.</summary>
    Task<List<ProductFacultyShareSummary>> ListProductSharesAsync(bool onlyWithShare = false, CancellationToken ct = default);

    // ── Bulk assign (the faculty counterpart of Franchise → Bulk Assign) ────────────────────

    /// <summary>
    /// Applies one rate to many (product × faculty) pairs.
    ///
    /// <para>Always computes the per-product impact first. Unlike franchise bulk-assign — where each
    /// product has one franchisee, so a rate can never combine — a faculty rate applied to several
    /// faculty on the same product ADDS UP. So every affected product is checked against
    /// <c>FacultySettings.MaxTotalSharePct</c> before anything is written, and the breaches are
    /// reported per product rather than as one opaque failure.</para>
    /// </summary>
    /// <param name="dryRun">
    /// When true nothing is written and the result carries only the projected per-product rows. This
    /// is what the admin page's Preview button calls.
    /// </param>
    Task<FacultyBulkAssignResult> BulkAssignSharesAsync(
        BulkAssignFacultySharesRequest req, Guid actorUserId, bool dryRun = false, CancellationToken ct = default);

    /// <summary>Every existing (product, faculty) rate, with its calculated money and the product's
    /// combined allocation. Backs the assignments management grid.</summary>
    Task<List<FacultyAssignmentRow>> ListAssignmentsAsync(CancellationToken ct = default);

    /// <summary>Edits one assignment's rate in place. Cap-validated like every other write path.</summary>
    Task<(bool ok, string? error)> UpdateAssignmentAsync(
        UpdateFacultyAssignmentRequest req, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Enables/disables one assignment without deleting it, so the agreement stays on record.</summary>
    Task<(bool ok, string? error)> ToggleAssignmentAsync(
        Guid productId, Guid facultyId, bool active, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Deletes one assignment. The faculty then falls back to the product default, if any.</summary>
    Task<(bool ok, string? error)> RemoveAssignmentAsync(
        Guid productId, Guid facultyId, Guid actorUserId, CancellationToken ct = default);

    // ── Earned-share ledger ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes one <c>FacultyShareEntry</c> per (order item × earning faculty), snapshotting the rule
    /// and each faculty's GST registration at this moment. Idempotent — a second call for the same
    /// order is a no-op. Called from order confirmation alongside the franchise commission write.
    /// </summary>
    Task RecordOrderFacultyShareAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>A faculty's earnings statement lines from the ledger, newest first.</summary>
    Task<List<FacultyEarningRow>> GetEarningsAsync(
        Guid facultyId, DateTime? fromUtc, DateTime? toUtc, int take = 200, CancellationToken ct = default);

    /// <summary>
    /// Recomputes faculty shares from CURRENT rules for revenue orders in [from, to) whose id is not
    /// in <paramref name="skipOrderIds"/> — i.e. orders placed before the earned-share ledger existed.
    ///
    /// <para>Exposed so <c>PayoutCalculationService</c> and <see cref="PayoutAsync"/> share one
    /// implementation of the fallback. Two copies would eventually disagree about a historical month,
    /// and the disagreement would surface as a payout dispute rather than a test failure.</para>
    /// </summary>
    Task<List<FacultyShareRecomputedLine>> ComputeFromRulesForPayoutAsync(
        DateTime fromUtc, DateTime toUtc, HashSet<Guid> skipOrderIds, CancellationToken ct = default);

    // ── Settings ───────────────────────────────────────────────────────────────────────────

    Task<FacultySettings> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(FacultySettings settings, Guid actorUserId, CancellationToken ct = default);
}
