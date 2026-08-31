using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

public class GeographyService(RioCommerceDbContext db) : IGeographyService
{
    private readonly RioCommerceDbContext _db = db;

    public async Task<List<StateItem>> ListStatesAsync()
    {
        return await _db.States
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new StateItem(s.Id, s.Name, s.Code, s.IsActive, s.Districts.Count))
            .ToListAsync();
    }

    public async Task<List<DistrictItem>> ListDistrictsAsync(Guid? stateId = null)
    {
        var q = _db.Districts.AsNoTracking().Include(d => d.State).AsQueryable();
        if (stateId.HasValue) q = q.Where(d => d.StateId == stateId.Value);

        return await q
            .OrderBy(d => d.Name)
            .Select(d => new DistrictItem(d.Id, d.Name, d.StateId, d.State.Name, d.IsActive, d.Talukas.Count))
            .ToListAsync();
    }

    public async Task<List<TalukaItem>> ListTalukasAsync(Guid? districtId = null)
    {
        var q = _db.Talukas.AsNoTracking().Include(t => t.District).AsQueryable();
        if (districtId.HasValue) q = q.Where(t => t.DistrictId == districtId.Value);

        return await q
            .OrderBy(t => t.Name)
            .Select(t => new TalukaItem(t.Id, t.Name, t.DistrictId, t.District.Name, t.IsActive))
            .ToListAsync();
    }

    public async Task<(bool ok, string? error, Guid id)> SaveDistrictAsync(DistrictEditModel model)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(model.Name))
            return (false, "Name is required.", Guid.Empty);

        var dup = await _db.Districts.AnyAsync(d =>
            d.StateId == model.StateId && d.Name == model.Name && (model.Id == null || d.Id != model.Id));
        if (dup) return (false, $"District '{model.Name}' already exists in this state.", Guid.Empty);

        if (model.Id is { } existingId)
        {
            var entity = await _db.Districts.FindAsync(existingId);
            if (entity == null) return (false, "District not found.", Guid.Empty);
            entity.Name = model.Name.Trim();
            entity.StateId = model.StateId;
            entity.IsActive = model.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, null, existingId);
        }
        else
        {
            var entity = new District { Name = model.Name.Trim(), StateId = model.StateId, IsActive = model.IsActive };
            _db.Districts.Add(entity);
            await _db.SaveChangesAsync();
            return (true, null, entity.Id);
        }
    }

    public async Task<(bool ok, string? error, Guid id)> SaveTalukaAsync(TalukaEditModel model)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(model.Name))
            return (false, "Name is required.", Guid.Empty);

        var dup = await _db.Talukas.AnyAsync(t =>
            t.DistrictId == model.DistrictId && t.Name == model.Name && (model.Id == null || t.Id != model.Id));
        if (dup) return (false, $"Taluka '{model.Name}' already exists in this district.", Guid.Empty);

        if (model.Id is { } existingId)
        {
            var entity = await _db.Talukas.FindAsync(existingId);
            if (entity == null) return (false, "Taluka not found.", Guid.Empty);
            entity.Name = model.Name.Trim();
            entity.DistrictId = model.DistrictId;
            entity.IsActive = model.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, null, existingId);
        }
        else
        {
            var entity = new Taluka { Name = model.Name.Trim(), DistrictId = model.DistrictId, IsActive = model.IsActive };
            _db.Talukas.Add(entity);
            await _db.SaveChangesAsync();
            return (true, null, entity.Id);
        }
    }

    public async Task<(bool ok, string? error)> DeleteDistrictAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var entity = await _db.Districts.Include(d => d.Talukas).FirstOrDefaultAsync(d => d.Id == id);
        if (entity == null) return (false, "Not found.");
        if (entity.Talukas.Count > 0) return (false, $"Cannot delete — {entity.Talukas.Count} taluka(s) reference this district.");
        var schoolCount = await _db.Schools.CountAsync(s => s.DistrictId == id);
        if (schoolCount > 0) return (false, $"Cannot delete — {schoolCount} school(s) reference this district.");
        _db.Districts.Remove(entity);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteTalukaAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var entity = await _db.Talukas.FindAsync(id);
        if (entity == null) return (false, "Not found.");
        var schoolCount = await _db.Schools.CountAsync(s => s.TalukaId == id);
        if (schoolCount > 0) return (false, $"Cannot delete — {schoolCount} school(s) reference this taluka.");
        _db.Talukas.Remove(entity);
        await _db.SaveChangesAsync();
        return (true, null);
    }
}
