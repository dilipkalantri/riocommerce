using RioCommerce.Core.DTOs.Invoices;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Idempotent invoice generation + lookup + PDF rendering.
///
/// Numbering: format <c>RIO-INV-YYYYMM-NNNN</c>. Sequence resets every calendar month.
/// Atomic via: pick max N for the prefix, increment, attempt insert, catch unique-violation,
/// retry up to 3 times.
/// </summary>
public sealed class InvoiceService : IInvoiceService
{
    private const int NumberRetryAttempts = 3;
    private const string CompanyDefaultsName = "RioCommerce";

    private readonly RioCommerceDbContext _db;
    private readonly ISettingService _settings;
    private readonly IAppLogService _appLog;
    private readonly ILogger<InvoiceService> _log;
    private readonly IReceiptService _receipts;

    public InvoiceService(RioCommerceDbContext db, ISettingService settings,
        IAppLogService appLog, ILogger<InvoiceService> log, IReceiptService receipts)
    {
        _db = db; _settings = settings; _appLog = appLog; _log = log; _receipts = receipts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Generation
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(Guid? newId, Guid? existingId, string? error)> EnsureForOrderAsync(
        Guid orderId, Guid? actorId = null, string? actorName = null, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.ProductMode)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return (null, null, "Order not found.");

        // Policy: invoice only for paid orders. Pending / Failed / Refunded orders are skipped.
        if (order.PaymentStatus != PaymentStatus.Success)
            return (null, null, "Order is not paid.");

        // Policy: an order funded from the franchise wallet is NOT invoiced. The money was already
        // invoiced when the admin credited the wallet (a WalletTopUp order), so raising a second
        // invoice here would bill the same rupees twice. The franchisee still gets the receipt.
        // This is the single choke point — FinalizeOrderAsync, the franchisee download endpoint and
        // GetOrCreateInvoiceAsync all funnel through here, so none of them can bypass it.
        if (order.PaidFromWallet)
            return (null, null, "Paid from the franchise wallet — invoiced at top-up.");

        // Idempotent — if a live invoice already exists for this order (whether created here
        // or by the legacy OrderAdminService.GetOrCreateInvoiceAsync flow), return it.
        var existing = await _db.Set<Invoice>()
            .Where(i => i.OrderId == orderId && i.Status == InvoiceStatus.Active)
            .Select(i => i.Id)
            .FirstOrDefaultAsync(ct);
        if (existing != Guid.Empty)
            return (null, existing, null);

        var company = await ReadCompanyAsync();
        var invoiceDate = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── Franchise orders are billed to the FRANCHISEE ────────────────────────────────────
        // The franchisee is our customer; the student is only the recipient of the material. The
        // billing party is read from the FRANCHISE RECORD here rather than trusted from the order,
        // so an order created before that rule existed — or by any path that forgets to stamp it —
        // still produces an invoice in the franchisee's legal name and against their GSTIN. The
        // student never appears on a franchise tax invoice; they stay on the order and receipt.
        var isFranchise = order.Source == OrderSource.Franchisee && order.FranchiseId != null;
        var franchise = isFranchise
            ? await _db.Franchises.AsNoTracking().FirstOrDefaultAsync(f => f.Id == order.FranchiseId!.Value, ct)
            : null;

        var billingPartyName = franchise != null
            ? (!string.IsNullOrWhiteSpace(franchise.BusinessName) ? franchise.BusinessName!.Trim() : franchise.Name.Trim())
            : (!string.IsNullOrWhiteSpace(order.BillingName) ? order.BillingName!
                : (!string.IsNullOrWhiteSpace(order.OrgName) ? order.OrgName! : order.StudentName));
        var franchiseGstin = Blank(franchise?.Gstin)?.ToUpperInvariant();

        // Nothing left to bill. FranchisePortalService caps the share at the order total
        // (CapTo(split, Min(payout, total))), so a coupon deep enough to drive the total down to the
        // share leaves the franchisee owing ₹0. There is no consideration and so no supply to declare.
        //
        // Guarded here rather than by letting it fall through, because the two zero-conventions below
        // disagree: invoiceTotal would be 0, while GstRowCalculator reads a 0 invoicedValue as
        // "not supplied" and computes on the GROSS instead — persisting a ₹0 invoice carrying the full
        // gross CGST/SGST, the exact defect migration 0033 exists to clear. The calculator's guard is
        // left alone on purpose: the report path passes null there and relies on 0 meaning "unknown".
        //
        // Reported through the third slot like the wallet skip above — a skip with a reason, not a
        // failure. Requires share > 0: legacy orders with share AND net both zero are not this case,
        // and still take the order total on the next line.
        if (isFranchise && order.FranchiseShareAmount > 0 && order.FranchiseNetPayable <= 0)
            return (null, null, "The franchisee's share covers the whole order — nothing left to invoice.");

        // What the franchisee owes. Portal orders net their share off up front; admin-created orders
        // bill the full amount and settle commission separately. Orders written before either value
        // was recorded (both zero) fall back to the order total rather than invoicing ₹0.
        var invoiceTotal = isFranchise && (order.FranchiseShareAmount > 0 || order.FranchiseNetPayable > 0)
            ? order.FranchiseNetPayable
            : order.TotalAmount;

        // Tax on what is ACTUALLY BILLED, not on the student-facing gross.
        //
        // A franchise invoice charges the net of the franchisee's commission — ₹98 on a ₹118 sale —
        // so the value of that supply is ₹98 and the tax inside it is ₹14.95, not the ₹18 embedded in
        // the ₹118 nobody is invoiced. Storing the gross split alongside a net TotalAmount produced a
        // document that did not add up: taxable ₹100 + GST ₹18 = ₹118 against a stated total of ₹98.
        //
        // Delegated to GstRowCalculator rather than recomputed here because the GST report and the
        // invoice PDF already scale the same way. Three readers, one implementation — they can no
        // longer disagree about what tax a franchise invoice carries.
        //
        // For every non-franchise invoice invoicedValue == order.TotalAmount, which takes the
        // calculator's pass-through branch: the order's own recorded CGST/SGST/IGST are returned
        // untouched. B2C and admin invoices are byte-for-byte unchanged by this, except that an
        // admin order storing only a GST total now gets its components derived instead of zeroed.
        var tax = GstRowCalculator.For(order, refundedFromLedger: 0m, invoicedValue: invoiceTotal);

        // Build the entity (number stamped inside the retry loop).
        var inv = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceDate = invoiceDate,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = billingPartyName,
            // On a franchise invoice the billed party is the franchisee — don't leak the student's
            // contact into the invoice. (The student remains on the order/receipt as a reference.)
            CustomerEmail = isFranchise ? null : order.StudentEmail,
            CustomerPhone = isFranchise ? null : order.StudentPhone,
            // Franchise invoices take the whole address block from the franchise record; everything
            // else keeps using the order's own billing snapshot.
            BillingAddress = franchise != null ? Blank(franchise.AddressLine) : order.BillingAddress,
            BillingCity = franchise != null ? Blank(franchise.City) : (order.BillingCity ?? order.StudentCity),
            BillingState = franchise != null ? Blank(franchise.State) : order.BillingState,
            BillingPincode = franchise != null ? Blank(franchise.PinCode) : order.BillingPincode,
            CustomerGstin = franchise != null ? franchiseGstin : order.GstNumber,
            GstClassification = franchise != null ? (franchiseGstin == null ? "B2C" : "B2B") : order.GstClassification,
            ReverseCharge = order.ReverseCharge,
            GstRate = order.Items.Select(it => it.GstRate ?? 0).DefaultIfEmpty(0).Max(),
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            // Taxable value and the tax split, both on the invoiced basis — so
            // TaxableAmount + CGST + SGST + IGST == TotalAmount on every invoice this method writes.
            TaxableAmount = tax.Taxable,
            CgstAmount = tax.Cgst,
            SgstAmount = tax.Sgst,
            IgstAmount = tax.Igst,
            ShippingCharges = order.ShippingCharges,
            FranchiseShareAmount = isFranchise ? order.FranchiseShareAmount : 0m,
            TotalAmount = invoiceTotal,
            Currency = "INR",
            PaymentMode = order.PaymentMode,
            // Snapshot what the customer actually paid with — set on the order moments earlier by
            // the settlement path, so it's already current when this runs.
            GatewayPaymentMode = order.GatewayPaymentMode,
            PaymentReference = null, // No clean txn-id column on Order; left null. Populate later from payment service if needed.
            PaidAt = order.ConfirmedAt ?? order.UpdatedAt,
            Status = InvoiceStatus.Active,
            GeneratedByUserId = actorId,
            GeneratedByName = actorName ?? "system",
            CompanyName = company.Name,
            CompanyGstin = company.Gstin,
            CompanyAddress = company.Address,
            CompanyPhone = company.Phone,
            CompanyEmail = company.Email,
            LineItems = order.Items.Select((it, idx) => new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                LineNumber = idx + 1,
                OrderItemId = it.Id,
                ProductId = it.ProductId,
                Description = it.Product?.Title ?? "Course",
                ModeName = it.ModeName ?? it.ProductMode?.ModeName,
                HsnCode = null,                    // Populate per product when we capture HSN — not modeled today.
                Quantity = it.Quantity,
                UnitPrice = it.UnitPrice,
                Discount = it.Discount,
                GstRate = it.GstRate,
                GstAmount = it.GstAmount,
                LineTotal = it.LineTotal,
            }).ToList(),
        };

        // Retry loop for number collision under concurrent generation.
        for (int attempt = 1; attempt <= NumberRetryAttempts; attempt++)
        {
            inv.InvoiceNumber = await AllocateInvoiceNumberAsync(inv.InvoiceDate, ct);
            _db.Set<Invoice>().Add(inv);
            try
            {
                await _db.SaveChangesAsync(ct);
                await _appLog.InfoAsync("Invoices",
                    $"Invoice {inv.InvoiceNumber} generated for order {order.OrderNumber}.",
                    eventCode: "invoice.generated",
                    properties: new { inv.InvoiceNumber, OrderNumber = order.OrderNumber, inv.TotalAmount },
                    orderId: order.Id, ct: ct);
                return (inv.Id, null, null);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < NumberRetryAttempts)
            {
                // Number collision under concurrency — detach and retry with a fresh number.
                _db.Entry(inv).State = EntityState.Detached;
                foreach (var li in inv.LineItems) _db.Entry(li).State = EntityState.Detached;
                _log.LogWarning("Invoice number collision attempt={Attempt} order={Order} — retrying", attempt, order.OrderNumber);
            }
            catch (DbUpdateException ex)
            {
                await _appLog.ErrorAsync("Invoices",
                    $"Failed to save invoice for order {order.OrderNumber}: {ex.InnerException?.Message ?? ex.Message}",
                    ex, "invoice.save_failed", orderId: order.Id, ct: ct);
                return (null, null, ex.InnerException?.Message ?? ex.Message);
            }
        }
        return (null, null, "Could not allocate a unique invoice number after retries.");
    }

    public async Task<int> EnsureForPaidOrdersAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var paidOrderIds = await _db.Orders
            .Where(o => o.PaymentStatus == PaymentStatus.Success && o.ConfirmedAt >= sinceUtc)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var alreadyInvoiced = await _db.Set<Invoice>()
            .Where(i => paidOrderIds.Contains(i.OrderId) && i.Status == InvoiceStatus.Active)
            .Select(i => i.OrderId)
            .ToListAsync(ct);
        var skip = alreadyInvoiced.ToHashSet();

        var generated = 0;
        foreach (var id in paidOrderIds)
        {
            if (skip.Contains(id)) continue;
            var (newId, _, _) = await EnsureForOrderAsync(id, ct: ct);
            if (newId.HasValue) generated++;
        }
        return generated;
    }

    public async Task<bool> CancelAsync(Guid invoiceId, string reason, Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        var inv = await _db.Set<Invoice>().FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (inv == null) return false;
        if (inv.Status == InvoiceStatus.Cancelled) return true; // already done
        inv.Status = InvoiceStatus.Cancelled;
        inv.CancelledAt = DateTime.UtcNow;
        inv.CancelledReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by admin" : reason.Trim();
        await _db.SaveChangesAsync(ct);
        await _appLog.WarnAsync("Invoices",
            $"Invoice {inv.InvoiceNumber} cancelled: {inv.CancelledReason}",
            eventCode: "invoice.cancelled",
            properties: new { inv.InvoiceNumber, Actor = actorName, Reason = inv.CancelledReason },
            orderId: inv.OrderId, ct: ct);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<InvoiceListItem>> ListAsync(InvoiceListFilter filter, CancellationToken ct = default)
    {
        var q = _db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (filter.Status.HasValue) q = q.Where(i => i.Status == filter.Status.Value);
        if (filter.OrderId.HasValue) q = q.Where(i => i.OrderId == filter.OrderId.Value);
        if (filter.FromUtc.HasValue) q = q.Where(i => i.InvoiceDate >= DateOnly.FromDateTime(filter.FromUtc.Value));
        if (filter.ToUtc.HasValue)   q = q.Where(i => i.InvoiceDate <= DateOnly.FromDateTime(filter.ToUtc.Value));
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var s = $"%{filter.Query.Trim()}%";
            q = q.Where(i => EF.Functions.ILike(i.InvoiceNumber, s)
                          || EF.Functions.ILike(i.OrderNumber, s)
                          || EF.Functions.ILike(i.CustomerName!, s));
        }
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 200);
        return await q.OrderByDescending(i => i.InvoiceDate)
            .Skip((page - 1) * size).Take(size)
            .Select(i => new InvoiceListItem
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                InvoiceDate = i.InvoiceDate.ToDateTime(TimeOnly.MinValue),
                OrderId = i.OrderId,
                OrderNumber = i.OrderNumber,
                CustomerName = i.CustomerName ?? string.Empty,
                TotalAmount = i.TotalAmount,
                Currency = i.Currency,
                Status = i.Status,
                PaymentMode = i.PaymentMode,
                GatewayPaymentMode = i.GatewayPaymentMode,
                CreatedAt = i.CreatedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<InvoiceDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await _db.Set<Invoice>().AsNoTracking()
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        return inv == null ? null : Map(inv);
    }

    public async Task<InvoiceDetail?> GetByOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var inv = await _db.Set<Invoice>().AsNoTracking()
            .Include(i => i.LineItems)
            .Where(i => i.OrderId == orderId && i.Status == InvoiceStatus.Active)
            .OrderByDescending(i => i.InvoiceDate)
            .FirstOrDefaultAsync(ct);
        return inv == null ? null : Map(inv);
    }

    public async Task<(byte[] bytes, string filename)?> RenderPdfAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var detail = await GetAsync(invoiceId, ct);
        if (detail == null) return null;
        // Franchise invoices carry the full per-product bifurcation on the PDF.
        if (detail.FranchiseShareAmount > 0 && detail.OrderId != Guid.Empty)
        {
            var bif = await _receipts.GetBifurcationAsync(detail.OrderId, ct);
            if (bif is { IsFranchiseOrder: true }) detail.Bifurcation = bif;
        }
        var bytes = InvoicePdfRenderer.Render(detail);
        var safeNumber = detail.InvoiceNumber.Replace('/', '-').Replace('\\', '-');
        return (bytes, $"{safeNumber}.pdf");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Numbering — RIO-INV-YYYYMM-NNNN, monthly reset
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> AllocateInvoiceNumberAsync(DateOnly invoiceDate, CancellationToken ct)
    {
        // Sequence resets per calendar month. Using the date's own year/month keeps numbering
        // consistent regardless of how UTC/local conversion would otherwise straddle midnight.
        var monthPrefix = $"RIO-INV-{invoiceDate:yyyyMM}-";
        var lastMaxNumber = await _db.Set<Invoice>()
            .Where(i => i.InvoiceNumber.StartsWith(monthPrefix))
            .Select(i => i.InvoiceNumber)
            .ToListAsync(ct);

        var next = 1;
        foreach (var num in lastMaxNumber)
        {
            var tail = num.Substring(monthPrefix.Length);
            if (int.TryParse(tail, out var n) && n >= next) next = n + 1;
        }
        return $"{monthPrefix}{next:D4}";
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql exposes PostgreSQL error code 23505 for unique violations.
        var inner = ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string;
        return inner == "23505";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Company info — read from AppSettings with sensible defaults
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<CompanyProfile> ReadCompanyAsync()
    {
        return new CompanyProfile
        {
            Name    = (await _settings.GetStringAsync("company.name"))    ?? CompanyDefaultsName,
            Gstin   =  await _settings.GetStringAsync("company.gstin"),
            Address =  await _settings.GetStringAsync("company.address"),
            Phone   =  await _settings.GetStringAsync("company.phone"),
            Email   =  await _settings.GetStringAsync("company.email"),
            Website =  await _settings.GetStringAsync("company.website"),
        };
    }

    private static InvoiceDetail Map(Invoice i) => new()
    {
        Id = i.Id,
        InvoiceNumber = i.InvoiceNumber,
        // Map DateOnly → DateTime for the DTO so existing Razor formatting code keeps working.
        InvoiceDate = i.InvoiceDate.ToDateTime(TimeOnly.MinValue),
        OrderId = i.OrderId,
        OrderNumber = i.OrderNumber,
        CustomerName = i.CustomerName ?? string.Empty,
        CustomerEmail = i.CustomerEmail,
        CustomerPhone = i.CustomerPhone,
        BillingAddress = i.BillingAddress,
        BillingCity = i.BillingCity,
        BillingState = i.BillingState,
        BillingPincode = i.BillingPincode,
        CustomerGstin = i.CustomerGstin,
        GstClassification = i.GstClassification,
        ReverseCharge = i.ReverseCharge,
        GstRatePct = i.GstRate,
        Subtotal = i.Subtotal,
        DiscountAmount = i.DiscountAmount,
        CgstAmount = i.CgstAmount,
        SgstAmount = i.SgstAmount,
        IgstAmount = i.IgstAmount,
        ShippingCharges = i.ShippingCharges,
        FranchiseShareAmount = i.FranchiseShareAmount,
        TotalAmount = i.TotalAmount,
        Currency = i.Currency,
        PaymentMode = i.PaymentMode,
        GatewayPaymentMode = i.GatewayPaymentMode,
        PaymentReference = i.PaymentReference,
        PaidAt = i.PaidAt,
        Status = i.Status,
        CancelledAt = i.CancelledAt,
        CancelledReason = i.CancelledReason,
        GeneratedByName = i.GeneratedByName,
        Notes = i.Notes,
        CreatedAt = i.CreatedAt,
        CompanyName = i.CompanyName ?? string.Empty,
        CompanyGstin = i.CompanyGstin,
        CompanyAddress = i.CompanyAddress,
        CompanyPhone = i.CompanyPhone,
        CompanyEmail = i.CompanyEmail,
        LineItems = i.LineItems.OrderBy(l => l.LineNumber).Select(l => new InvoiceLineItemDto
        {
            LineNumber = l.LineNumber,
            Description = l.Description,
            ModeName = l.ModeName,
            HsnCode = l.HsnCode,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            Discount = l.Discount,
            GstRate = l.GstRate,
            GstAmount = l.GstAmount,
            LineTotal = l.LineTotal,
        }).ToList(),
    };
}
