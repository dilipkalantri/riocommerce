using RioCommerce.Core.DTOs.Invoices;
using RioCommerce.Core.DTOs.Orders;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.Receipts;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public sealed class ReceiptService : IReceiptService
{
    private const string SellerState = "Maharashtra";
    private const string CompanyDefaultName = "Vijaypath";

    private readonly RioCommerceDbContext _db;
    private readonly ISettingService _settings;
    private readonly IFranchiseShareCalculator _shares;

    public ReceiptService(RioCommerceDbContext db, ISettingService settings, IFranchiseShareCalculator shares)
    {
        _db = db; _settings = settings; _shares = shares;
    }

    public async Task<ReceiptDetail?> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.IgnoreQueryFilters()
            .Include(o => o.Items)
            .Include(o => o.Transactions)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return null;

        var company = await ReadCompanyAsync();

        // SKU isn't snapshotted on the order line, so resolve it from the catalogue for display.
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var skus = await _db.Products.AsNoTracking().IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku })
            .ToDictionaryAsync(x => x.Id, x => x.Sku, ct);

        // Determine intra/inter-state from the billing state vs seller state.
        var intraState = string.IsNullOrWhiteSpace(order.BillingState)
            || string.Equals(order.BillingState?.Trim(), SellerState, StringComparison.OrdinalIgnoreCase);

        var taxable = Math.Round(order.Subtotal - order.DiscountAmount, 2);
        var headlineRate = order.Items.Select(i => i.GstRate ?? 0).DefaultIfEmpty(0).Max();
        // If the split wasn't stored (legacy), derive it for display.
        decimal cgst = order.CgstAmount, sgst = order.SgstAmount, igst = order.IgstAmount;
        if (cgst == 0 && sgst == 0 && igst == 0 && order.GstAmount > 0)
        {
            if (intraState) { cgst = Math.Round(order.GstAmount / 2m, 2); sgst = order.GstAmount - cgst; }
            else igst = order.GstAmount;
        }

        var lifecycle = OrderLifecycleMap.Resolve(order.Status, order.PaymentStatus);

        var lines = order.Items.OrderBy(i => i.ProductTitle).Select((it, idx) => new ReceiptLine
        {
            LineNumber = idx + 1,
            Description = it.ProductTitle,
            ModeName = it.ModeName,
            Sku = skus.TryGetValue(it.ProductId, out var sku) ? sku : null,
            Quantity = it.Quantity,
            UnitPrice = it.UnitPrice,
            Discount = it.Discount,
            GstRate = it.GstRate ?? 0,
            LineTotal = it.LineTotal
        }).ToList();

        var bifurcation = await GetBifurcationAsync(orderId, ct);

        return new ReceiptDetail
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            ReceiptNumber = BuildReceiptNumber(order.OrderNumber),
            OrderDateUtc = order.CreatedAt,
            CustomerName = order.StudentName,
            CustomerPhone = order.StudentPhone,
            CustomerEmail = order.StudentEmail,
            CustomerCity = order.StudentCity,
            CustomerAddress = order.BillingAddress,
            CustomerBillingCity = order.BillingCity,
            CustomerState = order.BillingState,
            CustomerPincode = order.BillingPincode,
            PaymentStatus = order.PaymentStatus.ToString(),
            OrderStatus = OrderLifecycleMap.Label(lifecycle),
            PaymentMode = order.PaymentMode.ToString(),
            GatewayPaymentMode = order.GatewayPaymentMode,
            PaidAtUtc = order.PaymentStatus == PaymentStatus.Success ? (order.ConfirmedAt ?? order.UpdatedAt) : null,
            TransactionId = ResolveTransactionId(order),
            ReferredBy = ResolveReferredBy(order),
            GstClassification = order.GstClassification,
            ReverseCharge = order.ReverseCharge,
            CustomerGstin = order.GstNumber,
            IntraState = intraState,
            Subtotal = order.Subtotal,
            DiscountAmount = order.DiscountAmount,
            PaidFromWallet = order.PaidFromWallet,
            TaxableAmount = taxable,
            GstRate = headlineRate,
            CgstAmount = cgst,
            SgstAmount = sgst,
            IgstAmount = igst,
            GstAmount = order.GstAmount,
            ShippingCharges = order.ShippingCharges,
            TotalAmount = order.TotalAmount,
            Lines = lines,
            Franchise = bifurcation,
            CompanyName = company.Name,
            CompanyGstin = company.Gstin,
            CompanyAddress = company.Address,
            CompanyPhone = company.Phone,
            CompanyEmail = company.Email,
            CompanyWebsite = company.Website
        };
    }

    /// <summary>Receipts have no DB-side number series (invoices do). Derive a stable, human-readable
    /// reference from the order number so the same order always shows the same receipt no.</summary>
    private static string BuildReceiptNumber(string orderNumber)
        => string.IsNullOrWhiteSpace(orderNumber) ? "RCPT" : $"RCPT-{orderNumber}";

    /// <summary>Gateway reference for the successful capture: prefer the ledger's gateway id, then its
    /// manual reference (UTR/cheque no). Refunds and failed attempts are ignored.</summary>
    private static string? ResolveTransactionId(Order order)
    {
        var capture = order.Transactions
            .Where(t => !t.IsRefund && t.Status == PaymentStatus.Success)
            .OrderByDescending(t => t.PaidOnUtc ?? t.CreatedAt)
            .FirstOrDefault();
        if (capture == null) return null;
        return !string.IsNullOrWhiteSpace(capture.GatewayTransactionId)
            ? capture.GatewayTransactionId
            : (string.IsNullOrWhiteSpace(capture.Reference) ? null : capture.Reference);
    }

    /// <summary>"Referred By" label snapshotted at checkout. When the source was "Other", the
    /// customer-typed text is appended (mirrors how admin order views render it).</summary>
    private static string? ResolveReferredBy(Order order)
    {
        var name = order.ReferralSourceName?.Trim();
        var custom = order.ReferralCustomText?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return string.IsNullOrWhiteSpace(custom) ? null : custom;
        return order.ReferralType == ReferralSourceType.Other && !string.IsNullOrWhiteSpace(custom)
            ? $"{name} — {custom}"
            : name;
    }

    public async Task<(byte[] bytes, string filename)?> RenderPdfAsync(Guid orderId, CancellationToken ct = default)
    {
        var receipt = await GetAsync(orderId, ct);
        if (receipt == null) return null;
        ReceiptPdfRenderer.EnsureLicense();
        var bytes = ReceiptPdfRenderer.Render(receipt);
        return (bytes, $"Receipt-{receipt.OrderNumber}.pdf");
    }

    public async Task<FranchiseBifurcation?> GetBifurcationAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return null;

        if (order.Source != OrderSource.Franchisee || order.FranchiseId == null)
            return new FranchiseBifurcation { IsFranchiseOrder = false, OrderTotal = order.TotalAmount };

        var f = await _db.Franchises.AsNoTracking().FirstOrDefaultAsync(x => x.Id == order.FranchiseId, ct);
        var settings = await _db.FranchiseSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new FranchiseSettings();

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var rules = await _db.FranchiseCommissions.AsNoTracking()
            .Where(c => c.FranchiseId == order.FranchiseId && productIds.Contains(c.ProductId))
            .ToDictionaryAsync(c => c.ProductId, ct);

        var isGstRegistered = !string.IsNullOrWhiteSpace(f?.Gstin);

        var lines = new List<FranchiseBifurcationLine>();
        var totalSplit = FranchiseCommissionMath.CommissionSplit.Zero;
        foreach (var it in order.Items)
        {
            products.TryGetValue(it.ProductId, out var product);
            var lineSplit = FranchiseCommissionMath.CommissionSplit.Zero;
            string shareLabel = "—"; bool special = false;
            if (product != null)
            {
                rules.TryGetValue(it.ProductId, out var rule);
                var calc = _shares.Calculate(product, rule, settings, isGstRegistered);
                if (calc.HasShare)
                {
                    lineSplit = new FranchiseCommissionMath.CommissionSplit(
                        calc.CommissionAmount, calc.GstOnCommission, calc.CalculatedFranchiseAmount)
                        .Times(it.Quantity);
                    shareLabel = calc.ShareType == CommissionType.Percent
                        ? calc.ShareValue.ToString("0.##") + "%"
                        : "₹" + calc.ShareValue.ToString("N0") + "/unit";
                    special = calc.IsSpecialPriceActive;
                }
            }
            // Cap line share at the line value, keeping commission and its GST in proportion.
            lineSplit = FranchiseCommissionMath.CapTo(lineSplit, Math.Min(lineSplit.TotalPayout, it.LineTotal));
            totalSplit = totalSplit.Add(lineSplit);
            lines.Add(new FranchiseBifurcationLine
            {
                ProductTitle = it.ProductTitle,
                Quantity = it.Quantity,
                LineAmount = it.LineTotal,
                ShareLabel = shareLabel,
                SpecialPriceApplied = special,
                FranchiseShare = lineSplit.TotalPayout,
                CompanyShare = Math.Round(it.LineTotal - lineSplit.TotalPayout, 2),
                CommissionAmount = lineSplit.Commission,
                GstOnCommission = lineSplit.GstOnCommission
            });
        }

        // Prefer the order's stored share (authoritative) for the order-level totals.
        var franchiseShare = order.FranchiseShareAmount > 0 ? order.FranchiseShareAmount : Math.Round(totalSplit.TotalPayout, 2);

        // The stored split wins when the order carries one. Orders placed before the split existed
        // have both columns at zero — fall back to the recomputed figures, and flag that the
        // breakdown is derived rather than what was actually billed.
        var storedSplit = order.FranchiseCommissionBase > 0 || order.FranchiseCommissionGst > 0;
        var commission = storedSplit ? order.FranchiseCommissionBase : totalSplit.Commission;
        var commissionGst = storedSplit ? order.FranchiseCommissionGst : totalSplit.GstOnCommission;

        return new FranchiseBifurcation
        {
            IsFranchiseOrder = true,
            FranchiseName = f?.BusinessName ?? f?.Name,
            FranchiseCode = f?.Code,
            Lines = lines,
            TotalFranchiseShare = franchiseShare,
            TotalCompanyShare = Math.Round(order.TotalAmount - franchiseShare, 2),
            NetPayableByFranchisee = order.FranchiseNetPayable > 0 ? order.FranchiseNetPayable : order.TotalAmount - franchiseShare,
            OrderTotal = order.TotalAmount,
            FranchiseeIsGstRegistered = isGstRegistered,
            TotalCommissionAmount = commission,
            TotalGstOnCommission = commissionGst,
            HasCommissionSplit = storedSplit || commission > 0
        };
    }

    private async Task<CompanyProfile> ReadCompanyAsync() => new()
    {
        Name = (await _settings.GetStringAsync("company.name")) ?? CompanyDefaultName,
        Gstin = await _settings.GetStringAsync("company.gstin"),
        Address = await _settings.GetStringAsync("company.address"),
        Phone = await _settings.GetStringAsync("company.phone"),
        Email = await _settings.GetStringAsync("company.email"),
        Website = await _settings.GetStringAsync("company.website"),
    };
}
