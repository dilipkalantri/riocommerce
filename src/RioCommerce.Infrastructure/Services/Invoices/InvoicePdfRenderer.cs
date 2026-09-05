using System.Globalization;
using RioCommerce.Core.DTOs.Invoices;
using RioCommerce.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RioCommerce.Infrastructure.Services.Invoices;

/// <summary>
/// QuestPDF renderer for the statutory tax invoice. A4 portrait, ruled-box layout:
///
///   1. "Tax Invoice" title band
///   2. Seller block (left) beside Invoice No. / Dated / Reference No. &amp; Date / Other References (right)
///   3. Buyer (Bill to) block
///   4. Item table — S.No | Particulars | HSN/SAC | Quantity | Rate | Amount (INR)
///   5. CGST / SGST / IGST and Total, aligned under the Amount column
///   6. Amount Chargeable (in words) + E. &amp; O.E.
///   7. Tax Amount (in words) beside Company's Bank Details
///   8. Company's PAN beside the Prepared / Verified / Authorised Signatory strip
///   9. "This is a Computer Generated Invoice" footer
///
/// <para>EVERY MONETARY FIGURE IS READ FROM THE PERSISTED INVOICE. This class does no pricing and
/// no tax arithmetic: CGST/SGST/IGST are the amounts InvoiceService stored on the invoiced basis
/// (migration 0033), and the line figures are the OrderItem snapshot taken when the order was
/// placed. The only arithmetic here is presentational — splitting a GST-INCLUSIVE line into its
/// taxable part for the Rate/Amount columns (line total minus the line's own stored GST), which is
/// the same identity the totals block already used. Consequently a later product price change
/// cannot alter a historical invoice, and no client-supplied amount can reach this renderer.</para>
/// </summary>
public static class InvoicePdfRenderer
{
    private const string Ink = "#000000";
    private const string Rule = "#000000";
    private const float RuleWidth = 0.6f;

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
                p.Margin(24);
                p.Size(PageSizes.A4);
                p.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(9).FontColor(Ink));

                p.Content().Element(e => Body(e, inv));
                p.Footer().PaddingTop(4).Element(PageFooter);
            });
        });
        return doc.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Body — one outer ruled box, as on the reference document
    // ─────────────────────────────────────────────────────────────────────────
    private static void Body(IContainer container, InvoiceDetail inv)
    {
        container.Border(RuleWidth).BorderColor(Rule).Column(col =>
        {
            col.Item().Element(e => TitleBand(e, inv));
            col.Item().Element(e => SellerAndMeta(e, inv));
            col.Item().Element(e => BuyerBlock(e, inv));
            col.Item().Element(e => ItemsTable(e, inv));

            // NO vertical filler here. The reference document rules its item area all the way down
            // the page, and an ExtendVertical() spacer reproduces that — but it claims the space
            // BEFORE the blocks below it are measured, so the totals, the amount-in-words, the bank
            // details and the signatures were all pushed onto a second page. A correct one-page
            // invoice beats a cosmetic full-height box, so the block simply ends with its content.
            col.Item().Element(e => SummaryTable(e, inv));
            col.Item().Element(e => AmountWords(e, inv));
            col.Item().Element(e => TaxWordsAndBank(e, inv));
            col.Item().Element(e => PanAndSignatories(e, inv));
        });

        // Franchise invoices keep their per-product bifurcation. It is supplementary to the
        // statutory document, so it follows the ruled box rather than sitting inside it.
        if (inv.Bifurcation is { IsFranchiseOrder: true, Lines.Count: > 0 })
            container.Column(c => c.Item().PaddingTop(14).Element(e => Bifurcation(e, inv.Bifurcation!, inv.Currency)));
    }

    private static void TitleBand(IContainer container, InvoiceDetail inv)
    {
        var cancelled = inv.Status == InvoiceStatus.Cancelled;
        container.BorderBottom(RuleWidth).BorderColor(Rule).PaddingVertical(4).AlignCenter()
            .Text(cancelled ? "Tax Invoice — CANCELLED" : "Tax Invoice")
            .FontSize(12).SemiBold().FontColor(cancelled ? "#991B1B" : Ink);
    }

    /// <summary>Seller identity on the left; invoice metadata in a 2×2 ruled grid on the right.</summary>
    private static void SellerAndMeta(IContainer container, InvoiceDetail inv)
    {
        container.BorderBottom(RuleWidth).BorderColor(Rule).Row(row =>
        {
            // ── Seller ──
            row.RelativeItem(5).BorderRight(RuleWidth).BorderColor(Rule).Padding(6).Column(cc =>
            {
                cc.Item().Text(inv.CompanyName).FontSize(10.5f).Bold();
                if (!string.IsNullOrWhiteSpace(inv.CompanyAddress))
                    cc.Item().PaddingTop(1).Text(inv.CompanyAddress).FontSize(9);
                if (!string.IsNullOrWhiteSpace(inv.CompanyGstin))
                    cc.Item().PaddingTop(1).Text($"GST No. - {inv.CompanyGstin}").FontSize(9);

                var contact = new List<string>();
                if (!string.IsNullOrWhiteSpace(inv.CompanyPhone)) contact.Add($"Mob - {inv.CompanyPhone}");
                if (!string.IsNullOrWhiteSpace(inv.CompanyEmail)) contact.Add($"Email - {inv.CompanyEmail}");
                if (contact.Count > 0)
                    cc.Item().PaddingTop(1).Text(string.Join(", ", contact)).FontSize(9);
            });

            // ── Metadata grid ──
            row.RelativeItem(4).Column(meta =>
            {
                meta.Item().BorderBottom(RuleWidth).BorderColor(Rule).Row(r =>
                {
                    r.RelativeItem().BorderRight(RuleWidth).BorderColor(Rule).Padding(4)
                        .Element(e => Field(e, "Invoice No.", inv.InvoiceNumber, bold: true));
                    r.RelativeItem().Padding(4)
                        .Element(e => Field(e, "Dated", inv.InvoiceDate.ToLocalTime().ToString("dd/MM/yyyy"), bold: true));
                });
                meta.Item().Row(r =>
                {
                    // Reference No. & Date = the order this invoice bills, which is the document
                    // a reader would reconcile against. Other References = the gateway payment ref.
                    r.RelativeItem().BorderRight(RuleWidth).BorderColor(Rule).Padding(4).MinHeight(34)
                        .Element(e => Field(e, "Reference No. & Date",
                            string.IsNullOrWhiteSpace(inv.OrderNumber) ? "—" : inv.OrderNumber));
                    r.RelativeItem().Padding(4).MinHeight(34)
                        .Element(e => Field(e, "Other References",
                            string.IsNullOrWhiteSpace(inv.PaymentReference) ? "—" : inv.PaymentReference!));
                });
            });
        });
    }

    private static void Field(IContainer container, string label, string value, bool bold = false)
    {
        container.Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor("#374151");
            var span = c.Item().PaddingTop(1).Text(value).FontSize(9.5f);
            if (bold) span.SemiBold();
        });
    }

    /// <summary>Buyer block. The name/address/phone are the ORDER's billing snapshot, so a student
    /// who later edits their profile does not retrospectively change an issued invoice.</summary>
    private static void BuyerBlock(IContainer container, InvoiceDetail inv)
    {
        container.BorderBottom(RuleWidth).BorderColor(Rule).Padding(6).Column(cc =>
        {
            cc.Item().Text("Buyer (Bill to)").FontSize(8.5f).SemiBold().FontColor("#374151");
            cc.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(inv.CustomerName) ? "—" : inv.CustomerName)
                .FontSize(10).Bold();

            var addr = new List<string?> { inv.BillingAddress, inv.BillingCity, inv.BillingState, inv.BillingPincode }
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (addr.Count > 0)
                cc.Item().Text(string.Join(", ", addr)).FontSize(9);

            if (!string.IsNullOrWhiteSpace(inv.CustomerPhone))
                cc.Item().Text(inv.CustomerPhone).FontSize(9);
            if (!string.IsNullOrWhiteSpace(inv.CustomerEmail))
                cc.Item().Text(inv.CustomerEmail).FontSize(9);
            if (!string.IsNullOrWhiteSpace(inv.CustomerGstin))
                cc.Item().PaddingTop(1).Text($"GSTIN / UIN: {inv.CustomerGstin}").FontSize(9).SemiBold();
            if (inv.ReverseCharge)
                cc.Item().PaddingTop(1).Text("Reverse Charge: Applicable (RCM)").FontSize(8.5f).SemiBold().FontColor("#92400E");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Items + tax + total, in one table so every figure aligns under "Amount (INR)"
    // ─────────────────────────────────────────────────────────────────────────
    // Column widths are shared by the item table, the spacer rules and the summary table so the
    // three line up as a single ruled block. Defined once — changing a width here moves all three.
    private const float ColSNo = 30, ColHsn = 58, ColQty = 52, ColRate = 62, ColAmount = 78;

    private static void DefineColumns(TableColumnsDefinitionDescriptor c)
    {
        c.ConstantColumn(ColSNo);      // S. No.
        c.RelativeColumn();            // Particulars
        c.ConstantColumn(ColHsn);      // HSN/SAC
        c.ConstantColumn(ColQty);      // Quantity
        c.ConstantColumn(ColRate);     // Rate
        c.ConstantColumn(ColAmount);   // Amount (INR)
    }

    private static void ItemsTable(IContainer container, InvoiceDetail inv)
    {
        var lines = inv.LineItems.OrderBy(x => x.LineNumber).ToList();

        container.Table(t =>
        {
            t.ColumnsDefinition(DefineColumns);

            // Repeats automatically on every page after the first (requirement: multi-page).
            t.Header(h =>
            {
                h.Cell().Element(HeadCell).AlignCenter().Text("S. No.");
                h.Cell().Element(HeadCell).Text("Particulars");
                h.Cell().Element(HeadCell).AlignCenter().Text("HSN/SAC");
                h.Cell().Element(HeadCell).AlignCenter().Text("Quantity");
                h.Cell().Element(HeadCell).AlignRight().Text("Rate");
                h.Cell().Element(HeadCellLast).AlignRight().Text("Amount (INR)");
            });

            foreach (var l in lines)
            {
                // GST-inclusive line split into its taxable part, using the line's OWN stored GST
                // amount — so a mixed-rate invoice stays correct and nothing is re-derived from a
                // headline rate. This is the only arithmetic in the renderer and it is display-only.
                var taxable = l.LineTotal - l.GstAmount;
                var unitTaxable = l.Quantity > 0 ? taxable / l.Quantity : taxable;

                t.Cell().Element(BodyCell).AlignCenter().Text(l.LineNumber.ToString()).FontSize(9);
                t.Cell().Element(BodyCell).Column(cc =>
                {
                    // Long product names wrap here rather than overflowing the column.
                    cc.Item().Text(l.Description).FontSize(9);
                    if (!string.IsNullOrWhiteSpace(l.ModeName))
                        cc.Item().Text(l.ModeName).FontSize(8).FontColor("#4B5563");
                });
                t.Cell().Element(BodyCell).AlignCenter().Text(l.HsnCode ?? "—").FontSize(9);
                t.Cell().Element(BodyCell).AlignCenter().Text(l.Quantity.ToString()).FontSize(9);
                t.Cell().Element(BodyCell).AlignRight().Text(Num(unitTaxable)).FontSize(9);
                t.Cell().Element(BodyCellLast).AlignRight().Text(Num(taxable)).FontSize(9);
            }
        });
    }

    private static void SummaryTable(IContainer container, InvoiceDetail inv)
    {
        var tax = inv.CgstAmount + inv.SgstAmount + inv.IgstAmount;

        // Rate labels are only truthful when a single rate applies across the invoice; on a mixed
        // invoice the "@ x%" suffix is dropped rather than mislabelling the whole tax at one rate.
        var rates = inv.LineItems.Select(l => l.GstRate ?? 0m).Where(r => r > 0m).Distinct().ToList();
        var single = rates.Count == 1 ? rates[0] : (decimal?)null;
        var half = single is { } s ? $" @ {(s / 2m).ToString("0.##", CultureInfo.InvariantCulture)}%" : "";
        var full = single is { } f ? $" @ {f.ToString("0.##", CultureInfo.InvariantCulture)}%" : "";

        container.Table(t =>
        {
            t.ColumnsDefinition(DefineColumns);

            // Invoice-level adjustments appear only when non-zero, so a plain invoice reads exactly
            // like the reference while a discounted / franchise one still reconciles line-by-line.
            if (inv.DiscountAmount > 0) SummaryRow(t, "Discount", -inv.DiscountAmount);
            if (inv.ShippingCharges > 0) SummaryRow(t, "Shipping", inv.ShippingCharges);
            if (inv.FranchiseShareAmount > 0) SummaryRow(t, "Franchisee Discount", -inv.FranchiseShareAmount);

            if (inv.CgstAmount > 0) SummaryRow(t, $"CGST{half}", inv.CgstAmount);
            if (inv.SgstAmount > 0) SummaryRow(t, $"SGST{half}", inv.SgstAmount);
            if (inv.IgstAmount > 0) SummaryRow(t, $"IGST{full}", inv.IgstAmount);
            if (inv.ReverseCharge && tax == 0)
                SummaryRow(t, "Tax payable by recipient (reverse charge)", 0m);

            // Total = the amount actually charged, straight off the invoice row. Never a sum of
            // anything computed here.
            t.Cell().ColumnSpan(4).Element(TotalLabelCell).AlignRight().Text("Total").FontSize(10).Bold();
            t.Cell().Element(TotalCell).Text("");
            t.Cell().Element(TotalCellLast).AlignRight().Text(Money(inv.TotalAmount, inv.Currency)).FontSize(10.5f).Bold();
        });

        static void SummaryRow(TableDescriptor t, string label, decimal amount)
        {
            t.Cell().ColumnSpan(4).Element(SummaryLabelCell).AlignRight().Text(label).FontSize(9);
            t.Cell().Element(SummaryCell).Text("");
            t.Cell().Element(SummaryCellLast).AlignRight().Text(Num(amount)).FontSize(9);
        }
    }

    // Cell chrome. The right-hand column omits its right border because the outer box supplies it.
    private static IContainer HeadCell(IContainer c) =>
        c.Border(RuleWidth).BorderTop(0).BorderLeft(0).BorderColor(Rule)
         .DefaultTextStyle(s => s.SemiBold().FontSize(9)).PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer HeadCellLast(IContainer c) =>
        c.BorderBottom(RuleWidth).BorderColor(Rule)
         .DefaultTextStyle(s => s.SemiBold().FontSize(9)).PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer BodyCell(IContainer c) =>
        c.BorderRight(RuleWidth).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer BodyCellLast(IContainer c) =>
        c.PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer SummaryLabelCell(IContainer c) =>
        c.BorderTop(RuleWidth).BorderRight(RuleWidth).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer SummaryCell(IContainer c) =>
        c.BorderTop(RuleWidth).BorderRight(RuleWidth).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer SummaryCellLast(IContainer c) =>
        c.BorderTop(RuleWidth).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer TotalLabelCell(IContainer c) =>
        c.Border(RuleWidth).BorderLeft(0).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer TotalCell(IContainer c) =>
        c.Border(RuleWidth).BorderLeft(0).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer TotalCellLast(IContainer c) =>
        c.BorderTop(RuleWidth).BorderBottom(RuleWidth).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(3);

    // ─────────────────────────────────────────────────────────────────────────
    // Words / bank / signatories
    // ─────────────────────────────────────────────────────────────────────────
    private static void AmountWords(IContainer container, InvoiceDetail inv)
    {
        container.BorderBottom(RuleWidth).BorderColor(Rule).Padding(6).Row(row =>
        {
            row.RelativeItem(3).Column(cc =>
            {
                cc.Item().Text("Amount Chargeable (in words)").FontSize(8).FontColor("#374151");
                cc.Item().PaddingTop(1).Text(AmountInWords.Rupees(inv.TotalAmount)).FontSize(9.5f).Bold();
            });
            row.RelativeItem().AlignRight().AlignBottom().Text("E. & O.E").FontSize(8.5f).Italic();
        });
    }

    private static void TaxWordsAndBank(IContainer container, InvoiceDetail inv)
    {
        var tax = inv.CgstAmount + inv.SgstAmount + inv.IgstAmount;
        var hasBank = !string.IsNullOrWhiteSpace(inv.CompanyBankName)
                   || !string.IsNullOrWhiteSpace(inv.CompanyBankAccountNumber)
                   || !string.IsNullOrWhiteSpace(inv.CompanyBankAccountName)
                   || !string.IsNullOrWhiteSpace(inv.CompanyBankIfsc);

        container.BorderBottom(RuleWidth).BorderColor(Rule).Row(row =>
        {
            row.RelativeItem().BorderRight(hasBank ? RuleWidth : 0).BorderColor(Rule).Padding(6).Column(cc =>
            {
                cc.Item().Text(t =>
                {
                    t.Span("Tax Amount (in words) : ").FontSize(8.5f).FontColor("#374151");
                    t.Span(AmountInWords.Rupees(tax)).FontSize(9).SemiBold();
                });
            });

            // The whole block is omitted when no bank details are configured, rather than
            // printing an empty labelled box that looks like missing data on a tax document.
            if (hasBank)
            {
                row.RelativeItem().Padding(6).Column(cc =>
                {
                    cc.Item().Text("Company's Bank Details").FontSize(9).Bold();
                    if (!string.IsNullOrWhiteSpace(inv.CompanyBankAccountName))
                        cc.Item().PaddingTop(1).Element(e => BankLine(e, "A/c Holder's Name", inv.CompanyBankAccountName!));
                    if (!string.IsNullOrWhiteSpace(inv.CompanyBankName))
                        cc.Item().Element(e => BankLine(e, "Bank Name", inv.CompanyBankName!));
                    if (!string.IsNullOrWhiteSpace(inv.CompanyBankAccountNumber))
                        cc.Item().Element(e => BankLine(e, "A/c No.", inv.CompanyBankAccountNumber!));

                    var branchIfsc = string.Join(" & ", new[] { inv.CompanyBankBranch, inv.CompanyBankIfsc }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (!string.IsNullOrWhiteSpace(branchIfsc))
                        cc.Item().Element(e => BankLine(e, "Branch & IFS Code", branchIfsc));
                });
            }
        });
    }

    private static void BankLine(IContainer container, string label, string value)
    {
        container.Row(r =>
        {
            r.ConstantItem(96).Text(label + " :").FontSize(8.5f).FontColor("#374151");
            r.RelativeItem().Text(value).FontSize(8.5f);
        });
    }

    private static void PanAndSignatories(IContainer container, InvoiceDetail inv)
    {
        container.Row(row =>
        {
            row.RelativeItem().BorderRight(RuleWidth).BorderColor(Rule).Padding(6).AlignMiddle().Column(cc =>
            {
                if (!string.IsNullOrWhiteSpace(inv.CompanyPan))
                    cc.Item().Text($"Company's PAN: {inv.CompanyPan}").FontSize(9).SemiBold();
            });

            row.RelativeItem().Padding(6).Column(cc =>
            {
                cc.Item().Text(inv.CompanyName).FontSize(9).SemiBold();
                cc.Item().PaddingTop(18).Row(r =>
                {
                    r.RelativeItem().Text("Prepared by").FontSize(8).FontColor("#374151");
                    r.RelativeItem().AlignCenter().Text("Verified by").FontSize(8).FontColor("#374151");
                    r.RelativeItem().AlignRight().Text("Authorised Signatory").FontSize(8).FontColor("#374151");
                });
            });
        });
    }

    private static void PageFooter(IContainer container)
    {
        container.AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(8).FontColor("#374151"));
            t.Span("This is a Computer Generated Invoice");
            t.Span("   ·   Page ");
            t.CurrentPageNumber();
            t.Span(" of ");
            t.TotalPages();
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Franchise bifurcation (unchanged behaviour, restyled to the ruled look)
    // ─────────────────────────────────────────────────────────────────────────
    private static void Bifurcation(IContainer container, RioCommerce.Core.DTOs.Orders.FranchiseBifurcation b, string currency)
    {
        container.Border(RuleWidth).BorderColor(Rule).Padding(6).Column(col =>
        {
            col.Item().Text("Franchisee Financial Bifurcation").FontSize(10).Bold();
            col.Item().PaddingTop(1).Text($"{b.FranchiseName}{(string.IsNullOrEmpty(b.FranchiseCode) ? "" : $" ({b.FranchiseCode})")}")
                .FontSize(8.5f).FontColor("#374151");

            col.Item().PaddingTop(5).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4); c.ConstantColumn(34); c.ConstantColumn(70);
                    c.ConstantColumn(54); c.ConstantColumn(74); c.ConstantColumn(74);
                });
                static IContainer Head(IContainer c) =>
                    c.BorderBottom(RuleWidth).BorderColor(Rule)
                     .DefaultTextStyle(s => s.SemiBold().FontSize(8.5f)).PaddingVertical(4).PaddingHorizontal(3);
                static IContainer Cell(IContainer c) =>
                    c.BorderBottom(0.3f).BorderColor("#9CA3AF").PaddingVertical(3).PaddingHorizontal(3);

                t.Header(h =>
                {
                    h.Cell().Element(Head).Text("Product");
                    h.Cell().Element(Head).AlignRight().Text("Qty");
                    h.Cell().Element(Head).AlignRight().Text("Line");
                    h.Cell().Element(Head).AlignRight().Text("Share");
                    h.Cell().Element(Head).AlignRight().Text("Franchisee");
                    h.Cell().Element(Head).AlignRight().Text("Company");
                });
                foreach (var l in b.Lines)
                {
                    t.Cell().Element(Cell).Column(cc =>
                    {
                        cc.Item().Text(l.ProductTitle).FontSize(8.5f);
                        if (l.SpecialPriceApplied)
                            cc.Item().Text("special price applied").FontSize(7.5f).Italic().FontColor("#92400E");
                    });
                    t.Cell().Element(Cell).AlignRight().Text(l.Quantity.ToString()).FontSize(8.5f);
                    t.Cell().Element(Cell).AlignRight().Text(Money(l.LineAmount, currency)).FontSize(8.5f);
                    t.Cell().Element(Cell).AlignRight().Text(l.ShareLabel).FontSize(8.5f);
                    t.Cell().Element(Cell).AlignRight().Text(Money(l.FranchiseShare, currency)).FontSize(8.5f);
                    t.Cell().Element(Cell).AlignRight().Text(Money(l.CompanyShare, currency)).FontSize(8.5f);
                }
            });

            col.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem();
                row.ConstantItem(250).Column(cc =>
                {
                    cc.Item().Element(e => KeyValue(e, "Order Total", Money(b.OrderTotal, currency)));
                    if (b.HasCommissionSplit && b.TotalGstOnCommission > 0)
                    {
                        cc.Item().Element(e => KeyValue(e, "Franchisee Commission", Money(-b.TotalCommissionAmount, currency)));
                        cc.Item().Element(e => KeyValue(e, "GST on Commission", Money(-b.TotalGstOnCommission, currency)));
                        cc.Item().Element(e => KeyValue(e, "Total Franchisee Share", Money(-b.TotalFranchiseShare, currency)));
                    }
                    else
                    {
                        cc.Item().Element(e => KeyValue(e, "Franchisee Share (commission)", Money(-b.TotalFranchiseShare, currency)));
                    }
                    cc.Item().Element(e => KeyValue(e, "Company Share", Money(b.TotalCompanyShare, currency)));
                    cc.Item().PaddingTop(2).LineHorizontal(0.4f).LineColor(Rule);
                    cc.Item().PaddingTop(2).Row(rr =>
                    {
                        rr.RelativeItem().Text("Net Payable by Franchisee").FontSize(9).Bold();
                        rr.ConstantItem(100).AlignRight().Text(Money(b.NetPayableByFranchisee, currency)).FontSize(9.5f).Bold();
                    });
                });
            });

            if (b.HasCommissionSplit)
                col.Item().PaddingTop(5).Text(b.FranchiseeIsGstRegistered
                        ? "Franchisee is GST-registered: commission is a taxable supply. Franchisee to raise a tax invoice for the commission plus GST shown above."
                        : "Franchisee is not GST-registered: no GST on commission. Franchisee to raise a bill of supply; no input tax credit arises.")
                    .FontSize(7.5f).Italic().FontColor("#374151");
        });
    }

    private static void KeyValue(IContainer container, string label, string value)
    {
        container.PaddingVertical(1).Row(r =>
        {
            r.RelativeItem().Text(label).FontSize(8.5f).FontColor("#374151");
            r.ConstantItem(100).AlignRight().Text(value).FontSize(8.5f);
        });
    }

    /// <summary>Bare 2-dp number for the ruled money columns (the column heading already says INR).</summary>
    private static string Num(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

    private static string Money(decimal v, string currency)
    {
        var symbol = currency == "INR" ? "₹" : (currency == "USD" ? "$" : currency + " ");
        return symbol + v.ToString("N2", CultureInfo.InvariantCulture);
    }
}
