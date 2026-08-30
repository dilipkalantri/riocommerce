namespace RioCommerce.Core.DTOs.Sharing;

/// <summary>
/// Result of a bulk Excel import operation. Imports are best-effort per row — valid rows
/// land, invalid rows surface in <see cref="Errors"/> with row numbers so the admin can fix
/// the spreadsheet and re-upload.
/// </summary>
public class SharingImportResult
{
    public int TotalRows { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<SharingImportError> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
}

public class SharingImportError
{
    /// <summary>1-based row number from the spreadsheet (header is row 1, first data row is 2).</summary>
    public int RowNumber { get; set; }
    public string Column { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>The raw cell value that caused the error (for "did you mean…" hints).</summary>
    public string? Value { get; set; }
}
