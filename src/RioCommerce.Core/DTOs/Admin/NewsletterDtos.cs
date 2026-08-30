namespace RioCommerce.Core.DTOs.Admin;

public record SubscribeRequest(string Email, string? Name, string? Source);

public record NewsletterSubscriberItem(Guid Id, string Email, string? Name, string? Source, bool IsActive, DateTime CreatedAt);

public record CampaignItem(Guid Id, string Name, string Subject, bool IsSent, int RecipientCount, DateTime? SentAt, DateTime CreatedAt);

public class CampaignEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public record NewsletterStats(int Subscribers, int ActiveSubscribers, int Campaigns, int Sent);
