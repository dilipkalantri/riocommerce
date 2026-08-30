using RioCommerce.Core.DTOs.Sharing;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Bulk Excel-based management for the two sharing tables that already exist in the project:
///   • <c>FranchiseCommissions</c> — per-franchise + per-product commission rules
///   • <c>FacultySharingRules</c>  — per-product + per-faculty share rules
///
/// Modelled on the nopCommerce / Serenity ERP import flow:
/// admin downloads a template OR exports current data, edits in Excel, re-uploads to apply.
/// Each row is validated independently — valid rows commit, invalid rows surface as errors
/// for the admin to fix.
/// </summary>
public interface ISharingImportExportService
{
    // ── Franchise commissions ─────────────────────────────────────────────────
    /// <summary>Export every active franchise commission rule as an .xlsx file.</summary>
    Task<byte[]> ExportFranchiseCommissionsAsync(CancellationToken ct = default);
    /// <summary>Returns a blank template with only the header row + one example row.</summary>
    Task<byte[]> GetFranchiseCommissionTemplateAsync(CancellationToken ct = default);
    /// <summary>Parse and upsert. Match by (FranchiseCode, ProductSku) — existing rows update,
    /// missing rows insert. Invalid rows are skipped and reported in the result.</summary>
    Task<SharingImportResult> ImportFranchiseCommissionsAsync(byte[] xlsx, Guid? actorId, string? actorName, CancellationToken ct = default);

    // ── Faculty shares ────────────────────────────────────────────────────────
    Task<byte[]> ExportFacultySharesAsync(CancellationToken ct = default);
    Task<byte[]> GetFacultyShareTemplateAsync(CancellationToken ct = default);
    Task<SharingImportResult> ImportFacultySharesAsync(byte[] xlsx, Guid? actorId, string? actorName, CancellationToken ct = default);
}
