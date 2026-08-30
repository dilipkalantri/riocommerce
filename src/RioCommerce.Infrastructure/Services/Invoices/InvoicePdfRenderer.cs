using System.Globalization;
using RioCommerce.Core.DTOs.Invoices;
using RioCommerce.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RioCommerce.Infrastructure.Services.Invoices;

/// <summary>
/// QuestPDF-based renderer for the invoice. Produces an A4 PDF with the standard layout:
///   1. Header band — company name, logo, "TAX INVOICE" title, invoice meta block
///   2. Bill-To + Ship-To (collapsed to a single Bill-To for our case)
///   3. Line-items table with HSN, Qty, Rate, Discount, GST, Total
///   4. Totals block (right-aligned), tax breakdown by CGST/SGST/IGST
///   5. Payment block (mode, ref, status)
///   6. Footer — bank details (if configured), declaration, signature placeholder
/// </summary>
public static class InvoicePdfRenderer
{
    /// <summary>QuestPDF requires a license type to be set once per process. Free for revenue
    /// &lt; $1M USD — see <see cref="https://www.questpdf.com/license-community"/>.</summary>
    public static void EnsureLicense()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Render(InvoiceDetail inv)
    {
        EnsureLicense();
        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Margin(36);
                p.Size(PageSizes.A4);
                p.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(10).FontColor("#1F2937"));

                p.Header().Element(e => Header(e, inv));
                p.Content().PaddingTop(14).Element(e => Body(e, inv));
                p.Footer().PaddingTop(8).Element(e => Footer(e, inv));
            });
        });
        return doc.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Header
    // ─────────────────────────────────────────────────────────────────────────
    private static void Header(IContainer container, InvoiceDetail inv)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                // Left: company identity
                row.RelativeItem(2).Column(cc =>
                {
                    cc.Item().Text(inv.CompanyName).FontSize(20).Bold().FontColor("#0F172A");
                    if (!string.IsNullOrEmpty(inv.CompanyAddress))
                        cc.Item().PaddingTop(2).Text(inv.CompanyAddress).FontSize(9).FontColor("#475569");
                    var meta = new List<string>();
                    if (!string.IsNullOrEmpty(inv.CompanyPhone))  meta.Add($"☎ {inv.CompanyPhone}");
                    if (!string.IsNullOrEmpty(inv.CompanyEmail))  meta.Add($"✉ {inv.CompanyEmail}");
                    if (meta.Count > 0)
                        cc.Item().PaddingTop(2).Text(string.Join("  ·  ", meta)).FontSize(9).FontColor("#475569");
                    if (!string.IsNullOrEmpty(inv.CompanyGstin))
                        cc.Item().PaddingTop(2).Text($"GSTIN: {inv.CompanyGstin}").FontSize(9).Bold();
                });

                // Right: invoice meta
                row.RelativeItem().Column(cc =>
                {
                    var title = inv.Status == InvoiceStatus.Cancelled ? "CANCELLED INVOICE" : "TAX INVOICE";
                    var titleColor = inv.Status == InvoiceStatus.Cancelled ? "#991B1B" : "#0F172A";
                    cc.Item().AlignRight().Text(title).FontSize(16).Bold().FontColor(titleColor);
                    cc.Item().PaddingTop(8).Border(0.5f).BorderColor("#CBD5E1").Padding(8).Column(meta =>
                    {
                        meta.Item().Row(r => {
                            r.RelativeItem().Text("Invoice #").FontSize(9).FontColor("#64748B");
                            r.RelativeItem().AlignRight().Text(inv.InvoiceNumber).FontSize(10).Bold();
                        });
                        meta.Item().PaddingTop(4).Row(r => {
                            r.RelativeItem().Text("Date").FontSize(9).FontColor("#64748B");
                            r.RelativeItem().AlignRight().Text(inv.InvoiceDate.ToLocalTime().ToString("dd MMM yyyy")).FontSize(10);
                        });
                        meta.Item().PaddingTop(4).Row(r => {
                            r.RelativeItem().Text("Order #").FontSize(9).FontColor("#64748B");
                            r.RelativeItem().AlignRight().Text(inv.OrderNumber).FontSize(10);
                        });
                    });
                });
            });

            col.Item().PaddingTop(12).LineHorizontal(1).LineColor("#0F172A");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Body
    // ─────────────────────────────────────────────────────────────────────────
    private static void Body(IContainer container, InvoiceDetail inv)
    {
        container.Column(col =>
        {
            // ── Bill To + (collapsed Ship To) ──
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(cc =>
                {
                    cc.Item().Text("Bill To").FontSize(9).Bold().FontColor("#64748B");
                    cc.Item().PaddingTop(4).Text(inv.CustomerName).FontSize(11).Bold();
                    if (!string.IsNullOrEmpty(inv.BillingAddress))
                        cc.Item().PaddingTop(2).Text(inv.BillingAddress).FontSize(9);
                    var loc = new List<string?> { inv.BillingCity, inv.BillingState, inv.BillingPincode }
                        .Where(s => !string.IsNullOrEmpty(s)).ToList();
                    if (loc.Count > 0) cc.Item().Text(string.Join(", ", loc)).FontSize(9);
                    if (!string.IsNullOrEmpty(inv.CustomerEmail))  cc.Item().PaddingTop(2).Text($"✉ {inv.CustomerEmail}").FontSize(9);
                    if (!string.IsNullOrEmpty(inv.CustomerPhone))  cc.Item().Text($"☎ {inv.CustomerPhone}").FontSize(9);
                    if (!string.IsNullOrEmpty(inv.CustomerGstin))
                        cc.Item().PaddingTop(4).Text($"GSTIN: {inv.CustomerGstin}").FontSize(9).Bold();
                    cc.Item().PaddingTop(2).Text($"Classification: {inv.GstClassification}").FontSize(9).FontColor("#64748B");
                    if (inv.ReverseCharge)
                        cc.Item().PaddingTop(2).Text("Reverse Charge: Applicable (RCM)").FontSize(9).Bold().FontColor("#92400E");
                });

                row.RelativeItem().Column(cc =>
                {
                    cc.Item().Text("Payment").FontSize(9).Bold().FontColor("#64748B");
                    // "Gateway" is the channel the customer was sent to (Razorpay / Easebuzz / Cash);
                    // "Mode" is what they actually paid with there (UPI / Credit Card / …), and only
                    // exists for gateway payments — omitted rather than shown blank.
                    cc.Item().PaddingTop(4).Text($"Gateway: {inv.PaymentMode}").FontSize(10);
                    if (!string.IsNullOrEmpty(inv.GatewayPaymentMode))
                        cc.Item().Text($"Mode: {inv.GatewayPaymentMode}").FontSize(10);
                    if (!string.IsNullOrEmpty(inv.PaymentReference))
                        cc.Item().Text($"Ref: {inv.PaymentReference}").FontSize(9);
                    if (inv.PaidAt.HasValue)
                        cc.Item().Text($"Paid: {inv.PaidAt.Value.ToLocalTime():dd MMM yyyy, HH:mm}").FontSize(9);
                    cc.Item().PaddingTop(4).Text("Status: PAID").FontSize(10).Bold().FontColor("#065F46");
                });
            });

            // ── Line items table ──
            col.Item().PaddingTop(18).Element(e => LineItemsTable(e, inv));

            // ── Totals ──
            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem(2).Column(c =>
                {
                    if (!string.IsNullOrEmpty(inv.Notes))
                    {
                        c.Item().Text("Notes").FontSize(9).Bold().FontColor("#64748B");
                        c.Item().PaddingTop(2).Text(inv.Notes).FontSize(9);
                    }
                });
                row.RelativeItem().Column(cc => Totals(cc, inv));
            });

            if (inv.ReverseCharge)
                col.Item().PaddingTop(6).Text("GST payable under Reverse Charge Mechanism (RCM).").FontSize(9).Italic().FontColor("#92400E");

            // Franchise invoices: full per-product franchisee/company bifurcation.
            if (inv.Bifurcation is { IsFranchiseOrder: true, Lines.Count: > 0 })
                col.Item().PaddingTop(16).Element(e => Bifurcation(e, inv.Bifurcation!, inv.Currency));
        });
    }

    private static void Bifurcation(IContainer container, RioCommerce.Core.DTOs.Orders.FranchiseBifurcation b, string currency)
    {
        container.Column(col =>
        {
            col.Item().Text("Franchisee Financial Bifurcation").FontSize(11).Bold().FontColor("#1F4E79");
            col.Item().PaddingTop(2).Text($"{b.FranchiseName}{(string.IsNullOrEmpty(b.FranchiseCode) ? "" : $" ({b.FranchiseCode})")}").FontSize(9).FontColor("#475569");
            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4); c.ConstantColumn(34); c.ConstantColumn(70);
                    c.ConstantColumn(54); c.ConstantColumn(74); c.ConstantColumn(74);
                });
                static IContainer Head(IContainer c) =>
                    c.Background("#0F172A").DefaultTextStyle(s => s.FontColor("#fff").Bold().FontSize(8.5f)).PaddingVertical(5).PaddingHorizontal(4);
                static IContainer Cell(IContainer c) =>
                    c.BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingVertical(4).PaddingHorizontal(4);
                t.Header(h =>
                {
                    h.Cell().Element(Head).Text("Product");
                    h.Cell().Element(Head).AlignRight().Text("Qty");
                    h.Cell().Element(Head).AlignRight().Text("Line ₹");
                    h.Cell().Element(Head).AlignRight().Text("Share");
                    h.Cell().Element(Head).AlignRight().Text("Franchisee");
                    h.Cell().Element(Head).AlignRight().Text("Company");
                });
                foreach (var l in b.Lines)
                {
                    t.Cell().Element(Cell).Column(cc =>
                    {
                        cc.Item().Text(l.ProductTitle).FontSize(9);
                        if (l.SpecialPriceApplied) cc.Item().Text("special price applied").FontSize(7.5f).Italic().FontColor("#92400E");
                    });
                    t.Cell().Element(Cell).AlignRight().Text(l.Quantity.ToString()).FontSize(9);
                    t.Cell().Element(Cell).AlignRight().Text(Money(l.LineAmount, currency)).FontSize(9);
                    t.Cell().Element(Cell).AlignRight().Text(l.ShareLabel).FontSize(9);
                    t.Cell().Element(Cell).AlignRight().Text(Money(l.FranchiseShare, currency)).FontSize(9).FontColor("#065F46");
                    t.Cell().Element(Cell).AlignRight().Text(Money(l.CompanyShare, currency)).FontSize(9);
                }
            });
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(240).Column(cc =>
                {
                    cc.Item().Element(e => TotalRow(e, "Order Total", b.OrderTotal, currency));
                    // Break the share into commission and the GST on it, so the franchisee can see
                    // what their own invoice to us must state and what we reclaim as input credit.
                    // Orders placed before the split was recorded show the single figure as before.
                    if (b.HasCommissionSplit && b.TotalGstOnCommission > 0)
                    {
                        cc.Item().Element(e => TotalRow(e, "Franchisee Commission", -b.TotalCommissionAmount, currency));
                        cc.Item().Element(e => TotalRow(e, "GST on Commission", -b.TotalGstOnCommission, currency));
                        cc.Item().Element(e => TotalRow(e, "Total Franchisee Share", -b.TotalFranchiseShare, currency));
                    }
                    else
                    {
                        cc.Item().Element(e => TotalRow(e, "Franchisee Share (commission)", -b.TotalFranchiseShare, currency));
                    }
                    cc.Item().Element(e => TotalRow(e, "Company Share", b.TotalCompanyShare, currency));
                    cc.Item().PaddingTop(3).LineHorizontal(0.5f).LineColor("#94A3B8");
                    cc.Item().PaddingTop(3).Row(rr =>
                    {
                        rr.RelativeItem().Text("Net Payable by Franchisee").FontSize(10).Bold();
                        rr.ConstantItem(100).AlignRight().Text(Money(b.NetPayableByFranchisee, currency)).FontSize(11).Bold().FontColor("#065F46");
                    });
                });
            });

            // Which document the franchisee owes us for their commission depends on whether they
            // are registered — a tax invoice supports our input credit, a bill of supply does not.
            if (b.HasCommissionSplit)
                col.Item().PaddingTop(6).Text(b.FranchiseeIsGstRegistered
                        ? "Franchisee is GST-registered: commission is a taxable supply. Franchisee to raise a tax invoice for the commission plus GST shown above."
                        : "Franchisee is not GST-registered: no GST on commission. Franchisee to raise a bill of supply; no input tax credit arises.")
                    .FontSize(7.5f).Italic().FontColor("#64748B");
        });
    }

    private static void LineItemsTable(IContainer container, InvoiceDetail inv)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(24);          // #
                c.RelativeColumn(5);           // Description
                c.ConstantColumn(50);          // HSN
                c.ConstantColumn(38);          // Qty
                c.ConstantColumn(60);          // Rate
                c.ConstantColumn(58);          // Discount
                c.ConstantColumn(48);          // GST%
                c.ConstantColumn(58);          // GST amt
                c.ConstantColumn(70);          // Line total
            });

            t.Header(h =>
            {
                static IContainer Cell(IContainer c) =>
                    c.Background("#0F172A").DefaultTextStyle(s => s.FontColor("#fff").Bold().FontSize(8.5f)).PaddingVertical(6).PaddingHorizontal(4);
                h.Cell().Element(Cell).AlignCenter().Text("#");
                h.Cell().Element(Cell).Text("Description");
                h.Cell().Element(Cell).AlignCenter().Text("HSN");
                h.Cell().Element(Cell).AlignRight().Text("Qty");
                h.Cell().Element(Cell).AlignRight().Text("Rate");
                h.Cell().Element(Cell).AlignRight().Text("Discount");
                h.Cell().Element(Cell).AlignRight().Text("GST %");
                h.Cell().Element(Cell).AlignRight().Text("GST Amt");
                h.Cell().Element(Cell).AlignRight().Text("Total");
            });

            foreach (var l in inv.LineItems.OrderBy(x => x.LineNumber))
            {
                static IContainer Body(IContainer c) =>
                    c.BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingVertical(5).PaddingHorizontal(4);
                t.Cell().Element(Body).AlignCenter().Text(l.LineNumber.ToString()).FontSize(9);
                t.Cell().Element(Body).Column(cc =>
                {
                    cc.Item().Text(l.Description).FontSize(9).Medium();
                    if (!string.IsNullOrEmpty(l.ModeName))
                        cc.Item().Text(l.ModeName).FontSize(8).FontColor("#64748B");
                });
                t.Cell().Element(Body).AlignCenter().Text(l.HsnCode ?? "—").FontSize(8.5f).FontColor("#64748B");
                t.Cell().Element(Body).AlignRight().Text(l.Quantity.ToString()).FontSize(9);
                t.Cell().Element(Body).AlignRight().Text(Money(l.UnitPrice, inv.Currency)).FontSize(9);
                t.Cell().Element(Body).AlignRight().Text(Money(l.Discount, inv.Currency)).FontSize(9);
                t.Cell().Element(Body).AlignRight().Text(l.GstRate.HasValue ? $"{l.GstRate:0.##}%" : "—").FontSize(9);
                t.Cell().Element(Body).AlignRight().Text(Money(l.GstAmount, inv.Currency)).FontSize(9);
                t.Cell().Element(Body).AlignRight().Text(Money(l.LineTotal, inv.Currency)).FontSize(9.5f).Bold();
            }
        });
    }

    /// <summary>
    /// The totals block. Every figure is READ from the invoice, never re-derived.
    ///
    /// <para>It used to recompute the franchise breakup from <c>Subtotal − share</c> at a single
    /// headline rate. That was wrong twice over: it dropped <c>DiscountAmount</c> and
    /// <c>ShippingCharges</c>, so a franchise order carrying either printed a Grand Total that
    /// disagreed with the amount actually charged; and it re-split the tax at the max line rate,
    /// which misstates a mixed-rate invoice. <c>InvoiceService</c> now stores the tax on the
    /// invoiced basis (see migration 0033), so the stored row is the authority and this method's
    /// only job is to lay it out.</para>
    ///
    /// <para>The block is a single sequence for every invoice type — franchise or not — because both
    /// obey the same two identities:</para>
    /// <code>
    /// Subtotal − Discount + Shipping − FranchiseeShare == TotalAmount
    /// TaxableValue + CGST + SGST + IGST                == TotalAmount
    /// </code>
    /// <para>Prices are GST-inclusive, which is why the tax lines sit UNDER a stated taxable value
    /// rather than being added to the subtotal. The previous non-franchise block listed CGST and SGST
    /// after the subtotal with no taxable line, so a ₹118 invoice printed "Subtotal 118, CGST 9,
    /// SGST 9, Grand Total 118" — three numbers that cannot be reconciled by a reader.</para>
    /// </summary>
    private static void Totals(QuestPDF.Fluent.ColumnDescriptor cc, InvoiceDetail inv)
    {
        cc.Item().LineHorizontal(1).LineColor("#0F172A");

        var tax = inv.CgstAmount + inv.SgstAmount + inv.IgstAmount;
        var taxable = Math.Round(inv.TotalAmount - tax, 2);

        // The "@ 9%" suffix is only truthful when every line carries the same rate. GstRatePct is the
        // MAX rate across lines, so on a mixed 5%/18% invoice it would label the whole tax at 18%.
        // Rate omitted in that case; the per-line rate column still carries the detail.
        var rates = inv.LineItems.Select(li => li.GstRate ?? 0m).Where(r => r > 0m).Distinct().ToList();
        var singleRate = rates.Count == 1 ? rates[0] : (decimal?)null;
        var half = singleRate is { } sr ? $" @ {(sr / 2m).ToString("0.##")}%" : "";
        var full = singleRate is { } fr ? $" @ {fr.ToString("0.##")}%" : "";

        cc.Item().PaddingTop(6).Element(e => TotalRow(e, "Subtotal", inv.Subtotal, inv.Currency));

        if (inv.DiscountAmount > 0)
            cc.Item().Element(e => TotalRow(e, "Discount", -inv.DiscountAmount, inv.Currency));

        if (inv.ShippingCharges > 0)
            cc.Item().Element(e => TotalRow(e, "Shipping", inv.ShippingCharges, inv.Currency));

        // Only the share that was actually netted off is shown as a deduction. A franchise order whose
        // commission is settled separately (share not deducted from TotalAmount) must not print a
        // discount line — the total would no longer follow from the lines above it. Such an order's
        // bifurcation is still rendered in its own section; it just isn't part of this arithmetic.
        if (inv.FranchiseShareAmount > 0)
        {
            cc.Item().Element(e => TotalRow(e, "Franchisee Discount", -inv.FranchiseShareAmount, inv.Currency));
            cc.Item().Element(e => TotalRow(e, "Net Amount", inv.TotalAmount, inv.Currency));
        }

        cc.Item().Element(e => TotalRow(e, "Taxable value", taxable, inv.Currency));

        if (inv.CgstAmount > 0)
            cc.Item().Element(e => TotalRow(e, $"CGST{half}", inv.CgstAmount, inv.Currency));
        if (inv.SgstAmount > 0)
            cc.Item().Element(e => TotalRow(e, $"SGST{half}", inv.SgstAmount, inv.Currency));
        if (inv.IgstAmount > 0)
            cc.Item().Element(e => TotalRow(e, $"IGST{full}", inv.IgstAmount, inv.Currency));

        if (inv.ReverseCharge)
            cc.Item().Element(e => TotalRow(e, "Tax payable by recipient (reverse charge)", 0m, inv.Currency));

        cc.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor("#94A3B8");
        cc.Item().PaddingTop(4).Background("#FEF3C7").Padding(8).Row(r =>
        {
            r.RelativeItem().Text("Grand Total").FontSize(11).Bold();
            r.ConstantItem(110).AlignRight().Text(Money(inv.TotalAmount, inv.Currency)).FontSize(13).Bold().FontColor("#92400E");
        });
    }

    private static void TotalRow(IContainer container, string label, decimal amount, string currency)
    {
        container.PaddingVertical(2).Row(r =>
        {
            r.RelativeItem().Text(label).FontSize(9.5f).FontColor("#475569");
            r.ConstantItem(110).AlignRight().Text(Money(amount, currency)).FontSize(9.5f);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Footer
    // ─────────────────────────────────────────────────────────────────────────
    private static void Footer(IContainer container, InvoiceDetail inv)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor("#CBD5E1");
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text("Thank you for your business.\nThis is a system-generated invoice; no signature required.")
                    .FontSize(8.5f).FontColor("#64748B");
                row.ConstantItem(160).AlignRight().Column(c =>
                {
                    c.Item().Text("For " + inv.CompanyName).FontSize(8.5f).FontColor("#64748B");
                    c.Item().PaddingTop(22).LineHorizontal(0.5f).LineColor("#94A3B8");
                    c.Item().PaddingTop(2).AlignRight().Text("Authorised Signatory").FontSize(8.5f).FontColor("#64748B");
                });
            });
            col.Item().PaddingTop(8).AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor("#94A3B8"));
                t.Span("Invoice ").SemiBold();
                t.Span(inv.InvoiceNumber);
                t.Span("  ·  Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    }

    private static string Money(decimal v, string currency)
    {
        var symbol = currency == "INR" ? "₹" : (currency == "USD" ? "$" : currency + " ");
        return symbol + v.ToString("N2", CultureInfo.InvariantCulture);
    }
}
