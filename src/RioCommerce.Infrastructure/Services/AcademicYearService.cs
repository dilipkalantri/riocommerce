using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

public class AcademicYearService(RioCommerceDbContext db) : IAcademicYearService
{
    private readonly RioCommerceDbContext _db = db;

    public async Task<List<AcademicYearItem>> ListAsync()
    {
        return await _db.AcademicYears
            .AsNoTracking()
            .OrderByDescending(a => a.StartDate)
            .Select(a => new AcademicYearItem(a.Id, a.Name, a.StartDate, a.EndDate, a.IsCurrent, a.IsActive))
            .ToListAsync();
    }

    public async Task<AcademicYearEditModel?> GetAsync(Guid id)
    {
        var a = await _db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return null;
        return new AcademicYearEditModel
        {
            Id = a.Id, Name = a.Name, StartDate = a.StartDate,
            EndDate = a.EndDate, IsCurrent = a.IsCurrent, IsActive = a.IsActive
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(AcademicYearEditModel model)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(model.Name))
            return (false, "Name is required.", Guid.Empty);

        var dup = await _db.AcademicYears.AnyAsync(a =>
            a.Name == model.Name && (model.Id == null || a.Id != model.Id));
        if (dup) return (false, $"Academic year '{model.Name}' already exists.", Guid.Empty);

        if (model.Id is { } existingId)
        {
            var entity = await _db.AcademicYears.FindAsync(existingId);
            if (entity == null) return (false, "Academic year not found.", Guid.Empty);
            entity.Name = model.Name.Trim();
            entity.StartDate = model.StartDate;
            entity.EndDate = model.EndDate;
            entity.IsCurrent = model.IsCurrent;
            entity.IsActive = model.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;

            if (model.IsCurrent)
                await ClearOtherCurrent(existingId);

            await _db.SaveChangesAsync();
            return (true, null, existingId);
        }
        else
        {
            var entity = new AcademicYear
            {
                Name = model.Name.Trim(),
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                IsCurrent = model.IsCurrent,
                IsActive = model.IsActive
            };
            _db.AcademicYears.Add(entity);

            if (model.IsCurrent)
                await ClearOtherCurrent(entity.Id);

            await _db.SaveChangesAsync();
            return (true, null, entity.Id);
        }
    }

    public async Task<(bool ok, string? error)> ToggleAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var entity = await _db.AcademicYears.FindAsync(id);
        if (entity == null) return (false, "Not found.");
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var entity = await _db.AcademicYears.FindAsync(id);
        if (entity == null) return (false, "Not found.");
        if (entity.IsCurrent) return (false, "Cannot delete the current academic year.");
        _db.AcademicYears.Remove(entity);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<AcademicYearItem?> GetCurrentAsync()
    {
        var a = await _db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(x => x.IsCurrent && x.IsActive);
        return a == null ? null : new AcademicYearItem(a.Id, a.Name, a.StartDate, a.EndDate, a.IsCurrent, a.IsActive);
    }

    private async Task ClearOtherCurrent(Guid exceptId)
    {
        await _db.AcademicYears
            .Where(a => a.IsCurrent && a.Id != exceptId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsCurrent, false)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow));
    }
}
