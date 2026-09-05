using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

public interface ISchoolService
{
    Task<List<SchoolListItem>> ListAsync(string? search = null, Guid? districtId = null);

    /// <summary>
    /// Type-ahead lookup for the public school picker: active schools in ONE taluka,
    /// optionally narrowed by name / UDISE / city, capped at <paramref name="limit"/>.
    /// Returns the total match count alongside the page so the UI can tell the user
    /// there is more to find rather than silently truncating.
    ///
    /// <para>The taluka is a REQUIRED argument, not an optional narrowing. The picker must never
    /// be able to fall back to a district-wide list — in Pune that is 8,038 rows — and making the
    /// parameter non-nullable is what guarantees it: there is no overload that omits it, so no
    /// caller can accidentally widen the search. A school whose TalukaId is NULL is NOT returned;
    /// students at one of those reach it through the "Other" option instead.</para>
    ///
    /// <para><paramref name="districtId"/> is still checked alongside the taluka. The two are
    /// redundant in clean data, which is exactly why it is kept: it costs nothing and stops a
    /// mismatched pair from ever resolving to rows.</para>
    /// </summary>
    Task<(List<SchoolOption> Items, int Total)> SearchInTalukaAsync(
        Guid districtId, Guid talukaId, string? search = null, int limit = 40);
    Task<SchoolEditModel?> GetAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveAsync(SchoolEditModel model);
    Task<(bool ok, string? error)> ToggleAsync(Guid id);
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
    Task<SchoolImportResult> ImportFromExcelAsync(Stream excelStream);
    Task<SchoolEditModel?> FindByUdiseAsync(string udiseCode);
}
