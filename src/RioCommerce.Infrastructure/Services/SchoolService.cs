using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

public class SchoolService(RioCommerceDbContext db) : ISchoolService
{
    private readonly RioCommerceDbContext _db = db;

    public async Task<List<SchoolListItem>> ListAsync(string? search = null, Guid? districtId = null)
    {
        var q = _db.Schools.AsNoTracking()
            .Include(s => s.Taluka)
            .Include(s => s.District)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(s => s.Name.ToLower().Contains(term)
                           || s.UdiseCode.ToLower().Contains(term)
                           || (s.CityOrVillage != null && s.CityOrVillage.ToLower().Contains(term)));
        }
        if (districtId.HasValue)
            q = q.Where(s => s.DistrictId == districtId.Value);

        return await q
            .OrderBy(s => s.Name)
            .Select(s => new SchoolListItem(
                s.Id, s.UdiseCode, s.Name, s.SchoolType,
                s.LowestClass, s.HighestClass,
                s.CityOrVillage,
                s.Taluka != null ? s.Taluka.Name : null,
                s.District != null ? s.District.Name : null,
                s.IsActive,
                s.SchoolUsers.Count))
            .ToListAsync();
    }

    public async Task<SchoolEditModel?> GetAsync(Guid id)
    {
        var s = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;
        return new SchoolEditModel
        {
            Id = s.Id, UdiseCode = s.UdiseCode, Name = s.Name, Address = s.Address,
            SchoolType = s.SchoolType, LowestClass = s.LowestClass, HighestClass = s.HighestClass,
            CityOrVillage = s.CityOrVillage, TalukaId = s.TalukaId, DistrictId = s.DistrictId,
            StateId = s.StateId, PinCode = s.PinCode, IsActive = s.IsActive
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(SchoolEditModel model)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(model.UdiseCode))
            return (false, "UDISE code is required.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(model.Name))
            return (false, "School name is required.", Guid.Empty);

        var udise = model.UdiseCode.Trim();
        var dup = await _db.Schools.AnyAsync(s =>
            s.UdiseCode == udise && (model.Id == null || s.Id != model.Id));
        if (dup) return (false, $"A school with UDISE code '{udise}' already exists.", Guid.Empty);

        if (model.Id is { } existingId)
        {
            var entity = await _db.Schools.FindAsync(existingId);
            if (entity == null) return (false, "School not found.", Guid.Empty);
            MapToEntity(model, entity);
            entity.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (true, null, existingId);
        }
        else
        {
            var entity = new School();
            MapToEntity(model, entity);
            _db.Schools.Add(entity);
            await _db.SaveChangesAsync();
            return (true, null, entity.Id);
        }
    }

    public async Task<(bool ok, string? error)> ToggleAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var entity = await _db.Schools.FindAsync(id);
        if (entity == null) return (false, "Not found.");
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var entity = await _db.Schools.FindAsync(id);
        if (entity == null) return (false, "Not found.");
        var userCount = await _db.SchoolUsers.CountAsync(su => su.SchoolId == id);
        if (userCount > 0) return (false, $"Cannot delete — {userCount} user(s) are linked to this school.");
        _db.Schools.Remove(entity);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<SchoolEditModel?> FindByUdiseAsync(string udiseCode)
    {
        var s = await _db.Schools.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UdiseCode == udiseCode.Trim());
        if (s == null) return null;
        return new SchoolEditModel
        {
            Id = s.Id, UdiseCode = s.UdiseCode, Name = s.Name, Address = s.Address,
            SchoolType = s.SchoolType, LowestClass = s.LowestClass, HighestClass = s.HighestClass,
            CityOrVillage = s.CityOrVillage, TalukaId = s.TalukaId, DistrictId = s.DistrictId,
            StateId = s.StateId, PinCode = s.PinCode, IsActive = s.IsActive
        };
    }

    public async Task<SchoolImportResult> ImportFromExcelAsync(Stream excelStream)
    {
        _db.ChangeTracker.Clear();
        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheets.First();
        var rows = ws.RowsUsed().Skip(1).ToList();

        var districts = await _db.Districts.AsNoTracking().ToListAsync();
        var talukas = await _db.Talukas.AsNoTracking().Include(t => t.District).ToListAsync();
        var states = await _db.States.AsNoTracking().ToListAsync();
        var mhState = states.FirstOrDefault(s => s.Code == "MH");

        int created = 0, updated = 0, errors = 0;
        var errorMessages = new List<string>();

        foreach (var row in rows)
        {
            var rowNum = row.RowNumber();
            var udise = row.Cell(1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(udise)) { errors++; errorMessages.Add($"Row {rowNum}: UDISE code is empty."); continue; }

            var name = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name)) { errors++; errorMessages.Add($"Row {rowNum}: School name is empty."); continue; }

            var address = row.Cell(3).GetString().Trim();
            var typeStr = row.Cell(4).GetString().Trim();
            if (!Enum.TryParse<SchoolType>(typeStr, true, out var schoolType))
                schoolType = SchoolType.Combined;

            int.TryParse(row.Cell(5).GetString().Trim(), out var lowestClass);
            int.TryParse(row.Cell(6).GetString().Trim(), out var highestClass);
            if (lowestClass == 0) lowestClass = 1;
            if (highestClass == 0) highestClass = 10;

            var cityOrVillage = row.Cell(7).GetString().Trim();
            var talukaName = row.Cell(8).GetString().Trim();
            var districtName = row.Cell(9).GetString().Trim();

            Guid? districtId = null, talukaId = null, stateId = mhState?.Id;
            if (!string.IsNullOrWhiteSpace(districtName))
            {
                var dist = districts.FirstOrDefault(d =>
                    d.Name.Equals(districtName, StringComparison.OrdinalIgnoreCase));
                if (dist != null)
                {
                    districtId = dist.Id;
                    if (!string.IsNullOrWhiteSpace(talukaName))
                    {
                        var tal = talukas.FirstOrDefault(t =>
                            t.DistrictId == dist.Id && t.Name.Equals(talukaName, StringComparison.OrdinalIgnoreCase));
                        talukaId = tal?.Id;
                    }
                }
            }

            var existing = await _db.Schools.FirstOrDefaultAsync(s => s.UdiseCode == udise);
            if (existing != null)
            {
                existing.Name = name;
                existing.Address = string.IsNullOrWhiteSpace(address) ? existing.Address : address;
                existing.SchoolType = schoolType;
                existing.LowestClass = lowestClass;
                existing.HighestClass = highestClass;
                existing.CityOrVillage = string.IsNullOrWhiteSpace(cityOrVillage) ? existing.CityOrVillage : cityOrVillage;
                existing.TalukaId = talukaId ?? existing.TalukaId;
                existing.DistrictId = districtId ?? existing.DistrictId;
                existing.StateId = stateId ?? existing.StateId;
                existing.UpdatedAt = DateTime.UtcNow;
                updated++;
            }
            else
            {
                _db.Schools.Add(new School
                {
                    UdiseCode = udise, Name = name, Address = address,
                    SchoolType = schoolType, LowestClass = lowestClass, HighestClass = highestClass,
                    CityOrVillage = cityOrVillage, TalukaId = talukaId, DistrictId = districtId,
                    StateId = stateId, PinCode = row.Cell(10).GetString().Trim()
                });
                created++;
            }
        }

        await _db.SaveChangesAsync();
        return new SchoolImportResult(created, updated, errors, errorMessages);
    }

    private static void MapToEntity(SchoolEditModel model, School entity)
    {
        entity.UdiseCode = model.UdiseCode.Trim();
        entity.Name = model.Name.Trim();
        entity.Address = model.Address?.Trim();
        entity.SchoolType = model.SchoolType;
        entity.LowestClass = model.LowestClass;
        entity.HighestClass = model.HighestClass;
        entity.CityOrVillage = model.CityOrVillage?.Trim();
        entity.TalukaId = model.TalukaId;
        entity.DistrictId = model.DistrictId;
        entity.StateId = model.StateId;
        entity.PinCode = model.PinCode?.Trim();
        entity.IsActive = model.IsActive;
    }
}
