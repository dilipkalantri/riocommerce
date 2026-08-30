using ClosedXML.Excel;
using RioCommerce.Core.DTOs.Sharing;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// ClosedXML-backed import/export for the two sharing tables. The wire format mirrors the
/// nopCommerce-style spreadsheets used in the reference ERP — one header row, one config per
/// data row, blank rows tolerated. Upserts by business key (FranchiseCode + ProductSku for
/// franchise; FacultyShortCode + ProductSku for faculty).
///
/// All writes happen in a single transaction so a partial import is impossible — either
/// everything that passed validation lands, or nothing does.
/// </summary>
public sealed class SharingImportExportService : ISharingImportExportService
{
    // ── Column indexes (1-based, ClosedXML convention) ──
    private const int COL_FRAN_CODE = 1;
    private const int COL_FRAN_SKU = 2;
    private const int COL_FRAN_TYPE = 3;
    private const int COL_FRAN_VALUE = 4;
    private const int COL_FRAN_EFFECTIVE = 5;
    private const int COL_FRAN_ACTIVE = 6;

    private const int COL_FAC_CODE = 1;
    private const int COL_FAC_SKU = 2;
    private const int COL_FAC_TYPE = 3;
    private const int COL_FAC_VALUE = 4;
    private const int COL_FAC_EFFECTIVE = 5;
    private const int COL_FAC_ACTIVE = 6;
    private const int COL_FAC_NOTES = 7;

    private readonly RioCommerceDbContext _db;
    private readonly IAppLogService _appLog;
    private readonly IFacultyShareCalculator _facultyShares;
    private readonly ILogger<SharingImportExportService> _log;

    public SharingImportExportService(RioCommerceDbContext db, IAppLogService appLog,
        IFacultyShareCalculator facultyShares, ILogger<SharingImportExportService> log)
    {
        _db = db; _appLog = appLog; _facultyShares = facultyShares; _log = log;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Franchise commissions — export, template, import
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<byte[]> ExportFranchiseCommissionsAsync(CancellationToken ct = default)
    {
        var rows = await (from c in _db.Set<FranchiseCommission>().AsNoTracking()
                          join f in _db.Set<Franchise>().AsNoTracking() on c.FranchiseId equals f.Id
                          join p in _db.Set<Product>().AsNoTracking() on c.ProductId equals p.Id
                          orderby f.Code, p.Sku
                          select new
                          {
                              FranchiseCode = f.Code,
                              FranchiseName = f.Name,
                              Sku = p.Sku,
                              ProductTitle = p.Title,
                              Type = c.Type,
                              c.Value,
                              c.EffectiveFrom,
                              c.IsActive,
                          }).ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("FranchiseCommissions");
        WriteFranchiseHeader(ws);
        var rIdx = 2;
        foreach (var r in rows)
        {
            ws.Cell(rIdx, COL_FRAN_CODE).Value = r.FranchiseCode;
            ws.Cell(rIdx, COL_FRAN_SKU).Value = r.Sku ?? "";
            ws.Cell(rIdx, COL_FRAN_TYPE).Value = r.Type == CommissionType.Percent ? "Percent" : "Fixed";
            ws.Cell(rIdx, COL_FRAN_VALUE).Value = (double)r.Value;
            ws.Cell(rIdx, COL_FRAN_EFFECTIVE).Value = r.EffectiveFrom;
            ws.Cell(rIdx, COL_FRAN_ACTIVE).Value = r.IsActive ? "YES" : "NO";
            // Helpful context columns (not parsed on import — there for the admin's eye)
            ws.Cell(rIdx, COL_FRAN_ACTIVE + 1).Value = r.FranchiseName;
            ws.Cell(rIdx, COL_FRAN_ACTIVE + 2).Value = r.ProductTitle;
            rIdx++;
        }
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    public Task<byte[]> GetFranchiseCommissionTemplateAsync(CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("FranchiseCommissions");
        WriteFranchiseHeader(ws);
        ws.Cell(2, COL_FRAN_CODE).Value = "FR001";
        ws.Cell(2, COL_FRAN_SKU).Value = "CA-FOUND-LIVE-001";
        ws.Cell(2, COL_FRAN_TYPE).Value = "Percent";
        ws.Cell(2, COL_FRAN_VALUE).Value = 10;
        ws.Cell(2, COL_FRAN_EFFECTIVE).Value = DateTime.Today;
        ws.Cell(2, COL_FRAN_ACTIVE).Value = "YES";
        AddFranchiseHelpSheet(wb);
        ws.Columns().AdjustToContents();
        return Task.FromResult(Save(wb));
    }

    private static void WriteFranchiseHeader(IXLWorksheet ws)
    {
        ws.Cell(1, COL_FRAN_CODE).Value = "FranchiseCode";
        ws.Cell(1, COL_FRAN_SKU).Value = "ProductSku";
        ws.Cell(1, COL_FRAN_TYPE).Value = "Type";
        ws.Cell(1, COL_FRAN_VALUE).Value = "Value";
        ws.Cell(1, COL_FRAN_EFFECTIVE).Value = "EffectiveFrom";
        ws.Cell(1, COL_FRAN_ACTIVE).Value = "IsActive";
        ws.Cell(1, COL_FRAN_ACTIVE + 1).Value = "FranchiseName (info)";
        ws.Cell(1, COL_FRAN_ACTIVE + 2).Value = "ProductTitle (info)";
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.SheetView.FreezeRows(1);
    }

    private static void AddFranchiseHelpSheet(XLWorkbook wb)
    {
        var help = wb.Worksheets.Add("Instructions");
        var lines = new[]
        {
            "Franchise Commissions — Import Format",
            "",
            "Required columns (in order):",
            "  FranchiseCode  — must match an existing franchise's Code (case-insensitive)",
            "  ProductSku     — must match an existing product's Sku (case-insensitive)",
            "  Type           — Percent  OR  Fixed",
            "  Value          — Number > 0. For Percent: 0 < value ≤ 100",
            "  EffectiveFrom  — Optional. Date the rule starts applying (today if blank)",
            "  IsActive       — YES / NO  (default YES if blank)",
            "",
            "Behaviour:",
            "  Existing (FranchiseCode + ProductSku) pair → row is UPDATED",
            "  No match → row is INSERTED",
            "  Invalid row → skipped, error returned with row number",
            "",
            "The two trailing columns (FranchiseName, ProductTitle) are ignored on import; they exist",
            "to help you cross-check the codes/SKUs in the exported file.",
        };
        for (var i = 0; i < lines.Length; i++)
            help.Cell(i + 1, 1).Value = lines[i];
        help.Row(1).Style.Font.Bold = true;
        help.Column(1).Width = 110;
    }

    public async Task<SharingImportResult> ImportFranchiseCommissionsAsync(byte[] xlsx, Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        var result = new SharingImportResult();
        using var ms = new MemoryStream(xlsx);
        XLWorkbook wb;
        try { wb = new XLWorkbook(ms); }
        catch (Exception ex)
        {
            result.Errors.Add(new SharingImportError { RowNumber = 0, Column = "(file)", Message = $"Could not read the .xlsx — {ex.Message}" });
            return result;
        }

        var ws = wb.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // Pre-load all franchises and products keyed by code/sku for fast lookup.
        var franchisesByCode = await _db.Set<Franchise>().AsNoTracking()
            .ToDictionaryAsync(f => f.Code.ToUpperInvariant(), f => f, ct);
        var productsBySku = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.Sku != null)
            .ToDictionaryAsync(p => p.Sku!.ToUpperInvariant(), p => p, ct);

        // Stage rows, then commit in one SaveChanges so a partial run is impossible.
        var toUpsert = new List<(FranchiseCommission row, bool isNew)>();
        var existingByKey = await _db.Set<FranchiseCommission>()
            .ToDictionaryAsync(c => $"{c.FranchiseId}|{c.ProductId}", c => c, ct);

        for (var r = 2; r <= lastRow; r++)
        {
            var code  = ws.Cell(r, COL_FRAN_CODE).GetString().Trim();
            var sku   = ws.Cell(r, COL_FRAN_SKU).GetString().Trim();
            var type  = ws.Cell(r, COL_FRAN_TYPE).GetString().Trim();
            var valS  = ws.Cell(r, COL_FRAN_VALUE).GetString().Trim();
            var effS  = ws.Cell(r, COL_FRAN_EFFECTIVE);
            var actS  = ws.Cell(r, COL_FRAN_ACTIVE).GetString().Trim();

            // Skip fully empty rows silently — common for users who clear the example row.
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(sku) && string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(valS))
                continue;

            result.TotalRows++;

            if (string.IsNullOrWhiteSpace(code))      { Err(r, "FranchiseCode", "Required", code); continue; }
            if (string.IsNullOrWhiteSpace(sku))       { Err(r, "ProductSku",    "Required", sku); continue; }
            if (string.IsNullOrWhiteSpace(type))      { Err(r, "Type",          "Required (Percent or Fixed)", type); continue; }
            if (string.IsNullOrWhiteSpace(valS))      { Err(r, "Value",         "Required", valS); continue; }

            if (!franchisesByCode.TryGetValue(code.ToUpperInvariant(), out var franchise))
            { Err(r, "FranchiseCode", "No franchise found with that code", code); continue; }

            if (!productsBySku.TryGetValue(sku.ToUpperInvariant(), out var product))
            { Err(r, "ProductSku", "No product found with that SKU", sku); continue; }

            CommissionType ctype;
            if (string.Equals(type, "Percent", StringComparison.OrdinalIgnoreCase) || type == "%") ctype = CommissionType.Percent;
            else if (string.Equals(type, "Fixed", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "FixAmount", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "FixedAmount", StringComparison.OrdinalIgnoreCase)) ctype = CommissionType.Fixed;
            else { Err(r, "Type", "Must be 'Percent' or 'Fixed'", type); continue; }

            if (!decimal.TryParse(valS, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val) || val <= 0)
            { Err(r, "Value", "Must be a positive number", valS); continue; }
            if (ctype == CommissionType.Percent && val > 100)
            { Err(r, "Value", "Percentage cannot exceed 100", valS); continue; }

            DateTime effectiveFrom = DateTime.UtcNow;
            if (!effS.IsEmpty())
            {
                if (effS.TryGetValue<DateTime>(out var dt)) effectiveFrom = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                else if (DateTime.TryParse(effS.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt2))
                    effectiveFrom = dt2.ToUniversalTime();
                else { Err(r, "EffectiveFrom", "Could not parse as a date", effS.GetString()); continue; }
            }

            var isActive = string.IsNullOrWhiteSpace(actS) || string.Equals(actS, "YES", StringComparison.OrdinalIgnoreCase) || string.Equals(actS, "TRUE", StringComparison.OrdinalIgnoreCase) || actS == "1";

            var key = $"{franchise.Id}|{product.Id}";
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.Type = ctype;
                existing.Value = val;
                existing.EffectiveFrom = effectiveFrom;
                existing.IsActive = isActive;
                toUpsert.Add((existing, false));
            }
            else
            {
                var ne = new FranchiseCommission
                {
                    Id = Guid.NewGuid(),
                    FranchiseId = franchise.Id,
                    ProductId = product.Id,
                    Type = ctype,
                    Value = val,
                    EffectiveFrom = effectiveFrom,
                    IsActive = isActive,
                };
                _db.Set<FranchiseCommission>().Add(ne);
                toUpsert.Add((ne, true));
            }
        }

        if (result.Errors.Count > 0 && toUpsert.Count == 0)
            return result;   // nothing to save; let the admin fix and re-upload

        await _db.SaveChangesAsync(ct);
        result.Inserted = toUpsert.Count(x => x.isNew);
        result.Updated = toUpsert.Count(x => !x.isNew);
        result.Skipped = result.Errors.Count;

        await _appLog.InfoAsync("Sharing",
            $"Franchise-commission import: {result.Inserted} new, {result.Updated} updated, {result.Skipped} skipped.",
            eventCode: "sharing.franchise_import",
            properties: new { result.Inserted, result.Updated, result.Skipped, Actor = actorName, result.TotalRows },
            ct: ct);

        return result;

        void Err(int row, string col, string msg, string? value)
            => result.Errors.Add(new SharingImportError { RowNumber = row, Column = col, Message = msg, Value = value });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Faculty shares — export, template, import
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<byte[]> ExportFacultySharesAsync(CancellationToken ct = default)
    {
        var rows = await (from r in _db.Set<FacultySharingRule>().AsNoTracking()
                          join f in _db.Set<Faculty>().AsNoTracking() on r.FacultyId equals f.Id
                          join p in _db.Set<Product>().AsNoTracking() on r.ProductId equals p.Id
                          orderby f.ShortCode, p.Sku
                          select new
                          {
                              FacultyCode = f.ShortCode,
                              FacultyName = f.DisplayName,
                              Sku = p.Sku,
                              ProductTitle = p.Title,
                              r.ShareType,
                              r.ShareValue,
                              r.EffectiveFrom,
                              r.IsActive,
                              r.Notes,
                          }).ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("FacultyShares");
        WriteFacultyHeader(ws);
        var rIdx = 2;
        foreach (var r in rows)
        {
            ws.Cell(rIdx, COL_FAC_CODE).Value = r.FacultyCode;
            ws.Cell(rIdx, COL_FAC_SKU).Value = r.Sku ?? "";
            ws.Cell(rIdx, COL_FAC_TYPE).Value = r.ShareType == SharingType.Percentage ? "Percentage" : "FixedAmount";
            ws.Cell(rIdx, COL_FAC_VALUE).Value = (double)r.ShareValue;
            ws.Cell(rIdx, COL_FAC_EFFECTIVE).Value = r.EffectiveFrom;
            ws.Cell(rIdx, COL_FAC_ACTIVE).Value = r.IsActive ? "YES" : "NO";
            ws.Cell(rIdx, COL_FAC_NOTES).Value = r.Notes ?? "";
            ws.Cell(rIdx, COL_FAC_NOTES + 1).Value = r.FacultyName;
            ws.Cell(rIdx, COL_FAC_NOTES + 2).Value = r.ProductTitle;
            rIdx++;
        }
        ws.Columns().AdjustToContents();
        return Save(wb);
    }

    public Task<byte[]> GetFacultyShareTemplateAsync(CancellationToken ct = default)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("FacultyShares");
        WriteFacultyHeader(ws);
        ws.Cell(2, COL_FAC_CODE).Value = "JV";
        ws.Cell(2, COL_FAC_SKU).Value = "CA-FOUND-LIVE-001";
        ws.Cell(2, COL_FAC_TYPE).Value = "Percentage";
        ws.Cell(2, COL_FAC_VALUE).Value = 30;
        ws.Cell(2, COL_FAC_EFFECTIVE).Value = DateTime.Today;
        ws.Cell(2, COL_FAC_ACTIVE).Value = "YES";
        ws.Cell(2, COL_FAC_NOTES).Value = "Updated for FY26 batch";
        AddFacultyHelpSheet(wb);
        ws.Columns().AdjustToContents();
        return Task.FromResult(Save(wb));
    }

    private static void WriteFacultyHeader(IXLWorksheet ws)
    {
        ws.Cell(1, COL_FAC_CODE).Value = "FacultyShortCode";
        ws.Cell(1, COL_FAC_SKU).Value = "ProductSku";
        ws.Cell(1, COL_FAC_TYPE).Value = "ShareType";
        ws.Cell(1, COL_FAC_VALUE).Value = "Value";
        ws.Cell(1, COL_FAC_EFFECTIVE).Value = "EffectiveFrom";
        ws.Cell(1, COL_FAC_ACTIVE).Value = "IsActive";
        ws.Cell(1, COL_FAC_NOTES).Value = "Notes";
        ws.Cell(1, COL_FAC_NOTES + 1).Value = "FacultyName (info)";
        ws.Cell(1, COL_FAC_NOTES + 2).Value = "ProductTitle (info)";
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.SheetView.FreezeRows(1);
    }

    private static void AddFacultyHelpSheet(XLWorkbook wb)
    {
        var help = wb.Worksheets.Add("Instructions");
        var lines = new[]
        {
            "Faculty Revenue Sharing — Import Format",
            "",
            "Required columns (in order):",
            "  FacultyShortCode — must match an existing faculty's ShortCode (case-insensitive)",
            "  ProductSku       — must match an existing product's Sku",
            "  ShareType        — Percentage  OR  FixedAmount",
            "  Value            — Number > 0. Percentage: 0 < value ≤ 100",
            "  EffectiveFrom    — Optional date this configuration takes effect from",
            "  IsActive         — YES / NO (default YES)",
            "  Notes            — Optional free-text",
            "",
            "Behaviour:",
            "  Existing (FacultyShortCode + ProductSku) → UPDATE",
            "  No match → INSERT",
            "  Invalid row → skipped, error returned with row number",
        };
        for (var i = 0; i < lines.Length; i++)
            help.Cell(i + 1, 1).Value = lines[i];
        help.Row(1).Style.Font.Bold = true;
        help.Column(1).Width = 110;
    }

    public async Task<SharingImportResult> ImportFacultySharesAsync(byte[] xlsx, Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        var result = new SharingImportResult();
        using var ms = new MemoryStream(xlsx);
        XLWorkbook wb;
        try { wb = new XLWorkbook(ms); }
        catch (Exception ex)
        {
            result.Errors.Add(new SharingImportError { RowNumber = 0, Column = "(file)", Message = $"Could not read the .xlsx — {ex.Message}" });
            return result;
        }

        var ws = wb.Worksheet(1);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        var facultiesByCode = await _db.Set<Faculty>().AsNoTracking()
            .ToDictionaryAsync(f => f.ShortCode.ToUpperInvariant(), f => f, ct);
        var productsBySku = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.Sku != null)
            .ToDictionaryAsync(p => p.Sku!.ToUpperInvariant(), p => p, ct);
        var existingByKey = await _db.Set<FacultySharingRule>()
            .ToDictionaryAsync(r => $"{r.FacultyId}|{r.ProductId}", r => r, ct);

        // A NEW share requires the faculty to be on the product's faculty list. Rows that UPDATE an
        // existing rule are exempt, so a spreadsheet can still re-rate a legacy agreement whose
        // attachment was removed.
        var attachedPairs = (await _db.Set<ProductFaculty>().AsNoTracking()
                .Select(pf => new { pf.ProductId, pf.FacultyId }).ToListAsync(ct))
            .Select(pf => $"{pf.FacultyId}|{pf.ProductId}").ToHashSet();

        var staged = 0;
        // productId → spreadsheet rows that touched it, so a cap breach can be reported against the
        // actual rows the admin needs to fix rather than as one opaque file-level failure.
        var stagedRowsByProduct = new Dictionary<Guid, List<int>>();

        for (var r = 2; r <= lastRow; r++)
        {
            var code  = ws.Cell(r, COL_FAC_CODE).GetString().Trim();
            var sku   = ws.Cell(r, COL_FAC_SKU).GetString().Trim();
            var type  = ws.Cell(r, COL_FAC_TYPE).GetString().Trim();
            var valS  = ws.Cell(r, COL_FAC_VALUE).GetString().Trim();
            var effS  = ws.Cell(r, COL_FAC_EFFECTIVE);
            var actS  = ws.Cell(r, COL_FAC_ACTIVE).GetString().Trim();
            var notes = ws.Cell(r, COL_FAC_NOTES).GetString().Trim();

            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(sku) && string.IsNullOrWhiteSpace(type) && string.IsNullOrWhiteSpace(valS))
                continue;

            result.TotalRows++;

            if (string.IsNullOrWhiteSpace(code))   { Err(r, "FacultyShortCode", "Required", code); continue; }
            if (string.IsNullOrWhiteSpace(sku))    { Err(r, "ProductSku",       "Required", sku); continue; }
            if (string.IsNullOrWhiteSpace(type))   { Err(r, "ShareType",        "Required (Percentage or FixedAmount)", type); continue; }
            if (string.IsNullOrWhiteSpace(valS))   { Err(r, "Value",            "Required", valS); continue; }

            if (!facultiesByCode.TryGetValue(code.ToUpperInvariant(), out var faculty))
            { Err(r, "FacultyShortCode", "No faculty found with that short code", code); continue; }
            if (!productsBySku.TryGetValue(sku.ToUpperInvariant(), out var product))
            { Err(r, "ProductSku", "No product found with that SKU", sku); continue; }

            SharingType stype;
            if (string.Equals(type, "Percentage", StringComparison.OrdinalIgnoreCase) || type == "%") stype = SharingType.Percentage;
            else if (string.Equals(type, "FixedAmount", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "Fixed", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "FixAmount", StringComparison.OrdinalIgnoreCase)) stype = SharingType.FixedAmount;
            else { Err(r, "ShareType", "Must be 'Percentage' or 'FixedAmount'", type); continue; }

            if (!decimal.TryParse(valS, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val) || val <= 0)
            { Err(r, "Value", "Must be a positive number", valS); continue; }
            if (stype == SharingType.Percentage && val > 100)
            { Err(r, "Value", "Percentage cannot exceed 100", valS); continue; }

            DateTime? effectiveFrom = null;
            if (!effS.IsEmpty())
            {
                if (effS.TryGetValue<DateTime>(out var dt)) effectiveFrom = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                else if (DateTime.TryParse(effS.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt2))
                    effectiveFrom = dt2.ToUniversalTime();
                else { Err(r, "EffectiveFrom", "Could not parse as a date", effS.GetString()); continue; }
            }

            var isActive = string.IsNullOrWhiteSpace(actS) || string.Equals(actS, "YES", StringComparison.OrdinalIgnoreCase) || string.Equals(actS, "TRUE", StringComparison.OrdinalIgnoreCase) || actS == "1";

            var key = $"{faculty.Id}|{product.Id}";
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.ShareType = stype;
                existing.ShareValue = val;
                existing.EffectiveFrom = effectiveFrom;
                existing.IsActive = isActive;
                existing.Notes = string.IsNullOrWhiteSpace(notes) ? existing.Notes : notes;
                result.Updated++;
            }
            else
            {
                if (!attachedPairs.Contains(key))
                {
                    Err(r, "FacultyShortCode",
                        $"'{faculty.DisplayName}' is not assigned to product '{product.Title}'. " +
                        "Add them to the product's Faculty list first, then re-import.", code);
                    continue;
                }

                var ne = new FacultySharingRule
                {
                    Id = Guid.NewGuid(),
                    FacultyId = faculty.Id,
                    ProductId = product.Id,
                    ShareType = stype,
                    ShareValue = val,
                    EffectiveFrom = effectiveFrom,
                    IsActive = isActive,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                };
                _db.Set<FacultySharingRule>().Add(ne);
                existingByKey[key] = ne;   // so a later duplicate row in the same file updates rather than inserts
                result.Inserted++;
            }
            staged++;
            if (!stagedRowsByProduct.TryGetValue(product.Id, out var rowsForProduct))
                stagedRowsByProduct[product.Id] = rowsForProduct = new List<int>();
            rowsForProduct.Add(r);
        }

        if (staged == 0)
            return result;   // nothing to save; admin can fix and re-upload

        // ── Combined-share cap ──────────────────────────────────────────────────────────────────
        // Per-row validation cannot catch the multi-faculty failure: a spreadsheet where every row is
        // individually fine can still put three faculty on 40% of the same product. Checked here so a
        // bulk upload cannot do what the UI and the API both refuse.
        var capErrors = await ValidateFacultyCapAsync(stagedRowsByProduct, ct);
        if (capErrors.Count > 0)
        {
            // Whole-file abort rather than dropping the offending rows. Partially applying an
            // over-allocated import would leave exactly the state this check exists to prevent, and
            // the admin cannot tell which half landed.
            result.Errors.AddRange(capErrors);
            _db.ChangeTracker.Clear();
            result.Inserted = 0;
            result.Updated = 0;
            result.Skipped = result.Errors.Count;
            return result;
        }

        await _db.SaveChangesAsync(ct);
        result.Skipped = result.Errors.Count;

        await _appLog.InfoAsync("Sharing",
            $"Faculty-share import: {result.Inserted} new, {result.Updated} updated, {result.Skipped} skipped.",
            eventCode: "sharing.faculty_import",
            properties: new { result.Inserted, result.Updated, result.Skipped, Actor = actorName, result.TotalRows },
            ct: ct);

        return result;

        void Err(int row, string col, string msg, string? value)
            => result.Errors.Add(new SharingImportError { RowNumber = row, Column = col, Message = msg, Value = value });
    }

    /// <summary>
    /// Checks each product touched by the import against
    /// <c>FacultySettings.MaxTotalSharePct</c>, using the rules as they WOULD BE after the import
    /// (the change tracker already holds the staged edits, so reading the tracked entities gives the
    /// post-import state without saving).
    /// </summary>
    /// <returns>One error per affected spreadsheet row; empty when everything fits.</returns>
    private async Task<List<SharingImportError>> ValidateFacultyCapAsync(
        Dictionary<Guid, List<int>> stagedRowsByProduct, CancellationToken ct)
    {
        var errors = new List<SharingImportError>();
        if (stagedRowsByProduct.Count == 0) return errors;

        var settings = await _facultyShares.GetSettingsAsync(ct);
        if (!settings.EnforceMaxTotalShare) return errors;

        var productIds = stagedRowsByProduct.Keys.ToList();
        var products = await _db.Set<Product>().AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // Tracked (not AsNoTracking) so the staged inserts/updates are what gets evaluated.
        var rulesByProduct = _db.Set<FacultySharingRule>().Local
            .Where(r => productIds.Contains(r.ProductId))
            .GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Rules already in the DB for these products that the import didn't touch still count toward
        // the total — a file adding one 60% row to a product that already had 60% must fail.
        var untouched = await _db.Set<FacultySharingRule>().AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId)).ToListAsync(ct);

        var facultyIds = untouched.Select(r => r.FacultyId)
            .Concat(rulesByProduct.Values.SelectMany(r => r).Select(r => r.FacultyId))
            .Distinct().ToList();
        var attachments = await _db.Set<ProductFaculty>().AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId }).ToListAsync(ct);
        facultyIds = facultyIds.Concat(attachments.Select(a => a.FacultyId)).Distinct().ToList();

        var infoMap = (await _db.Set<Faculty>().AsNoTracking()
                .Where(f => facultyIds.Contains(f.Id)).ToListAsync(ct))
            .ToDictionary(f => f.Id, FacultyShareCalculator.Info);
        var attachedByProduct = attachments.GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToList());

        foreach (var (productId, rows) in stagedRowsByProduct)
        {
            if (!products.TryGetValue(productId, out var product)) continue;

            rulesByProduct.TryGetValue(productId, out var tracked);
            tracked ??= new List<FacultySharingRule>();
            var trackedFacultyIds = tracked.Select(r => r.FacultyId).ToHashSet();

            var all = tracked
                .Concat(untouched.Where(r => r.ProductId == productId && !trackedFacultyIds.Contains(r.FacultyId)))
                .ToList();

            attachedByProduct.TryGetValue(productId, out var attachedList);
            var attached = (attachedList ?? new List<Guid>()).ToHashSet();
            var map = new Dictionary<Guid, FacultyGstInfo>();
            foreach (var fid in attached.Concat(all.Select(r => r.FacultyId)).Distinct())
                if (infoMap.TryGetValue(fid, out var info)) map[fid] = info;

            var preview = _facultyShares.Calculate(product, all, map, attached, settings);
            if (!preview.ExceedsConfiguredCap) continue;

            foreach (var row in rows)
                errors.Add(new SharingImportError
                {
                    RowNumber = row,
                    Column = "Value",
                    Message = $"'{product.Title}' would total {preview.TotalSharePctOfBase:0.##}% faculty share " +
                              $"across {preview.Lines.Count} faculty, over the {settings.MaxTotalSharePct:0.##}% limit. " +
                              "No rows were imported.",
                    Value = null
                });
        }

        return errors;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] Save(XLWorkbook wb)
    {
        using var output = new MemoryStream();
        wb.SaveAs(output);
        return output.ToArray();
    }
}
