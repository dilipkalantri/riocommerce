using RioCommerce.Core.DTOs.Installments;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Installment plans for counter orders: validate &amp; create the schedule at order time, expose
/// the schedule for the order-detail page, collect the next installment at the counter, and drive
/// the four installment reports. Final invoice generation is intentionally deferred to the
/// existing <see cref="IInvoiceService.EnsureForOrderAsync"/> (gated on PaymentStatus.Success),
/// which this service triggers only after the last installment is collected.
/// </summary>
public interface IInstallmentService
{
    /// <summary>
    /// Validates the installment block against settings + the order total and creates the plan
    /// (down payment + scheduled installments). Called from the counter-order creation path AFTER
    /// the order header + totals exist. The down payment is recorded as a paid Payment row by the
    /// caller; this method records the schedule and the down-payment slice on the plan.
    /// Returns (ok, error). Throws nothing — validation failures come back as error text.
    /// </summary>
    Task<(bool ok, string? error)> CreatePlanAsync(Guid orderId, InstallmentPlanInput input, Guid? actorId, string? actorName, CancellationToken ct = default);

    /// <summary>The plan view for an order, or null if the order has no installment plan.</summary>
    Task<InstallmentPlanView?> GetPlanAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Collects (all or part of) an installment at the counter. Updates installment +
    /// plan state, records Payment + PaymentTransaction rows, and — when this clears the final
    /// balance — flips the order to paid and generates the invoice. Idempotent against an
    /// already-paid installment (returns ok with no double charge).</summary>
    Task<CollectInstallmentResult> CollectAsync(CollectInstallmentRequest req, Guid? actorId, string? actorName, CancellationToken ct = default);

    /// <summary>Server-side validation helper for the wizard (down-payment minimum, sum match).
    /// Returns null when valid, else an error message.</summary>
    Task<string?> ValidatePlanAsync(decimal orderTotal, InstallmentPlanInput input, CancellationToken ct = default);

    // ── Settings ──
    Task<InstallmentSettingsModel> GetSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(InstallmentSettingsModel model, CancellationToken ct = default);

    // ── Reports ──
    Task<List<InstallmentReportRow>> ReportAsync(InstallmentReportFilter filter, CancellationToken ct = default);

    // ── Reminders (used by the scheduled task) ──
    /// <summary>Sends due-date reminders for installments coming up within the configured window
    /// that haven't been reminded yet. Returns the number of reminders sent.</summary>
    Task<int> SendDueRemindersAsync(CancellationToken ct = default);
}
