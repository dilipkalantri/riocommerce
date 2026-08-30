using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class SiteSettingsService : ISiteSettingsService
{
    private const string Cat = "site";
    private readonly RioCommerceDbContext _db;
    private readonly SettingsCache _cache;
    private readonly IPublicFileStorage _files;
    public SiteSettingsService(RioCommerceDbContext db, SettingsCache cache, IPublicFileStorage files)
    { _db = db; _cache = cache; _files = files; }

    public async Task<SiteSettings> GetAsync()
    {
        var all = await _db.AppSettings
            .Where(s => s.Category == Cat || s.Category == "general")
            .ToListAsync();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in all.OrderBy(s => s.Category == Cat ? 1 : 0))
            map[s.Key] = s.Value;
        string? V(string k) => map.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        return new SiteSettings
        {
            CompanyName = V("company_name"),
            WebsiteUrl = V("website_url"),
            CopyrightText = V("copyright_text"),
            LogoUrl = V("logo_url"),
            ContactPhone1 = V("company_phone_1"),
            ContactPhone2 = V("company_phone_2"),
            ContactPhone3 = V("company_phone_3"),
            ContactEmail = V("company_email"),
            SupportEmail = V("support_email"),
            Address = V("company_address"),
            GoogleMapsUrl = V("google_maps_url"),
            BusinessHours = V("business_hours"),
            WhatsappNumber = V("whatsapp_number"),
            WhatsappMessage = V("whatsapp_message"),
            InstagramUrl = V("instagram_url"),
            YoutubeUrl = V("youtube_url"),
            TelegramUrl = V("telegram_url"),
            Ga4Id = V("ga4_id"),
            GtmId = V("gtm_id"),
            MetaPixelId = V("meta_pixel_id"),
            AnnouncementText = V("announcement_text"),
            FomoEnabled = V("fomo_enabled") != "false",
        };
    }

    public async Task SaveAsync(SiteSettings s)
    {
        await UpsertAsync("company_name", s.CompanyName);
        await UpsertAsync("website_url", s.WebsiteUrl);
        await UpsertAsync("copyright_text", s.CopyrightText);
        await UpsertAsync("logo_url", s.LogoUrl);
        await UpsertAsync("company_phone_1", s.ContactPhone1);
        await UpsertAsync("company_phone_2", s.ContactPhone2);
        await UpsertAsync("company_phone_3", s.ContactPhone3);
        await UpsertAsync("company_email", s.ContactEmail);
        await UpsertAsync("support_email", s.SupportEmail);
        await UpsertAsync("company_address", s.Address);
        await UpsertAsync("google_maps_url", s.GoogleMapsUrl);
        await UpsertAsync("business_hours", s.BusinessHours);
        await UpsertAsync("whatsapp_number", s.WhatsappNumber);
        await UpsertAsync("whatsapp_message", s.WhatsappMessage);
        await UpsertAsync("instagram_url", s.InstagramUrl);
        await UpsertAsync("youtube_url", s.YoutubeUrl);
        await UpsertAsync("telegram_url", s.TelegramUrl);
        await UpsertAsync("ga4_id", s.Ga4Id);
        await UpsertAsync("gtm_id", s.GtmId);
        await UpsertAsync("meta_pixel_id", s.MetaPixelId);
        await UpsertAsync("announcement_text", s.AnnouncementText);
        await UpsertAsync("fomo_enabled", s.FomoEnabled ? "true" : "false");
        await _db.SaveChangesAsync();
        _cache.Clear();
    }

    public async Task<(bool ok, string? error, string? url)> UploadLogoAsync(string extension, Stream content)
    {
        var ext = (extension ?? "").Trim().ToLowerInvariant().TrimStart('.');
        if (ext is not ("png" or "jpg" or "jpeg" or "webp" or "svg"))
            return (false, "Unsupported file type. Use PNG, JPG, WEBP or SVG.", null);

        // Delete the previous file so we don't leave orphans in storage.
        var prev = (await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "logo_url"))?.Value;
        if (!string.IsNullOrWhiteSpace(prev)) { try { _files.Delete(prev); } catch { } }

        try
        {
            var url = await _files.SaveAsync("site", ext, content);
            await UpsertAsync("logo_url", url);
            await _db.SaveChangesAsync();
            _cache.Clear();
            return (true, null, url);
        }
        catch (Exception ex)
        {
            return (false, "Upload failed: " + ex.Message, null);
        }
    }

    public async Task DeleteLogoAsync()
    {
        var prev = (await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == "logo_url"))?.Value;
        if (!string.IsNullOrWhiteSpace(prev)) { try { _files.Delete(prev); } catch { } }
        await UpsertAsync("logo_url", null);
        await _db.SaveChangesAsync();
        _cache.Clear();
    }

    private async Task UpsertAsync(string key, string? value)
    {
        var row = await _db.AppSettings.FirstOrDefaultAsync(a => a.Key == key);
        if (row == null)
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value ?? "", Category = Cat, UpdatedAt = DateTime.UtcNow });
        else { row.Value = value ?? ""; row.Category = Cat; row.UpdatedAt = DateTime.UtcNow; }
    }
}
