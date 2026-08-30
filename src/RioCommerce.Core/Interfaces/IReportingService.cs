using RioCommerce.Core.DTOs.Reporting;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// The single entry point for all eight reports (§27, §29).
///
/// <para>One method rather than eight: every report takes the same <see cref="ReportQuery"/> and
/// returns the same <see cref="ReportTable"/>, so the page, the filter bar, the pager and both
/// exporters are written once and work for every report — including any added later.</para>
///
/// <para>Filtering, sorting and paging all happen server-side against <c>IQueryable</c>, and the
/// totals are computed over the whole filtered set before paging (§20, §24, §36.18/19).</para>
/// </summary>
public interface IReportingService
{
    Task<ReportTable> RunAsync(ReportType type, ReportQuery query, CancellationToken ct = default);

    /// <summary>
    /// Lookup lists for the shared multi-select filter bar.
    ///
    /// <para>Pass the current <paramref name="query"/> to make the catalog pickers cascade: Faculty,
    /// Product and Subject each narrow to what the OTHER two allow, so picking a faculty stops the
    /// Product list offering courses that faculty does not teach. Each dimension ignores its own
    /// selection, otherwise choosing one product would leave that product as the only one on offer
    /// and a second could never be added.</para>
    ///
    /// <para>Franchisees and couriers are deliberately NOT cascaded — neither has a catalog
    /// relationship, so narrowing them would mean querying orders on every keystroke. Status filters
    /// are fixed enums and stay independent for the same reason.</para>
    ///
    /// <para>Omit the query (or pass one with nothing selected) and the lists are exactly what they
    /// have always been — every active faculty and subject, and every product.</para>
    /// </summary>
    Task<ReportFilterOptions> GetFilterOptionsAsync(ReportQuery? query = null, CancellationToken ct = default);
}

/// <summary>Renders a <see cref="ReportTable"/> to a downloadable file. Both formats receive the
/// unpaged, filtered result, so a download always matches the filters on screen (§21, §22).</summary>
public interface IReportExportService
{
    Task<(byte[] bytes, string filename, string contentType)> ExportAsync(
        ReportType type, ReportQuery query, string format, CancellationToken ct = default);
}
