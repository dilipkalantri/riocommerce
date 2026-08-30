namespace RioCommerce.Core.DTOs.SerialKeys;

/// <summary>Admin-facing view of a synced Valence pack (for the product config picker and
/// the settings-page pack list).</summary>
public class ValencePackItem
{
    public int ExternalId { get; set; }
    public string PackName { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

/// <summary>Result of a manual pack sync.</summary>
public class ValencePackSyncResult
{
    public bool Success { get; set; }
    public int Fetched { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deactivated { get; set; }
    public string? ErrorMessage { get; set; }

    public static ValencePackSyncResult Fail(string message)
        => new() { Success = false, ErrorMessage = message };
}
