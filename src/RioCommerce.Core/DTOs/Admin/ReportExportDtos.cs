using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

// Common filter shared by all report types — only some fields apply to each.
public class ReportFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? FranchiseId { get; set; }       // null = all franchises (admin)
    public Guid? FacultyId { get; set; }         // faculty-share report; null = all faculty
    public OrderStatus? Status { get; set; }     // orders report
    public OrderSource? Source { get; set; }     // orders report
}

// Tabular report payload that the Excel/PDF renderers turn into a downloadable file.
public class ReportData
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string[] Headers { get; set; } = Array.Empty<string>();
    public List<string[]> Rows { get; set; } = new();
    public List<KeyValuePair<string, string>> Summary { get; set; } = new();

    /// <summary>Zero-based indexes of the columns that hold plain numbers. Excel writes these as real
    /// numeric cells (2dp, right-aligned) so the sheet can total them; every other column stays text.
    /// Empty = the whole table is text, which is how the non-financial reports render.</summary>
    public int[] NumericColumns { get; set; } = Array.Empty<int>();

    /// <summary>Relative PDF column widths, one entry per header. Null = every column equal. Set it on
    /// wide reports so identifier columns (GSTIN, customer name) aren't squeezed to nothing.</summary>
    public float[]? ColumnWeights { get; set; }
}
