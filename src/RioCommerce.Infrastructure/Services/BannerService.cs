using RioCommerce.Core.DTOs.Banners;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class BannerService : IBannerService
{
    private readonly RioCommerceDbContext _db;
    private readonly IPublicFileStorage _files;

    public BannerService(RioCommerceDbContext db, IPublicFileStorage files)
    {
        _db = db;
        _files = files;
    }

    // Effective desktop image: the new field, falling back to the legacy single ImageUrl.
    private static string DesktopOf(Banner b) =>
        !string.IsNullOrWhiteSpace(b.DesktopImageUrl) ? b.DesktopImageUrl! : b.ImageUrl;

    // Effective mobile image: the mobile field, falling back to the desktop image.
    private static string MobileOf(Banner b)
    {
        var desktop = DesktopOf(b);
        return !string.IsNullOrWhiteSpace(b.MobileImageUrl) ? b.MobileImageUrl! : desktop;
    }

    private static bool InWindow(Banner b, DateTime nowUtc) =>
        (b.StartDate is null || b.StartDate <= nowUtc) &&
        (b.EndDate is null || b.EndDate >= nowUtc);

    // ── Admin: list & edit ──────────────────────────────────────────────────
    public async Task<List<BannerAdminItem>> ListAsync(string placement = "homepage")
    {
        var now = DateTime.UtcNow;
        var rows = await _db.Banners
            .Where(b => b.Placement == placement)
            .OrderBy(b => b.DisplayOrder).ThenBy(b => b.CreatedAt)
            .ToListAsync();

        return rows.Select(b => new BannerAdminItem
        {
            Id = b.Id,
            Name = string.IsNullOrWhiteSpace(b.Name) ? (b.Title ?? "Banner") : b.Name,
            Title = b.Title,
            DesktopImageUrl = DesktopOf(b),
            MobileImageUrl = MobileOf(b),
            LinkUrl = b.LinkUrl,
            DisplayOrder = b.DisplayOrder,
            StartDate = b.StartDate,
            EndDate = b.EndDate,
            IsActive = b.IsActive,
            IsLive = b.IsActive && InWindow(b, now)
        }).ToList();
    }

    public async Task<BannerEditModel?> GetAsync(Guid id)
    {
        var b = await _db.Banners.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return null;
        return new BannerEditModel
        {
            Id = b.Id,
            Name = b.Name,
            DesktopImageUrl = DesktopOf(b),
            MobileImageUrl = b.MobileImageUrl,
            Title = b.Title,
            Description = b.Description,
            ButtonText = b.ButtonText,
            LinkUrl = b.LinkUrl,
            DisplayOrder = b.DisplayOrder,
            StartDate = b.StartDate,
            EndDate = b.EndDate,
            IsActive = b.IsActive
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(BannerEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
            return (false, "Banner name is required.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(model.DesktopImageUrl))
            return (false, "A desktop banner image is required.", Guid.Empty);
        if (model.StartDate is { } s && model.EndDate is { } e && e < s)
            return (false, "End date can't be before the start date.", Guid.Empty);
        // A button with no destination is a dead click — require a link when button text is set.
        if (!string.IsNullOrWhiteSpace(model.ButtonText) && string.IsNullOrWhiteSpace(model.LinkUrl))
            return (false, "Add a Button URL, or clear the button text.", Guid.Empty);

        Banner b;
        if (model.Id is { } id && id != Guid.Empty)
        {
            b = await _db.Banners.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("Banner not found.");
        }
        else
        {
            b = new Banner { Placement = "homepage" };
            // New banners append to the end of the order.
            var max = await _db.Banners.Where(x => x.Placement == "homepage")
                .Select(x => (int?)x.DisplayOrder).MaxAsync() ?? 0;
            b.DisplayOrder = model.DisplayOrder > 0 ? model.DisplayOrder : max + 1;
            _db.Banners.Add(b);
        }

        b.Name = model.Name.Trim();
        b.DesktopImageUrl = model.DesktopImageUrl?.Trim();
        // Keep the legacy column in sync so any older read path still finds an image.
        b.ImageUrl = b.DesktopImageUrl ?? string.Empty;
        b.MobileImageUrl = string.IsNullOrWhiteSpace(model.MobileImageUrl) ? null : model.MobileImageUrl.Trim();
        b.Title = string.IsNullOrWhiteSpace(model.Title) ? null : model.Title.Trim();
        b.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        b.ButtonText = string.IsNullOrWhiteSpace(model.ButtonText) ? null : model.ButtonText.Trim();
        b.LinkUrl = string.IsNullOrWhiteSpace(model.LinkUrl) ? null : model.LinkUrl.Trim();
        if (model.Id is { } && model.DisplayOrder > 0) b.DisplayOrder = model.DisplayOrder;
        b.StartDate = model.StartDate;
        b.EndDate = model.EndDate;
        b.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        return (true, null, b.Id);
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        var b = await _db.Banners.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return (false, "Banner not found.");

        // Best-effort: clean up uploaded artwork so we don't orphan files in storage.
        try { _files.Delete(b.DesktopImageUrl); } catch { }
        try { _files.Delete(b.MobileImageUrl); } catch { }
        if (!string.Equals(b.ImageUrl, b.DesktopImageUrl, StringComparison.Ordinal))
            try { _files.Delete(b.ImageUrl); } catch { }

        _db.Banners.Remove(b);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task ToggleAsync(Guid id)
    {
        var b = await _db.Banners.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return;
        b.IsActive = !b.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds is null || orderedIds.Count == 0) return;
        var rows = await _db.Banners.Where(b => orderedIds.Contains(b.Id)).ToListAsync();
        var byId = rows.ToDictionary(r => r.Id);
        for (var i = 0; i < orderedIds.Count; i++)
            if (byId.TryGetValue(orderedIds[i], out var b))
                b.DisplayOrder = i + 1;
        await _db.SaveChangesAsync();
    }

    // ── Admin: image upload ───────────────────────────────────────────────────
    public async Task<(bool ok, string? error, string? url)> UploadImageAsync(string device, string extension, Stream content)
    {
        var ext = (extension ?? "").Trim().ToLowerInvariant().TrimStart('.');
        if (ext is not ("png" or "jpg" or "jpeg" or "webp"))
            return (false, "Unsupported file type. Use PNG, JPG or WEBP.", null);

        var folder = string.Equals(device, "mobile", StringComparison.OrdinalIgnoreCase)
            ? "bannersmobile" : "banners";
        try
        {
            var url = await _files.SaveAsync(folder, ext, content);
            return (true, null, url);
        }
        catch (Exception ex)
        {
            return (false, "Upload failed: " + ex.Message, null);
        }
    }

    // ── Admin: global slider settings ─────────────────────────────────────────
    public async Task<BannerSliderSettingsModel> GetSettingsAsync()
    {
        var s = await _db.BannerSliderSettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new BannerSliderSettings();
        return new BannerSliderSettingsModel
        {
            Autoplay = s.Autoplay,
            AutoplayIntervalMs = s.AutoplayIntervalMs,
            ShowArrows = s.ShowArrows,
            ShowDots = s.ShowDots,
            PauseOnHover = s.PauseOnHover,
            RecommendedDesktopW = s.RecommendedDesktopW,
            RecommendedDesktopH = s.RecommendedDesktopH,
            RecommendedMobileW = s.RecommendedMobileW,
            RecommendedMobileH = s.RecommendedMobileH
        };
    }

    public async Task SaveSettingsAsync(BannerSliderSettingsModel model)
    {
        var s = await _db.BannerSliderSettings.FirstOrDefaultAsync();
        if (s == null) { s = new BannerSliderSettings(); _db.BannerSliderSettings.Add(s); }

        s.Autoplay = model.Autoplay;
        // Clamp to a sane band so a typo can't freeze the carousel or spin it absurdly fast.
        s.AutoplayIntervalMs = Math.Clamp(model.AutoplayIntervalMs, 1500, 30000);
        s.ShowArrows = model.ShowArrows;
        s.ShowDots = model.ShowDots;
        s.PauseOnHover = model.PauseOnHover;
        s.RecommendedDesktopW = Math.Max(1, model.RecommendedDesktopW);
        s.RecommendedDesktopH = Math.Max(1, model.RecommendedDesktopH);
        s.RecommendedMobileW = Math.Max(1, model.RecommendedMobileW);
        s.RecommendedMobileH = Math.Max(1, model.RecommendedMobileH);
        await _db.SaveChangesAsync();
    }

    // ── Storefront ─────────────────────────────────────────────────────────────
    public async Task<BannerSliderView> GetSliderAsync(string placement = "homepage")
    {
        var now = DateTime.UtcNow;
        var rows = await _db.Banners.AsNoTracking()
            .Where(b => b.Placement == placement && b.IsActive
                && (b.StartDate == null || b.StartDate <= now)
                && (b.EndDate == null || b.EndDate >= now))
            .OrderBy(b => b.DisplayOrder).ThenBy(b => b.CreatedAt)
            .ToListAsync();

        var slides = rows
            .Where(b => !string.IsNullOrWhiteSpace(DesktopOf(b)))
            .Select(b => new BannerSlide(
                b.Id, b.Title, b.Description, b.ButtonText, b.LinkUrl,
                DesktopOf(b), MobileOf(b)))
            .ToList();

        var s = await _db.BannerSliderSettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new BannerSliderSettings();

        return new BannerSliderView(
            slides, s.Autoplay, s.AutoplayIntervalMs, s.ShowArrows, s.ShowDots, s.PauseOnHover);
    }
}
