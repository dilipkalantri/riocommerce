using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

// Stateless payout maths over the order/refund/rule ledger. Materialises the minimal raw rows then
// computes in memory (service-method share logic isn't EF-translatable). Never persists anything.
public class PayoutCalculationService : IPayoutCalculationService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };
    // Outstanding = owed but not yet settled out.
    private static readonly PayoutStatus[] Owed = { PayoutStatus.Draft, PayoutStatus.PendingApproval, PayoutStatus.Approved, PayoutStatus.Processing };

    private readonly RioCommerceDbContext _db;
    private readonly ISettingService _settings;
    private readonly IFacultySharingService _facultyShares;
    public PayoutCalculationService(RioCommerceDbContext db, ISettingService settings, IFacultySharingService facultyShares)
    {
        _db = db; _settings = settings; _facultyShares = facultyShares;
    }

    public async Task<List<PayoutPreviewDto>> PreviewAsync(PayoutType type, DateTime fromUtc, DateTime toUtc, bool excludeAlreadySettled = true)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        var settled = excludeAlreadySettled ? await LoadSettledKeysAsync(type) : new HashSet<string>();
        return type switch
        {
            PayoutType.Faculty => await FacultyPreviewAsync(fromUtc, toUtc, settled),
            PayoutType.Franchise => await FranchisePreviewAsync(fromUtc, toUtc, settled),
            PayoutType.Affiliate => await AffiliatePreviewAsync(fromUtc, toUtc, settled),
            _ => new List<PayoutPreviewDto>()   // Vendor: no automatic source yet (manual payouts only)
        };
    }

    public async Task<List<PayableSliceDto>> OutstandingPayableAsync()
    {
        var rows = await _db.Payouts.Where(p => Owed.Contains(p.Status))
            .GroupBy(p => p.BeneficiaryType)
            .Select(g => new PayableSliceDto(g.Key, g.Count(), g.Sum(x => x.NetAmount)))
            .ToListAsync();
        return rows.OrderByDescending(r => r.Amount).ToList();
    }

    // ── Dedupe: every (beneficiary, source, order/item/refund) tuple already in a non-cancelled payout ──
    private static string Key(PayoutType t, Guid benId, PayoutItemSource s, Guid? orderItemId, Guid? orderId, Guid? refundId)
        => $"{(int)t}:{benId}:{(int)s}:{orderItemId}:{orderId}:{refundId}";

    private async Task<HashSet<string>> LoadSettledKeysAsync(PayoutType type)
    {
        var rows = await _db.PayoutItems
            .Where(i => i.Payout.BeneficiaryType == type && i.Payout.Status != PayoutStatus.Cancelled)
            .Select(i => new { i.Payout.BeneficiaryType, i.Payout.BeneficiaryId, i.Source, i.OrderItemId, i.OrderId, i.RefundId })
            .ToListAsync();
        return rows.Select(r => Key(r.BeneficiaryType, r.BeneficiaryId, r.Source, r.OrderItemId, r.OrderId, r.RefundId)).ToHashSet();
    }

    // ── Faculty: per-product revenue-share rules, net of refund clawbacks ──
    //
    // Earnings come from the FacultyShareEntry ledger written at order confirmation, which snapshots
    // the rule AND each faculty's GST registration at the time. Recomputing from live rules — what
    // this method used to do — meant editing a rate silently restated payouts that were already
    // settled. Orders predating the ledger still fall back to a recompute, because reporting ₹0 for
    // every historical month would be worse than reporting a recomputed figure.
    //
    // The share arithmetic itself is no longer here at all: it lives in IFacultyShareCalculator via
    // FacultySharingService, so this method and the admin screens cannot disagree about what a
    // faculty earned. The old inline `LineTotal × pct / 100` applied the share to the GST-INCLUSIVE
    // gross and never added GST on the faculty's own supply.
    private async Task<List<PayoutPreviewDto>> FacultyPreviewAsync(DateTime from, DateTime to, HashSet<string> settled)
    {
        var tdsPct = await _settings.GetDecimalAsync(FinanceSettingsService.TdsPct, 0m);
        var facultySettings = await _facultyShares.GetSettingsAsync();
        var acc = new Dictionary<Guid, Acc>();

        // ── Earnings from the ledger ──
        // Aliased: `from` is the LINQ query keyword, so the parameter cannot be referenced by that
        // name inside a query expression.
        var rangeStart = from;
        var rangeEnd = to;

        // Joined to Orders rather than filtered via a navigation so the Order soft-delete query filter
        // applies — a deleted order must stop generating a payout liability.
        var entries = await (
                from e in _db.FacultyShareEntries.AsNoTracking()
                join o in _db.Orders.AsNoTracking() on e.OrderId equals o.Id
                where e.EarnedAt >= rangeStart && e.EarnedAt < rangeEnd && !NonRevenue.Contains(o.Status)
                select new
                {
                    e.FacultyId, Name = e.Faculty.DisplayName, e.OrderId, e.OrderItemId, e.OrderNumber,
                    e.ShareAmount, e.GstOnShare, e.TotalPayout
                })
            .ToListAsync();

        var ledgerOrderIds = entries.Select(e => e.OrderId).ToHashSet();

        foreach (var e in entries)
        {
            if (settled.Contains(Key(PayoutType.Faculty, e.FacultyId, PayoutItemSource.Earning, e.OrderItemId, e.OrderId, null))) continue;
            var a = Get(acc, e.FacultyId, e.Name);
            a.Orders.Add(e.OrderId);
            a.Gross += e.TotalPayout;
            a.TaxExemptGst += e.GstOnShare;
            a.Lines.Add(new PayoutLineDto(PayoutItemSource.Earning, e.OrderId, e.OrderNumber, e.OrderItemId, null, null,
                e.GstOnShare > 0
                    ? $"Share on {e.OrderNumber} (₹{e.ShareAmount:N2} + ₹{e.GstOnShare:N2} GST)"
                    : $"Share on {e.OrderNumber}",
                e.TotalPayout));
        }

        // ── Fallback for pre-ledger orders ──
        var legacy = await _facultyShares.ComputeFromRulesForPayoutAsync(from, to, ledgerOrderIds);
        foreach (var l in legacy)
        {
            if (settled.Contains(Key(PayoutType.Faculty, l.FacultyId, PayoutItemSource.Earning, l.OrderItemId, l.OrderId, null))) continue;
            var a = Get(acc, l.FacultyId, l.Name);
            a.Orders.Add(l.OrderId);
            a.Gross += l.Share + l.Gst;
            a.TaxExemptGst += l.Gst;
            a.Lines.Add(new PayoutLineDto(PayoutItemSource.Earning, l.OrderId, l.OrderNumber, l.OrderItemId, null, null,
                $"Share on {l.OrderNumber} (recomputed — no ledger entry)", l.Share + l.Gst));
        }

        if (acc.Count == 0) return new();

        await ApplyFacultyClawbacksAsync(from, to, settled, acc);

        return Finalize(acc, PayoutType.Faculty, from, to, tdsPct,
            tdsOnGrossExcluding: facultySettings.TdsOnShareExcludingGst);
    }

    /// <summary>
    /// Refund clawbacks against ledger earnings. Prorated from the ledger row rather than recomputed
    /// from the rule, so a clawback always reverses what was actually credited — a rate change between
    /// the sale and the refund cannot leave a faculty owing more or less than they were paid.
    /// </summary>
    private async Task ApplyFacultyClawbacksAsync(
        DateTime from, DateTime to, HashSet<string> settled, Dictionary<Guid, Acc> acc)
    {
        var refunds = await _db.Refunds
            .Where(r => r.Status == RefundStatus.Succeeded && r.Order.CreatedAt >= from && r.Order.CreatedAt < to
                        && r.Order.Status != OrderStatus.Refunded)
            .Select(r => new
            {
                r.Id, r.Amount, r.OrderId, OrderTotal = r.Order.TotalAmount, OrderNumber = r.Order.OrderNumber,
                Items = r.Items.Select(it => new { it.OrderItemId, it.Quantity, it.Amount }).ToList()
            })
            .ToListAsync();
        if (refunds.Count == 0) return;

        var orderIds = refunds.Select(r => r.OrderId).Distinct().ToList();

        var ledger = await _db.FacultyShareEntries.AsNoTracking()
            .Where(e => orderIds.Contains(e.OrderId))
            .Select(e => new
            {
                e.FacultyId, Name = e.Faculty.DisplayName, e.OrderId, e.OrderItemId, e.TotalPayout
            })
            .ToListAsync();
        if (ledger.Count == 0) return;

        var byItem = ledger.GroupBy(e => e.OrderItemId).ToDictionary(g => g.Key, g => g.ToList());
        var byOrder = ledger.GroupBy(e => e.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        // Line totals only for proration of the item-level case; a refund of part of a line claws back
        // the same fraction of that line's share.
        var lineTotals = await _db.OrderItems.AsNoTracking().Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new { i.Id, i.LineTotal }).ToListAsync();
        var lineTotalById = lineTotals.ToDictionary(i => i.Id, i => i.LineTotal);

        foreach (var refund in refunds)
        {
            if (refund.Items.Count > 0)
            {
                foreach (var ri in refund.Items)
                {
                    if (!byItem.TryGetValue(ri.OrderItemId, out var earned)) continue;
                    // Fraction of THIS line that was refunded. A zero/unknown line total means we
                    // cannot prorate safely, so treat it as a full reversal of that line rather than
                    // guessing a partial one.
                    var lineTotal = lineTotalById.TryGetValue(ri.OrderItemId, out var lt) ? lt : 0m;
                    var fraction = lineTotal > 0 ? Math.Min(1m, ri.Amount / lineTotal) : 1m;

                    foreach (var e in earned)
                        AddClawback(acc, settled, e.FacultyId, e.Name, refund.Id, e.OrderItemId,
                            refund.OrderId, refund.OrderNumber, Math.Round(e.TotalPayout * fraction, 2));
                }
            }
            else if (refund.OrderTotal > 0 && byOrder.TryGetValue(refund.OrderId, out var orderEarned))
            {
                // Amount-only refund: prorate every faculty's earning on the order by the refunded
                // fraction of the order value.
                var fraction = Math.Min(1m, refund.Amount / refund.OrderTotal);
                foreach (var e in orderEarned)
                    AddClawback(acc, settled, e.FacultyId, e.Name, refund.Id, e.OrderItemId,
                        refund.OrderId, refund.OrderNumber, Math.Round(e.TotalPayout * fraction, 2));
            }
        }
    }

    // ── Franchise: configurable commission % on attributed order revenue, net of refunds ──
    private async Task<List<PayoutPreviewDto>> FranchisePreviewAsync(DateTime from, DateTime to, HashSet<string> settled)
    {
        var pct = await _settings.GetDecimalAsync(FinanceSettingsService.FranchiseCommissionPct, 0m);
        if (pct <= 0) return new();   // no commission configured → no phantom liabilities

        // No commission is due on a wallet top-up — it's the franchisee funding their own account.
        var orders = await _db.Orders.ExcludeWalletTopUps()
            .Where(o => o.FranchiseId != null && o.CreatedAt >= from && o.CreatedAt < to && !NonRevenue.Contains(o.Status))
            .Select(o => new { o.Id, o.OrderNumber, o.TotalAmount, FranchiseId = o.FranchiseId!.Value, FranchiseName = o.Franchise!.Name })
            .ToListAsync();

        var acc = new Dictionary<Guid, Acc>();
        foreach (var o in orders)
        {
            var commission = Math.Round(o.TotalAmount * pct / 100m, 2);
            if (commission <= 0) continue;
            if (settled.Contains(Key(PayoutType.Franchise, o.FranchiseId, PayoutItemSource.Earning, null, o.Id, null))) continue;
            var a = Get(acc, o.FranchiseId, o.FranchiseName);
            a.Orders.Add(o.Id);
            a.Gross += commission;
            a.Lines.Add(new PayoutLineDto(PayoutItemSource.Earning, o.Id, o.OrderNumber, null, null, null,
                $"{pct:0.##}% commission on {o.OrderNumber}", commission));
        }

        var refunds = await _db.Refunds
            .Where(r => r.Status == RefundStatus.Succeeded && r.Order.FranchiseId != null
                        && r.Order.CreatedAt >= from && r.Order.CreatedAt < to && r.Order.Status != OrderStatus.Refunded)
            .Select(r => new { r.Id, r.Amount, r.OrderId, OrderNumber = r.Order.OrderNumber, FranchiseId = r.Order.FranchiseId!.Value, FranchiseName = r.Order.Franchise!.Name })
            .ToListAsync();
        foreach (var r in refunds)
        {
            var claw = Math.Round(r.Amount * pct / 100m, 2);
            AddClawback(acc, settled, r.FranchiseId, r.FranchiseName, r.Id, null, r.OrderId, r.OrderNumber, claw, PayoutType.Franchise);
        }

        return Finalize(acc, PayoutType.Franchise, from, to, 0m);   // franchise commission: no TDS by default
    }

    // ── Affiliate: pre-computed per-referral commission (unpaid only) ──
    private async Task<List<PayoutPreviewDto>> AffiliatePreviewAsync(DateTime from, DateTime to, HashSet<string> settled)
    {
        var tdsPct = await _settings.GetDecimalAsync(FinanceSettingsService.TdsPct, 0m);
        var referrals = await _db.AffiliateReferrals
            .Where(r => !r.IsPaid && r.CreatedAt >= from && r.CreatedAt < to && r.Commission > 0)
            .Select(r => new { r.AffiliateId, AffiliateName = r.Affiliate.Name, r.OrderId, r.OrderNumber, r.Commission })
            .ToListAsync();

        var acc = new Dictionary<Guid, Acc>();
        foreach (var r in referrals)
        {
            if (settled.Contains(Key(PayoutType.Affiliate, r.AffiliateId, PayoutItemSource.Earning, null, r.OrderId, null))) continue;
            var a = Get(acc, r.AffiliateId, r.AffiliateName);
            a.Orders.Add(r.OrderId);
            a.Gross += r.Commission;
            a.Lines.Add(new PayoutLineDto(PayoutItemSource.Earning, r.OrderId, r.OrderNumber, null, null, null,
                $"Referral commission on {r.OrderNumber}", r.Commission));
        }
        return Finalize(acc, PayoutType.Affiliate, from, to, tdsPct);
    }

    // ── helpers ──
    private sealed class Acc
    {
        public string Name = "";
        public readonly HashSet<Guid> Orders = new();
        public decimal Gross;
        public decimal Clawback;   // stored positive
        /// <summary>
        /// The portion of <see cref="Gross"/> that is GST charged by the beneficiary on their own
        /// supply — a pass-through, not income, so TDS must not be deducted from it. Zero for payout
        /// types that carry no tax component (franchise, affiliate), which is why those paths are
        /// unaffected by the TDS-base adjustment in <c>Finalize</c>.
        /// </summary>
        public decimal TaxExemptGst;
        public readonly List<PayoutLineDto> Lines = new();
    }

    private static Acc Get(Dictionary<Guid, Acc> acc, Guid id, string name)
    {
        if (!acc.TryGetValue(id, out var a)) { a = new Acc { Name = name }; acc[id] = a; }
        return a;
    }

    private static void AddClawback(Dictionary<Guid, Acc> acc, HashSet<string> settled, Guid benId, string name,
        Guid refundId, Guid? orderItemId, Guid orderId, string orderNumber, decimal claw, PayoutType type = PayoutType.Faculty)
    {
        if (claw <= 0) return;
        if (settled.Contains(Key(type, benId, PayoutItemSource.RefundClawback, orderItemId, orderId, refundId))) return;
        var a = Get(acc, benId, name);
        a.Clawback += claw;
        a.Lines.Add(new PayoutLineDto(PayoutItemSource.RefundClawback, orderId, orderNumber, orderItemId, refundId, null,
            $"Refund clawback on {orderNumber}", -claw));
    }

    /// <param name="tdsOnGrossExcluding">
    /// When true (the default), <see cref="Acc.TaxExemptGst"/> is removed from the TDS base — TDS under
    /// s.194J applies to the professional fee, not to the GST the payee charges on top of it. The
    /// exempt amount is prorated by the same fraction as the clawback so a partly-refunded earning
    /// doesn't shelter more tax than it earned. Franchise and affiliate accumulators carry no GST
    /// component, so this is a no-op for them either way.
    /// </param>
    private static List<PayoutPreviewDto> Finalize(
        Dictionary<Guid, Acc> acc, PayoutType type, DateTime from, DateTime to, decimal tdsPct,
        bool tdsOnGrossExcluding = true)
    {
        var result = new List<PayoutPreviewDto>();
        foreach (var (id, a) in acc)
        {
            var gross = Math.Round(a.Gross, 2);
            var clawback = Math.Round(a.Clawback, 2);
            var taxable = Math.Max(0m, gross - clawback);

            var exemptGst = 0m;
            if (tdsOnGrossExcluding && a.TaxExemptGst > 0m && gross > 0m)
                exemptGst = Math.Round(Math.Round(a.TaxExemptGst, 2) * taxable / gross, 2);

            var tdsBase = Math.Max(0m, taxable - exemptGst);
            var tax = tdsPct > 0 ? Math.Round(tdsBase * tdsPct / 100m, 2) : 0m;
            var net = gross - clawback - tax;
            result.Add(new PayoutPreviewDto(type, id, a.Name, a.Orders.Count, gross, clawback, tax, net, a.Lines));
        }
        return result.OrderByDescending(r => r.Net).ToList();
    }
}
