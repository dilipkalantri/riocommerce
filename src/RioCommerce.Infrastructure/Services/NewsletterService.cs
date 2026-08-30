using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

public class NewsletterService : INewsletterService
{
    private readonly RioCommerceDbContext _db;
    private readonly ILogger<NewsletterService> _log;
    public NewsletterService(RioCommerceDbContext db, ILogger<NewsletterService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<(bool ok, string message)> SubscribeAsync(SubscribeRequest req)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || !email.Contains('.'))
            return (false, "Please enter a valid email address.");

        var existing = await _db.NewsletterSubscribers.FirstOrDefaultAsync(s => s.Email == email);
        if (existing != null)
        {
            if (existing.IsActive) return (true, "You're already subscribed 🎉");
            existing.IsActive = true;
            existing.UnsubscribedAt = null;
            await _db.SaveChangesAsync();
            return (true, "Welcome back — you're subscribed again!");
        }

        _db.NewsletterSubscribers.Add(new NewsletterSubscriber
        {
            Email = email,
            Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim(),
            Source = string.IsNullOrWhiteSpace(req.Source) ? "website" : req.Source.Trim(),
            IsActive = true
        });
        await _db.SaveChangesAsync();
        return (true, "Thanks for subscribing! 🎉");
    }

    public async Task<NewsletterStats> StatsAsync() => new(
        await _db.NewsletterSubscribers.CountAsync(),
        await _db.NewsletterSubscribers.CountAsync(s => s.IsActive),
        await _db.Campaigns.CountAsync(),
        await _db.Campaigns.CountAsync(c => c.IsSent));

    public async Task<List<NewsletterSubscriberItem>> ListSubscribersAsync() =>
        await _db.NewsletterSubscribers.OrderByDescending(s => s.CreatedAt)
            .Select(s => new NewsletterSubscriberItem(s.Id, s.Email, s.Name, s.Source, s.IsActive, s.CreatedAt))
            .ToListAsync();

    public async Task ToggleSubscriberAsync(Guid id)
    {
        var s = await _db.NewsletterSubscribers.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return;
        s.IsActive = !s.IsActive;
        s.UnsubscribedAt = s.IsActive ? null : DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RemoveSubscriberAsync(Guid id)
    {
        var s = await _db.NewsletterSubscribers.FirstOrDefaultAsync(x => x.Id == id);
        if (s != null) { _db.NewsletterSubscribers.Remove(s); await _db.SaveChangesAsync(); }
    }

    public async Task<List<CampaignItem>> ListCampaignsAsync() =>
        await _db.Campaigns.OrderByDescending(c => c.CreatedAt)
            .Select(c => new CampaignItem(c.Id, c.Name, c.Subject, c.IsSent, c.RecipientCount, c.SentAt, c.CreatedAt))
            .ToListAsync();

    public async Task<CampaignEditModel?> GetCampaignAsync(Guid id)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
        return c == null ? null : new CampaignEditModel { Id = c.Id, Name = c.Name, Subject = c.Subject, Body = c.Body };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveCampaignAsync(CampaignEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Campaign name is required.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(m.Subject)) return (false, "Subject is required.", Guid.Empty);

        Campaign entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("Campaign not found.");
            if (entity.IsSent) return (false, "A sent campaign can't be edited.", Guid.Empty);
        }
        else
        {
            entity = new Campaign();
            _db.Campaigns.Add(entity);
        }

        entity.Name = m.Name.Trim();
        entity.Subject = m.Subject.Trim();
        entity.Body = m.Body?.Trim() ?? "";
        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task<(bool ok, string? error)> DeleteCampaignAsync(Guid id)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Campaign not found.");
        _db.Campaigns.Remove(c);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error, int recipients)> SendCampaignAsync(Guid id)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Campaign not found.", 0);
        if (c.IsSent) return (false, "This campaign has already been sent.", 0);

        var recipients = await _db.NewsletterSubscribers.CountAsync(s => s.IsActive);
        c.IsSent = true;
        c.SentAt = DateTime.UtcNow;
        c.RecipientCount = recipients;
        await _db.SaveChangesAsync();

        // Real delivery is wired in Phase 14 (notification engine); for now we log the dispatch.
        _log.LogInformation("📣 [stub] Campaign \"{Name}\" queued to {Recipients} active subscriber(s)", c.Name, recipients);
        return (true, null, recipients);
    }
}
