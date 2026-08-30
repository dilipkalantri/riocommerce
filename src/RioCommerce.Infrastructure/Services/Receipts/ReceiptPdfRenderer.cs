using System.Globalization;
using RioCommerce.Core.DTOs.Orders;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RioCommerce.Infrastructure.Services.Receipts;

/// <summary>
/// QuestPDF renderer for an order RECEIPT (available for every order, any status).
///
/// Layout (A4 portrait, white background, RioCommerce blue + orange):
///   1. Brand header — monogram, company block, "RECEIPT" title, receipt/order/date meta
///   2. Two rounded cards — Customer Information and Payment Information
///   3. Products table — Sr, course (+ purchase option), SKU, qty, unit price, total
///   4. Referred By card (only when the order carries a referral) + Order Summary
///   5. Footer — thank-you line, website, page numbers
///
/// Everything is driven from <see cref="ReceiptDetail"/>; nothing is hardcoded. Optional fields
/// (email, SKU, transaction id, referral, GSTIN) are omitted entirely when absent so the layout
/// never shows empty labels.
///
/// NOTE: QuestPDF 2024.12 has no native corner-radius API, so rounded surfaces are drawn as a
/// dynamic SVG background behind the primary layer (see <see cref="RoundedSurface"/>).
/// </summary>
public static class ReceiptPdfRenderer
{
    // ── brand palette ──────────────────────────────────────────────────────────
    private const string Navy = "#0B2A5B";
    private const string NavyDeep = "#071B3D";
    private const string BlueSoft = "#EEF4FF";
    private const string Orange = "#F97316";
    private const string OrangeDeep = "#C2410C";
    private const string OrangeSoft = "#FFF5ED";
    private const string Ink = "#0F172A";
    private const string Muted = "#64748B";
    private const string Faint = "#94A3B8";
    private const string Hairline = "#E2E8F0";
    private const string Panel = "#F8FAFC";
    private const string White = "#FFFFFF";
    private const string Success = "#047857";
    private const string Warning = "#B45309";

    private const float CardRadius = 10f;
    private const string DefaultWebsite = "https://example.com";
    private const float LogoHeight = 46f;
    private const float LogoBoxWidth = 78f;   // fits the 500x332 mark at LogoHeight with slack

    /// <summary>Brand mark, loaded once from the embedded resource. Null → header falls back to
    /// a drawn monogram, so a missing/renamed asset can never break receipt generation.</summary>
    private static readonly byte[]? LogoBytes = LoadLogo();

    private static byte[]? LoadLogo()
    {
        try
        {
            var asm = typeof(ReceiptPdfRenderer).Assembly;
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("receipt-logo.png", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    public static void EnsureLicense() => QuestPDF.Settings.License = LicenseType.Community;

    public static byte[] Render(ReceiptDetail r)
    {
        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                p.Margin(30);
                p.Size(PageSizes.A4);
                p.PageColor(White);
                p.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));
                p.Header().Element(e => Header(e, r));
                p.Content().PaddingTop(14).Element(e => Body(e, r));
                p.Footer().Element(e => Footer(e, r));
            });
        });
        return doc.GeneratePdf();
    }

    // ── 1. header ──────────────────────────────────────────────────────────────
    private static void Header(IContainer container, ReceiptDetail r)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                // Brand block: monogram + company snapshot.
                row.RelativeItem().Row(brand =>
                {
                    if (LogoBytes != null)
                        brand.ConstantItem(LogoBoxWidth).Height(LogoHeight).AlignLeft().AlignMiddle()
                             .Image(LogoBytes).FitArea();
                    else
                        brand.ConstantItem(44).Element(e => Monogram(e, Initials(r.CompanyName)));

                    brand.ConstantItem(11);
                    brand.RelativeItem().Column(cc =>
                    {
                        cc.Item().Text(r.CompanyName).FontSize(16).Bold().FontColor(Navy);

                        if (!string.IsNullOrWhiteSpace(r.CompanyAddress))
                            cc.Item().PaddingTop(2).Text(r.CompanyAddress).FontSize(8.5f).FontColor(Muted);

                        var contact = new List<string>();
                        if (!string.IsNullOrWhiteSpace(r.CompanyPhone)) contact.Add(r.CompanyPhone!);
                        if (!string.IsNullOrWhiteSpace(r.CompanyEmail)) contact.Add(r.CompanyEmail!);
                        if (contact.Count > 0)
                            cc.Item().PaddingTop(1).Text(string.Join("  ·  ", contact)).FontSize(8.5f).FontColor(Muted);

                        if (!string.IsNullOrWhiteSpace(r.CompanyGstin))
                            cc.Item().PaddingTop(2).Text($"GSTIN  {r.CompanyGstin}").FontSize(8.5f).Bold().FontColor(Ink);
                    });
                });

                // Title + document meta.
                row.ConstantItem(196).Column(cc =>
                {
                    cc.Item().AlignRight().Text("RECEIPT").FontSize(23).Bold().FontColor(Navy).LetterSpacing(0.08f);
                    cc.Item().PaddingTop(3).AlignRight().Width(58).Height(3).Background(Orange);

                    cc.Item().PaddingTop(9).Element(e => RoundedSurface(e, Panel, Hairline, CardRadius, inner =>
                        inner.PaddingVertical(8).PaddingHorizontal(10).Column(meta =>
                        {
                            if (!string.IsNullOrWhiteSpace(r.ReceiptNumber))
                                MetaRow(meta, "Receipt No.", r.ReceiptNumber!, strong: true);
                            MetaRow(meta, "Order No.", r.OrderNumber, strong: true);
                            MetaRow(meta, "Date", r.OrderDateUtc.ToLocalTime().ToString("dd MMM yyyy, HH:mm"));
                            MetaRow(meta, "Order Status", r.OrderStatus);
                        })));
                });
            });

            // Brand rule — orange lead-in into navy.
            col.Item().PaddingTop(11).Row(rule =>
            {
                rule.ConstantItem(64).Height(2.5f).Background(Orange);
                rule.RelativeItem().Height(2.5f).Background(Navy);
            });
        });
    }

    private static void Monogram(IContainer container, string initials) =>
        container.Height(44).Layers(l =>
        {
            l.Layer().Svg(size => RoundedRectSvg(size, Navy, Navy, 12f));
            l.Layer().PaddingTop(30).PaddingHorizontal(11).Height(3).Background(Orange);
            l.PrimaryLayer().AlignCenter().AlignMiddle().PaddingBottom(5)
                .Text(initials).FontSize(15).Bold().FontColor(White);
        });

    private static void MetaRow(ColumnDescriptor col, string label, string value, bool strong = false)
    {
        col.Item().PaddingVertical(1.6f).Row(row =>
        {
            row.ConstantItem(62).Text(label).FontSize(8).FontColor(Muted);
            var cell = row.RelativeItem().AlignRight();
            if (strong) cell.Text(value).FontSize(9.5f).Bold().FontColor(Navy);
            else cell.Text(value).FontSize(9).FontColor(Ink);
        });
    }

    // ── 2-4. body ──────────────────────────────────────────────────────────────
    private static void Body(IContainer container, ReceiptDetail r)
    {
        container.Column(col =>
        {
            // Customer + payment cards.
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(e => CustomerCard(e, r));
                row.ConstantItem(12);
                row.RelativeItem().Element(e => PaymentCard(e, r));
            });

            col.Item().PaddingTop(14).Element(e => SectionTitle(e, "Order Details"));
            col.Item().PaddingTop(6).Element(e => LineItems(e, r));

            // Referral (optional) alongside the order summary.
            col.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (!string.IsNullOrWhiteSpace(r.ReferredBy))
                        left.Item().Element(e => ReferredByCard(e, r.ReferredBy!));

                    if (r.ReverseCharge)
                        left.Item().PaddingTop(8).Text("GST payable under Reverse Charge Mechanism (RCM).")
                            .FontSize(8.5f).Italic().FontColor(Warning);
                });
                row.ConstantItem(14);
                row.ConstantItem(238).ShowEntire().Element(e => Summary(e, r));
            });

            // NOTE: the franchisee financial bifurcation (share / company split / net payable) is
            // intentionally NOT rendered on the customer/student receipt — the student isn't party to
            // the franchise commission arrangement. Franchisees see their bifurcation in the
            // franchisee portal (FranchiseOrdersPage) and via GetBifurcationAsync.
        });
    }

    private static void CustomerCard(IContainer container, ReceiptDetail r) =>
        Card(container, cc =>
        {
            CardTitle(cc, "Customer Information");
            cc.Item().PaddingTop(7).Text(r.CustomerName).FontSize(11.5f).Bold().FontColor(Navy);

            cc.Item().PaddingTop(5).Column(f =>
            {
                if (!string.IsNullOrWhiteSpace(r.CustomerPhone)) Field(f, "Mobile", r.CustomerPhone!);
                if (!string.IsNullOrWhiteSpace(r.CustomerEmail)) Field(f, "Email", r.CustomerEmail!);

                var address = BillingAddress(r);
                if (address.Count > 0) Field(f, "Billing Address", string.Join("\n", address));

                if (!string.IsNullOrWhiteSpace(r.CustomerGstin)) Field(f, "GSTIN", r.CustomerGstin!, strong: true);
                Field(f, "Type", r.GstClassification);
            });
        });

    private static void PaymentCard(IContainer container, ReceiptDetail r) =>
        Card(container, cc =>
        {
            CardTitle(cc, "Payment Information");

            cc.Item().PaddingTop(7).Row(row =>
            {
                row.RelativeItem().Column(m =>
                {
                    m.Item().Text("Payment Method").FontSize(8).FontColor(Muted);
                    m.Item().PaddingTop(1).Text(Display(r.PaymentMode)).FontSize(11.5f).Bold().FontColor(Navy);
                });
                row.ConstantItem(96).AlignRight().AlignMiddle().Element(e => StatusPill(e, r.PaymentStatus));
            });

            cc.Item().PaddingTop(6).Column(f =>
            {
                // "Payment Method" above is the gateway/channel (Razorpay / Easebuzz / Cash); this is
                // what the customer actually paid with there (UPI / Credit Card / …), as reported by
                // the gateway. Omitted for offline orders and unreported payments.
                if (!string.IsNullOrWhiteSpace(r.GatewayPaymentMode)) Field(f, "Payment Mode", r.GatewayPaymentMode!, strong: true);
                // Order status intentionally omitted here — it already appears in the header meta block.
                if (!string.IsNullOrWhiteSpace(r.TransactionId)) Field(f, "Transaction Id", r.TransactionId!, strong: true);
                if (r.PaidAtUtc.HasValue)
                    Field(f, "Paid On", r.PaidAtUtc.Value.ToLocalTime().ToString("dd MMM yyyy, HH:mm"));
            });
        });

    private static void ReferredByCard(IContainer container, string referredBy) =>
        RoundedSurface(container, OrangeSoft, "#FCD9BD", CardRadius, inner =>
            inner.PaddingVertical(10).PaddingHorizontal(12).Column(cc =>
            {
                cc.Item().Text("REFERRED BY").FontSize(7.5f).Bold().FontColor(OrangeDeep).LetterSpacing(0.09f);
                cc.Item().PaddingTop(3).Text(referredBy).FontSize(11).Bold().FontColor(Ink);
            }));

    // ── products table ─────────────────────────────────────────────────────────
    private static void LineItems(IContainer container, ReceiptDetail r)
    {
        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.ConstantColumn(30);    // Sr No.
                c.RelativeColumn();      // Course name + purchase option
                c.ConstantColumn(74);    // SKU
                c.ConstantColumn(32);    // Qty
                c.ConstantColumn(66);    // Unit price
                c.ConstantColumn(72);    // Total
            });

            // Repeats automatically on every page when the table spans a page break.
            t.Header(h =>
            {
                h.Cell().Element(HeadCell).AlignCenter().Text("SR");
                h.Cell().Element(HeadCell).Text("COURSE NAME");
                h.Cell().Element(HeadCell).Text("SKU");
                h.Cell().Element(HeadCell).AlignCenter().Text("QTY");
                h.Cell().Element(HeadCell).AlignRight().Text("UNIT PRICE");
                h.Cell().Element(HeadCell).AlignRight().Text("TOTAL");
            });

            var zebra = false;
            foreach (var l in r.Lines)
            {
                var shade = zebra ? Panel : White;
                zebra = !zebra;

                t.Cell().Element(c => BodyCell(c, shade)).AlignCenter()
                    .Text(l.LineNumber.ToString(CultureInfo.InvariantCulture)).FontSize(9).FontColor(Muted);

                t.Cell().Element(c => BodyCell(c, shade)).Column(cc =>
                {
                    cc.Item().Text(l.Description).FontSize(9.5f).SemiBold().FontColor(Ink);
                    if (!string.IsNullOrWhiteSpace(l.ModeName))
                        cc.Item().PaddingTop(1.5f).Text($"Purchase Option: {l.ModeName}")
                            .FontSize(8).Italic().FontColor(Muted);
                    if (l.Discount > 0)
                        cc.Item().PaddingTop(1.5f).Text($"Less discount {Money(l.Discount)}")
                            .FontSize(8).FontColor(Success);
                });

                t.Cell().Element(c => BodyCell(c, shade))
                    .Text(string.IsNullOrWhiteSpace(l.Sku) ? "—" : l.Sku!).FontSize(8.5f).FontColor(Muted);

                t.Cell().Element(c => BodyCell(c, shade)).AlignCenter()
                    .Text(l.Quantity.ToString(CultureInfo.InvariantCulture)).FontSize(9);

                t.Cell().Element(c => BodyCell(c, shade)).AlignRight()
                    .Text(Money(l.UnitPrice)).FontSize(9);

                t.Cell().Element(c => BodyCell(c, shade)).AlignRight()
                    .Text(Money(l.LineTotal)).FontSize(9.5f).Bold().FontColor(Navy);
            }
        });
    }

    // ── order summary ──────────────────────────────────────────────────────────
    private static void Summary(IContainer container, ReceiptDetail r) =>
        RoundedSurface(container, White, Hairline, CardRadius, inner =>
            inner.PaddingTop(11).PaddingBottom(8).PaddingHorizontal(12).Column(cc =>
            {
                cc.Item().Text("ORDER SUMMARY").FontSize(7.5f).Bold().FontColor(Muted).LetterSpacing(0.09f);
                cc.Item().PaddingTop(7).Column(rows =>
                {
                    SummaryRow(rows, "Subtotal", r.Subtotal);
                    if (r.DiscountAmount > 0) SummaryRow(rows, "Discount", -r.DiscountAmount, Success);
                    SummaryRow(rows, "Taxable Value", r.TaxableAmount);

                    if (r.IntraState)
                    {
                        SummaryRow(rows, $"CGST @ {r.GstRate / 2:0.##}%", r.CgstAmount);
                        SummaryRow(rows, $"SGST @ {r.GstRate / 2:0.##}%", r.SgstAmount);
                    }
                    else
                    {
                        SummaryRow(rows, $"IGST @ {r.GstRate:0.##}%", r.IgstAmount);
                    }

                    SummaryRow(rows, "Shipping", r.ShippingCharges);
                });

                cc.Item().PaddingTop(9).Element(e => RoundedSurface(e, Navy, Navy, 8f, band =>
                    band.PaddingVertical(9).PaddingHorizontal(11).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle()
                            .Text("GRAND TOTAL").FontSize(9).Bold().FontColor(White).LetterSpacing(0.06f);
                        row.RelativeItem().AlignRight().AlignMiddle()
                            .Text(Money(r.TotalAmount)).FontSize(14.5f).Bold().FontColor(White);
                    })));
            }));

    private static void SummaryRow(ColumnDescriptor col, string label, decimal amount, string? valueColor = null)
    {
        col.Item().PaddingVertical(2.6f).Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(9).FontColor(Muted);
            row.ConstantItem(92).AlignRight()
                .Text((amount < 0 ? "− " : "") + Money(Math.Abs(amount)))
                .FontSize(9).SemiBold().FontColor(valueColor ?? Ink);
        });
    }

    // ── 5. footer ──────────────────────────────────────────────────────────────
    private static void Footer(IContainer container, ReceiptDetail r)
    {
        var website = string.IsNullOrWhiteSpace(r.CompanyWebsite) ? DefaultWebsite : r.CompanyWebsite!;

        container.Column(col =>
        {
            col.Item().PaddingBottom(7).Row(rule =>
            {
                rule.ConstantItem(64).Height(2).Background(Orange);
                rule.RelativeItem().Height(2).Background(Hairline);
            });

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(cc =>
                {
                    cc.Item().Text($"Thank you for choosing {r.CompanyName}.")
                        .FontSize(9.5f).Bold().FontColor(Navy);
                    cc.Item().PaddingTop(1).Text(website).FontSize(8.5f).SemiBold().FontColor(Orange);
                });

                row.ConstantItem(180).AlignRight().AlignBottom().Text(t =>
                {
                    t.Span("Computer-generated receipt  ·  Page ").FontSize(8).FontColor(Faint);
                    t.CurrentPageNumber().FontSize(8).FontColor(Faint);
                    t.Span(" of ").FontSize(8).FontColor(Faint);
                    t.TotalPages().FontSize(8).FontColor(Faint);
                });
            });
        });
    }

    // ── rounded-surface helpers ────────────────────────────────────────────────

    /// <summary>
    /// Draws <paramref name="content"/> on top of a rounded rectangle. QuestPDF 2024.12 has no
    /// corner-radius API, so the surface is a dynamic SVG sized to the laid-out element.
    /// </summary>
    private static void RoundedSurface(IContainer container, string fill, string stroke, float radius,
                                       Action<IContainer> content) =>
        container.Layers(l =>
        {
            l.Layer().Svg(size => RoundedRectSvg(size, fill, stroke, radius));
            l.PrimaryLayer().Element(content);
        });

    private static void Card(IContainer container, Action<ColumnDescriptor> content) =>
        RoundedSurface(container, White, Hairline, CardRadius, inner =>
            inner.PaddingVertical(11).PaddingHorizontal(13).Column(content));

    private static string RoundedRectSvg(Size size, string fill, string stroke, float radius)
    {
        var w = size.Width;
        var h = size.Height;
        // Guard against the degenerate/unbounded sizes QuestPDF can probe with during measurement.
        if (float.IsNaN(w) || float.IsNaN(h) || float.IsInfinity(w) || float.IsInfinity(h) || w <= 0 || h <= 0)
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"></svg>";

        const float inset = 0.5f;                       // keep the 1px stroke inside the bounds
        var rw = Math.Max(w - inset * 2, 0.1f);
        var rh = Math.Max(h - inset * 2, 0.1f);
        var rr = Math.Min(radius, Math.Min(rw, rh) / 2f);

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(w)}\" height=\"{F(h)}\" viewBox=\"0 0 {F(w)} {F(h)}\">" +
               $"<rect x=\"{F(inset)}\" y=\"{F(inset)}\" width=\"{F(rw)}\" height=\"{F(rh)}\" " +
               $"rx=\"{F(rr)}\" ry=\"{F(rr)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1\"/></svg>";
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ── small building blocks ──────────────────────────────────────────────────
    private static void SectionTitle(IContainer container, string title) =>
        container.Row(row =>
        {
            row.ConstantItem(3).Height(13).Background(Orange);
            row.ConstantItem(7);
            row.RelativeItem().AlignMiddle().Text(title).FontSize(10.5f).Bold().FontColor(Navy);
        });

    private static void CardTitle(ColumnDescriptor col, string title) =>
        col.Item().Text(title.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(Muted).LetterSpacing(0.09f);

    private static void Field(ColumnDescriptor col, string label, string value, bool strong = false)
    {
        col.Item().PaddingTop(4).Row(row =>
        {
            row.ConstantItem(74).Text(label).FontSize(8).FontColor(Faint);
            var cell = row.RelativeItem();
            if (strong) cell.Text(value).FontSize(9).SemiBold().FontColor(Ink);
            else cell.Text(value).FontSize(9).FontColor(Ink);
        });
    }

    private static void StatusPill(IContainer container, string status)
    {
        var paid = status.Equals("Success", StringComparison.OrdinalIgnoreCase);
        var fill = paid ? "#ECFDF5" : OrangeSoft;
        var edge = paid ? "#A7F3D0" : "#FCD9BD";
        var text = paid ? Success : Warning;

        container.AlignRight().Element(e =>
            RoundedSurface(e, fill, edge, 9f, inner =>
                inner.PaddingVertical(4).PaddingHorizontal(10)
                    .Text(status.ToUpperInvariant()).FontSize(8).Bold().FontColor(text).LetterSpacing(0.06f)));
    }

    private static IContainer HeadCell(IContainer c) =>
        c.Background(Navy)
         .DefaultTextStyle(s => s.FontColor(White).Bold().FontSize(7.8f).LetterSpacing(0.06f))
         .PaddingVertical(7).PaddingHorizontal(7);

    // ShowEntire keeps a line item atomic — without it a tall row (title + purchase option +
    // discount note) can be sliced in half across a page break.
    private static IContainer BodyCell(IContainer c, string background) =>
        c.Background(background).BorderBottom(0.6f).BorderColor(Hairline)
         .ShowEntire().PaddingVertical(7).PaddingHorizontal(7);

    // ── formatting ─────────────────────────────────────────────────────────────
    private static string Money(decimal v) => "₹" + v.ToString("N2", CultureInfo.InvariantCulture);

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value!;

    private static string Initials(string? companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return "RC";
        var parts = companyName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "RC";
        if (parts[0].Length >= 2 && parts[0].All(char.IsUpper)) return parts[0][..2];
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
    }

    /// <summary>Billing address lines, skipping anything the order didn't capture.</summary>
    private static List<string> BillingAddress(ReceiptDetail r)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.CustomerAddress)) lines.Add(r.CustomerAddress!.Trim());

        var city = !string.IsNullOrWhiteSpace(r.CustomerBillingCity) ? r.CustomerBillingCity : r.CustomerCity;
        var cityLine = string.Join(", ", new[] { city, r.CustomerState }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim()));
        if (!string.IsNullOrWhiteSpace(r.CustomerPincode))
            cityLine = string.IsNullOrWhiteSpace(cityLine) ? r.CustomerPincode!.Trim() : $"{cityLine} {r.CustomerPincode!.Trim()}";
        if (!string.IsNullOrWhiteSpace(cityLine)) lines.Add(cityLine);

        return lines;
    }
}
