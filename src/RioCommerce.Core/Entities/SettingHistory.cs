namespace RioCommerce.Core.Entities;

// Append-only change log for AppSetting values — version/history tracking for critical (esp. finance) settings.
// Secret values are masked here so the history never exposes plaintext credentials.
public class SettingHistory : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool WasSecret { get; set; }
    public Guid? ChangedById { get; set; }
    public string ChangedByName { get; set; } = "system";
}
