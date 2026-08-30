using RioCommerce.Core.DTOs.Referral;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class ReferralAdminService : IReferralAdminService
{
    private readonly RioCommerceDbContext _db;
    public ReferralAdminService(RioCommerceDbContext db) => _db = db;

    public Task<List<ReferralSourceAdminItem>> ListAsync() =>
        _db.ReferralSources
            .OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name)
            .Select(r => new ReferralSourceAdminItem(r.Id, r.Name, r.Type, r.DisplayOrder, r.IsActive, r.IsDefault, r.ColorBadge, r.CreatedAt))
            .ToListAsync();

    public async Task<ReferralSourceEditModel?> GetAsync(Guid id)
    {
        var r = await _db.ReferralSources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return r == null ? null : new ReferralSourceEditModel
        {
            Id = r.Id, Name = r.Name, Type = r.Type, DisplayOrder = r.DisplayOrder,
            IsActive = r.IsActive, IsDefault = r.IsDefault,
            ColorBadge = r.ColorBadge, Icon = r.Icon, Description = r.Description
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(ReferralSourceEditModel m, Guid? actorId)
    {
        _db.ChangeTracker.Clear();   // self-contained write — never flush stale circuit state
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);
        var name = m.Name.Trim();

        // Duplicate name check (case-insensitive), excluding the row being edited.
        var duplicate = await _db.ReferralSources.AnyAsync(x => x.Id != (m.Id ?? Guid.Empty) && x.Name.ToLower() == name.ToLower());
        if (duplicate) return (false, "Another referral source already uses that name.", Guid.Empty);

        ReferralSource entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.ReferralSources.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("Referral source not found.");
            entity.UpdatedById = actorId;
        }
        else
        {
            entity = new ReferralSource { CreatedById = actorId };
            _db.ReferralSources.Add(entity);
        }

        entity.Name = name;
        entity.Type = m.Type;
        entity.DisplayOrder = m.DisplayOrder;
        entity.IsActive = m.IsActive;
        entity.IsDefault = m.IsDefault;
        entity.ColorBadge = Clean(m.ColorBadge);
        entity.Icon = Clean(m.Icon);
        entity.Description = Clean(m.Description);

        // Only one default allowed — unflag the others when this one becomes default.
        if (entity.IsDefault)
        {
            await _db.ReferralSources.Where(x => x.Id != entity.Id && x.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        }

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task ToggleAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var r = await _db.ReferralSources.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return;
        r.IsActive = !r.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        // Soft delete — orders that reference this option keep their snapshot intact.
        var r = await _db.ReferralSources.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return (false, "Referral source not found.");
        r.IsDeleted = true;
        r.DeletedAt = DateTime.UtcNow;
        r.IsActive = false;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public class ReferralService : IReferralService
{
    private readonly RioCommerceDbContext _db;
    public ReferralService(RioCommerceDbContext db) => _db = db;

    public Task<List<ReferralSourceOption>> ListActiveAsync() =>
        _db.ReferralSources.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name)
            .Select(r => new ReferralSourceOption(r.Id, r.Name, r.Type, r.Icon, r.ColorBadge))
            .ToListAsync();
}
