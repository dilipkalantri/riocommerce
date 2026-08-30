namespace RioCommerce.Core.Entities;
public class Campaign : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsSent { get; set; }
    public int RecipientCount { get; set; }
    public DateTime? SentAt { get; set; }
}
