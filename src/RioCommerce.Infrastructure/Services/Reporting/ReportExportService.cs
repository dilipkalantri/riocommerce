using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using RioCommerce.Core.DTOs.Reporting;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// Renders any report to Excel or CSV (§21, §22).
///
/// <para>Written once against <see cref="ReportTable"/> rather than per report — every report has
/// columns, rows and totals, which is all either format needs. Both run the query with paging
/// removed, so a download contains the whole filtered result rather than the page on screen
/// (§36.7).</para>
/// </summary>
public class ReportExportService : IReportExportService
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvMime = "text/csv";

    private readonly IReportingService _reports;
    public ReportExportService(IReportingService reports) => _reports = reports;

    public async Task<(byte[] bytes, string filename, string contentType)> ExportAsync(
        ReportType type, ReportQuery query, string format, CancellationToken ct = default)
    {
        // ForExport() strips paging while keeping every filter, sort and search term intact.
        var table = await _reports.RunAsync(type, query.ForExport(), ct);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var slug = string.IsNullOrWhiteSpace(table.Slug) ? "report" : table.Slug;

        return format.Equals("csv", StringComparison.OrdinalIgnoreCase)
            ? (Csv(table), $"{slug}-{stamp}.csv", CsvMime)
            : (Excel(table), $"{slug}-{stamp}.xlsx", XlsxMime);
    }

    // ─────────────── Excel ───────────────

    private static byte[] Excel(ReportTable t)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SheetName(t.Title));

        var cols = Math.Max(1, t.Columns.Count);

        var title = ws.Range(1, 1, 1, cols).Merge();
        title.Value = t.Title;
        title.Style.Font.FontSize = 15;
        title.Style.Font.Bold = true;

        // The subtitle records the filters that produced the file, so an exported sheet is still
        // self-describing weeks later when nobody remembers what was selected.
        var sub = ws.Range(2, 1, 2, cols).Merge();
        sub.Value = t.Subtitle;
        sub.Style.Font.FontSize = 10;
        sub.Style.Font.FontColor = XLColor.Gray;

        const int headerRow = 4;
        for (var c = 0; c < t.Columns.Count; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = t.Columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            if (t.Columns[c].IsNumeric)
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        for (var r = 0; r < t.Rows.Count; r++)
        {
            var row = t.Rows[r];
            for (var c = 0; c < row.Length && c < t.Columns.Count; c++)
            {
                var cell = ws.Cell(headerRow + 1 + r, c + 1);
                var raw = row[c];
                var col = t.Columns[c];

                // Money and counts go in as real numbers so the recipient can total and pivot the
                // sheet. A blank stays blank rather than becoming a misleading zero.
                if (col.IsNumeric && !string.IsNullOrEmpty(raw)
                    && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var n))
                {
                    cell.Value = n;
                    cell.Style.NumberFormat.Format = col.Kind == ReportColumnKind.Money ? "#,##0.00" : "#,##0";
                }
                else cell.Value = raw;
            }
        }

        var totals = t.Totals.Items().ToList();
        if (totals.Count > 0)
        {
            var start = headerRow + t.Rows.Count + 3;
            var heading = ws.Cell(start, 1);
            heading.Value = "Summary";
            heading.Style.Font.Bold = true;
            heading.Style.Font.FontSize = 12;

            for (var i = 0; i < totals.Count; i++)
            {
                ws.Cell(start + 1 + i, 1).Value = totals[i].Label;
                ws.Cell(start + 1 + i, 1).Style.Font.Bold = true;
                ws.Cell(start + 1 + i, 2).Value = totals[i].Value;
            }
        }

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Excel rejects <c>: \ / ? * [ ]</c> in sheet names and caps them at 31 characters.</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Select(ch => @"\/:*?[]".Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (clean.Length == 0) clean = "Report";
        return clean.Length > 31 ? clean[..31] : clean;
    }

    // ─────────────── CSV ───────────────

    private static byte[] Csv(ReportTable t)
    {
        var sb = new StringBuilder();

        // Title and filter line first, then a blank row. Spreadsheet apps skip straight to the
        // header row, and the provenance travels with the file.
        sb.Append(Escape(t.Title)).Append('\n');
        sb.Append(Escape(t.Subtitle)).Append('\n');
        sb.Append('\n');

        sb.AppendJoin(',', t.Columns.Select(c => Escape(c.Header))).Append('\n');

        foreach (var row in t.Rows)
            sb.AppendJoin(',', row.Select(Escape)).Append('\n');

        var totals = t.Totals.Items().ToList();
        if (totals.Count > 0)
        {
            sb.Append('\n').Append("Summary").Append('\n');
            foreach (var (label, value) in totals)
                sb.Append(Escape(label)).Append(',').Append(Escape(value)).Append('\n');
        }

        // UTF-8 WITH a BOM: the money columns carry a rupee sign and several report titles contain
        // one too. Excel on Windows assumes the system codepage for a BOM-less file and turns
        // every ₹ into mojibake.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString());
    }

    /// <summary>
    /// RFC 4180 quoting. Also defuses formula injection: a cell beginning <c>= + - @</c> or a
    /// control character is executed by Excel and Sheets when the file is opened, and report cells
    /// carry user-supplied text — student names, notes, product titles. Prefixing a tab neutralises
    /// it while leaving the value legible.
    /// </summary>
    private static string Escape(string? value)
    {
        var s = value ?? string.Empty;

        if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
            s = "\t" + s;

        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return '"' + s.Replace("\"", "\"\"") + '"';

        return s;
    }
}
