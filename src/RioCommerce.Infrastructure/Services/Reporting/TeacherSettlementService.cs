using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// Generates and settles faculty settlement periods (§15).
///
/// <para>Earnings come from the <c>FacultyShareEntries</c> ledger, which snapshots the sharing rule
/// and the faculty's GST registration at the moment of each order. Recomputing from today's rules
/// would restate settled history, which is exactly what the ledger exists to prevent (§26).</para>
/// </summary>
public class TeacherSettlementService : ITeacherSettlementService
{
    private readonly RioCommerceDbContext _db;
    public TeacherSettlementService(RioCommerceDbContext db) => _db = db;

    public async Task<List<TeacherSettlementRow>> ListAsync(
        DateTime? from, DateTime? to, Guid? facultyId, CancellationToken ct = default)
    {
        var q = _db.TeacherSettlements.AsNoTracking().AsQueryable();

        // Overlap, not containment — a quarterly settlement must still appear under a one-month
        // filter that falls inside it.
        if (from is { } f) q = q.Where(s => s.PeriodEndUtc > DateTime.SpecifyKind(f, DateTimeKind.Utc));
        if (to is { } t) q = q.Where(s => s.PeriodStartUtc < DateTime.SpecifyKind(t, DateTimeKind.Utc));
        if (facultyId is { } fid) q = q.Where(s => s.FacultyId == fid);

        return await q
            .OrderByDescending(s => s.PeriodStartUtc).ThenBy(s => s.FacultyName)
            .Select(s => new TeacherSettlementRow(
                s.Id, s.SettlementNumber, s.FacultyId, s.FacultyName,
                s.PeriodStartUtc, s.PeriodEndUtc,
                s.TotalSales, s.ShareAmount, s.GstOnShare, s.TdsDeduction,
                s.TotalPayable, s.AmountPaid, s.BalancePayable,
                s.Status.ToString(), s.SettledOn, s.Items.Count))
            .ToListAsync(ct);
    }

    public async Task<SettlementGenerationResult> GenerateAsync(
        DateTime periodStart, DateTime periodEnd, IReadOnlyList<Guid> facultyIds,
        Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        var start = DateTime.SpecifyKind(periodStart, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(periodEnd, DateTimeKind.Utc);
        var messages = new List<string>();

        if (end <= start)
            return new SettlementGenerationResult(0, 0, 0, 0,
                new List<string> { "The period end must be after the period start." });

        // Earnings for the window, straight from the ledger.
        var entries = _db.FacultyShareEntries.AsNoTracking()
            .Where(e => e.EarnedAt >= start && e.EarnedAt < end);
        if (facultyIds.Count > 0) entries = entries.Where(e => facultyIds.Contains(e.FacultyId));

        var raw = await entries.Select(e => new
        {
            e.FacultyId,
            FacultyName = e.Faculty.DisplayName,
            e.OrderId,
            e.OrderItemId,
            e.ProductId,
            e.ProductTitle,
            e.BaseAmount,
            e.ShareType,
            e.ShareValue,
            e.ShareAmount,
            e.GstOnShare,
            e.TotalPayout,
            SubjectId = _db.Products.Where(p => p.Id == e.ProductId).Select(p => p.SubjectId).FirstOrDefault(),
            SubjectName = _db.Products.Where(p => p.Id == e.ProductId)
                .Select(p => p.Subject != null ? p.Subject.Name : null).FirstOrDefault(),
            // Sales context: the order line the share was earned on.
            Quantity = _db.OrderItems.Where(i => i.Id == e.OrderItemId).Select(i => (int?)i.Quantity).FirstOrDefault() ?? 0,
            LineTotal = _db.OrderItems.Where(i => i.Id == e.OrderItemId).Select(i => (decimal?)i.LineTotal).FirstOrDefault() ?? 0m
        }).ToListAsync(ct);

        if (raw.Count == 0)
            return new SettlementGenerationResult(0, 0, 0, 0,
                new List<string> { "No faculty earnings were recorded in this period." });

        // TDS applies to the professional fee only, not to the GST charged on it, unless the
        // institute has configured otherwise. Read from settings rather than hard-coded so the
        // report agrees with how payouts are actually computed.
        var settings = await _db.FacultySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var tdsOnShareOnly = settings?.TdsOnShareExcludingGst ?? true;

        var existing = await _db.TeacherSettlements
            .Include(s => s.Items)
            .Where(s => s.PeriodStartUtc == start && s.PeriodEndUtc == end)
            .ToListAsync(ct);

        int created = 0, updated = 0, locked = 0;

        foreach (var group in raw.GroupBy(r => new { r.FacultyId, r.FacultyName }))
        {
            var prior = existing.FirstOrDefault(s => s.FacultyId == group.Key.FacultyId);

            // A period that has taken money, or been cancelled, is history. Refusing to restate it
            // is the whole point of snapshotting the payable.
            if (prior != null && (prior.AmountPaid > 0m || prior.Status == TeacherSettlementStatus.Cancelled))
            {
                locked++;
                messages.Add($"{group.Key.FacultyName}: left unchanged — {(prior.Status == TeacherSettlementStatus.Cancelled ? "cancelled" : $"₹{prior.AmountPaid:N2} already paid")}.");
                continue;
            }

            var lines = group
                .GroupBy(r => new { r.ProductId, r.ProductTitle, r.SubjectId, r.SubjectName })
                .Select(g => new TeacherSettlementItem
                {
                    ProductId = g.Key.ProductId,
                    ProductTitle = g.Key.ProductTitle,
                    SubjectId = g.Key.SubjectId,
                    SubjectName = g.Key.SubjectName,
                    Quantity = g.Sum(x => x.Quantity),
                    OrderCount = g.Select(x => x.OrderId).Distinct().Count(),
                    GrossSales = Math.Round(g.Sum(x => x.LineTotal), 2),
                    TaxableBase = Math.Round(g.Sum(x => x.BaseAmount), 2),
                    // Rule is per (product, faculty), so every entry in the group carries the same
                    // one; the most recent wins if a rate changed mid-period.
                    ShareType = g.Last().ShareType,
                    ShareValue = g.Last().ShareValue,
                    ShareAmount = Math.Round(g.Sum(x => x.ShareAmount), 2),
                    GstOnShare = Math.Round(g.Sum(x => x.GstOnShare), 2),
                    TotalPayout = Math.Round(g.Sum(x => x.TotalPayout), 2)
                })
                .ToList();

            var share = lines.Sum(l => l.ShareAmount);
            var gst = lines.Sum(l => l.GstOnShare);
            var sales = lines.Sum(l => l.GrossSales);
            var qty = lines.Sum(l => l.Quantity);

            // TDS is left at zero here: the rate is per-faculty (PAN status, 194J threshold) and is
            // applied at payout time, not at settlement generation. The column exists so a manual
            // deduction can be recorded before payment; the flag decides its basis when it is.
            var tdsBasis = tdsOnShareOnly ? share : share + gst;
            var tds = prior?.TdsDeduction ?? 0m;
            if (tds > tdsBasis) tds = tdsBasis;

            var adjustments = prior?.Adjustments ?? 0m;
            var payable = Math.Round(share + gst - tds + adjustments, 2);

            if (prior == null)
            {
                var s = new TeacherSettlement
                {
                    SettlementNumber = await NextNumberAsync(ct),
                    FacultyId = group.Key.FacultyId,
                    FacultyName = group.Key.FacultyName,
                    PeriodStartUtc = start,
                    PeriodEndUtc = end,
                    TotalSales = Math.Round(sales, 2),
                    TotalQuantity = qty,
                    ShareAmount = share,
                    GstOnShare = gst,
                    TdsDeduction = tds,
                    Adjustments = adjustments,
                    TotalPayable = payable,
                    AmountPaid = 0m,
                    BalancePayable = payable,
                    Status = TeacherSettlementStatus.Pending,
                    CreatedById = actorId,
                    CreatedByName = actorName ?? "system"
                };
                foreach (var l in lines) s.Items.Add(l);
                _db.TeacherSettlements.Add(s);
                created++;
            }
            else
            {
                prior.FacultyName = group.Key.FacultyName;
                prior.TotalSales = Math.Round(sales, 2);
                prior.TotalQuantity = qty;
                prior.ShareAmount = share;
                prior.GstOnShare = gst;
                prior.TdsDeduction = tds;
                prior.TotalPayable = payable;
                prior.BalancePayable = Math.Round(payable - prior.AmountPaid, 2);
                prior.UpdatedAt = DateTime.UtcNow;

                _db.TeacherSettlementItems.RemoveRange(prior.Items);
                prior.Items.Clear();
                foreach (var l in lines) prior.Items.Add(l);
                updated++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SettlementGenerationResult(created, updated, 0, locked, messages);
    }

    public async Task<(bool ok, string? error)> RecordPaymentAsync(
        Guid settlementId, decimal amount, PaymentMode? mode, string? reference, string? notes,
        Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        if (amount <= 0m) return (false, "Enter an amount greater than zero.");

        var s = await _db.TeacherSettlements
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == settlementId, ct);

        if (s == null) return (false, "Settlement not found.");
        if (s.Status == TeacherSettlementStatus.Cancelled) return (false, "This settlement is cancelled.");

        var remaining = Math.Round(s.TotalPayable - s.AmountPaid, 2);
        if (remaining <= 0m) return (false, "This settlement is already fully paid.");
        if (amount > remaining)
            return (false, $"That is more than the outstanding balance of ₹{remaining:N2}.");

        s.Payments.Add(new TeacherSettlementPayment
        {
            Amount = Math.Round(amount, 2),
            PaidOnUtc = DateTime.UtcNow,
            PaidVia = mode,
            Reference = reference?.Trim(),
            Notes = notes?.Trim(),
            RecordedById = actorId,
            RecordedByName = actorName ?? "system"
        });

        s.AmountPaid = Math.Round(s.AmountPaid + amount, 2);
        s.BalancePayable = Math.Round(s.TotalPayable - s.AmountPaid, 2);
        s.PaidVia = mode ?? s.PaidVia;
        if (!string.IsNullOrWhiteSpace(reference)) s.PaymentReference = reference.Trim();

        if (s.BalancePayable <= 0m)
        {
            s.Status = TeacherSettlementStatus.Paid;
            s.SettledOn = DateTime.UtcNow;
            s.BalancePayable = 0m;
        }
        else s.Status = TeacherSettlementStatus.PartiallyPaid;

        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> CancelAsync(Guid settlementId, string reason, CancellationToken ct = default)
    {
        var s = await _db.TeacherSettlements.FirstOrDefaultAsync(x => x.Id == settlementId, ct);
        if (s == null) return (false, "Settlement not found.");
        if (s.Status == TeacherSettlementStatus.Cancelled) return (false, "This settlement is already cancelled.");
        if (s.AmountPaid > 0m)
            return (false, $"₹{s.AmountPaid:N2} has already been paid against this settlement — it cannot be cancelled.");
        if (string.IsNullOrWhiteSpace(reason)) return (false, "A cancellation reason is required.");

        s.Status = TeacherSettlementStatus.Cancelled;
        s.Notes = string.IsNullOrWhiteSpace(s.Notes) ? reason.Trim() : $"{s.Notes}\nCancelled: {reason.Trim()}";
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary><c>RIO-TS-YYYYMM-NNNN</c>, resetting monthly.</summary>
    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var prefix = $"RIO-TS-{DateTime.UtcNow:yyyyMM}-";
        var last = await _db.TeacherSettlements
            .Where(s => s.SettlementNumber.StartsWith(prefix))
            .OrderByDescending(s => s.SettlementNumber)
            .Select(s => s.SettlementNumber)
            .FirstOrDefaultAsync(ct);

        var next = 1;
        if (last != null && int.TryParse(last[prefix.Length..], out var n)) next = n + 1;
        return prefix + next.ToString("D4");
    }
}
