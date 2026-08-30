namespace RioCommerce.Core.Entities;

// Per-user key→JSON preferences (grid layouts, saved filters, page size, notification read marker, …).
public class UserPreference : BaseEntity
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
}
