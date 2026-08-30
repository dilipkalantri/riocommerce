namespace RioCommerce.Core.Entities;
public class NewsletterSubscriber : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Source { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UnsubscribedAt { get; set; }
}
