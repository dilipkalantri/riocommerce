namespace RioCommerce.Core.Interfaces;

/// <summary>A settlement as the management list shows it.</summary>
public sealed record TeacherSettlementRow(
    Guid Id, string SettlementNumber, Guid FacultyId, string FacultyName,
    DateTime PeriodStart, DateTime PeriodEnd,
    decimal TotalSales, decimal ShareAmount, decimal GstOnShare, decimal TdsDeduction,
    decimal TotalPayable, decimal AmountPaid, decimal BalancePayable,
    string Status, DateTime? SettledOn, int LineCount);

/// <summary>What a generation run produced, so the admin gets a result rather than a silent redirect.</summary>
public sealed record SettlementGenerationResult(
    int Created, int Updated, int SkippedNoEarnings, int SkippedLocked, List<string> Messages);

/// <summary>
/// Generates and settles faculty settlement periods (§15).
///
/// <para>Generation rolls the <c>FacultyShareEntries</c> earnings ledger up into one settlement per
/// faculty per period, snapshotting the payable. Re-running a period updates a settlement that has
/// not been paid against and leaves alone one that has — a period already part-paid must not be
/// silently restated by a later rule change or refund.</para>
/// </summary>
public interface ITeacherSettlementService
{
    Task<List<TeacherSettlementRow>> ListAsync(
        DateTime? from, DateTime? to, Guid? facultyId, CancellationToken ct = default);

    /// <summary>
    /// Builds or refreshes settlements for every faculty who earned in the window.
    /// </summary>
    /// <param name="facultyIds">Empty = all faculty with earnings in the period.</param>
    Task<SettlementGenerationResult> GenerateAsync(
        DateTime periodStart, DateTime periodEnd, IReadOnlyList<Guid> facultyIds,
        Guid? actorId, string? actorName, CancellationToken ct = default);

    /// <summary>Records a payment against a settlement and moves its status accordingly.</summary>
    Task<(bool ok, string? error)> RecordPaymentAsync(
        Guid settlementId, decimal amount, Enums.PaymentMode? mode, string? reference, string? notes,
        Guid? actorId, string? actorName, CancellationToken ct = default);

    Task<(bool ok, string? error)> CancelAsync(Guid settlementId, string reason, CancellationToken ct = default);
}
