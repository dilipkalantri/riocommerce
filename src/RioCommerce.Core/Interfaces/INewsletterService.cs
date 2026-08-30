using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

public interface INewsletterService
{
    /// <summary>Public signup — upserts the email and reactivates a previously-unsubscribed address.</summary>
    Task<(bool ok, string message)> SubscribeAsync(SubscribeRequest request);

    Task<NewsletterStats> StatsAsync();
    Task<List<NewsletterSubscriberItem>> ListSubscribersAsync();
    Task ToggleSubscriberAsync(Guid id);
    Task RemoveSubscriberAsync(Guid id);

    Task<List<CampaignItem>> ListCampaignsAsync();
    Task<CampaignEditModel?> GetCampaignAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveCampaignAsync(CampaignEditModel model);
    Task<(bool ok, string? error)> DeleteCampaignAsync(Guid id);
    /// <summary>Marks the campaign sent and snapshots the active-subscriber recipient count (real delivery = Phase 14).</summary>
    Task<(bool ok, string? error, int recipients)> SendCampaignAsync(Guid id);
}
