using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

public class EducationMasterService(RioCommerceDbContext db) : IEducationMasterService
{
    private readonly RioCommerceDbContext _db = db;

    public async Task<List<BoardItem>> ListActiveBoardsAsync() =>
        await _db.Boards.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BoardItem(b.Id, b.Name))
            .ToListAsync();

    public async Task<EducationValidation> ValidateSelectionAsync(
        Guid? stateId, Guid? districtId, Guid? schoolId, Guid? boardId, string? studentClass,
        Guid? talukaId = null, string? otherSchoolName = null)
    {
        static EducationValidation Fail(string error) =>
            new(false, error, null, null, null, null, null, null);

        if (stateId is not { } sid) return Fail("Please select your state.");
        if (districtId is not { } did) return Fail("Please select your district.");
        if (talukaId is not { } tkId) return Fail("Please select your taluka.");
        if (boardId is not { } bId) return Fail("Please select your board.");

        // "Other" = the student could not find their school. A typed name is accepted INSTEAD of a
        // SchoolId, never as a way around it: schoolId must be absent, so a request carrying both
        // cannot use the free text to dodge the master-data checks below.
        var typedName = string.IsNullOrWhiteSpace(otherSchoolName) ? null : otherSchoolName.Trim();
        var isOther = schoolId is null && typedName is not null;

        if (!isOther && schoolId is null)
            return Fail("Please select your school from the list, or choose Other and type its name.");

        var state = await _db.States.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid);
        if (state is null) return Fail("The selected state is not recognised.");

        // The district must belong to the state that was submitted — not merely exist.
        var district = await _db.Districts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == did);
        if (district is null) return Fail("The selected district is not recognised.");
        if (district.StateId != sid)
            return Fail("The selected district does not belong to the selected state.");

        // The taluka must belong to the district that was submitted — not merely exist.
        var taluka = await _db.Talukas.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tkId);
        if (taluka is null) return Fail("The selected taluka is not recognised.");
        if (taluka.DistrictId != did)
            return Fail("The selected taluka does not belong to the selected district.");

        var board = await _db.Boards.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bId);
        if (board is null) return Fail("The selected board is not recognised.");
        if (!board.IsActive) return Fail("The selected board is no longer available.");

        if (string.IsNullOrWhiteSpace(studentClass) || !int.TryParse(studentClass.Trim(), out var cls))
            return Fail("Please select your class / standard.");

        // ── "Other": no school master row to validate against ────────────────────────────────
        // The class-range check is skipped because there is no school to read a range from; the
        // geography chain above has already been verified, so the record is still coherent.
        if (isOther)
        {
            if (typedName!.Length < 3)
                return Fail("Please enter your school's name.");

            return new EducationValidation(
                true, null,
                null, typedName,               // SchoolId stays NULL — nothing is invented
                board.Id, board.Name,
                state.Name, district.Name,
                taluka.Id, taluka.Name);
        }

        // The school must be active and sit on the EXACT chain that was submitted. All three
        // levels are re-read from the school's own row, so a browser that posts a valid SchoolId
        // with a mismatched state/district/taluka is refused rather than quietly re-homed.
        var school = await _db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Id == schoolId!.Value);
        if (school is null) return Fail("The selected school is not recognised.");
        if (!school.IsActive) return Fail("The selected school is no longer available.");
        if (school.StateId != sid)
            return Fail("The selected school does not belong to the selected state.");
        if (school.DistrictId != did)
            return Fail("The selected school does not belong to the selected district.");

        // Strict, and NULL is a failure rather than a wildcard: this mirrors the picker exactly.
        // A school with no taluka recorded cannot be listed under any taluka, so it must not be
        // accepted under one either — otherwise a hand-crafted request could attach a school the
        // UI would never have offered. Those students use "Other" until the import backfills.
        if (school.TalukaId != tkId)
            return Fail("The selected school does not belong to the selected taluka.");

        // The school's own imported range is the authority. Zero/absent bounds fall back to
        // 1-12 so a sparsely imported row cannot reject an otherwise valid signup.
        var lo = school.LowestClass > 0 ? school.LowestClass : 1;
        var hi = school.HighestClass > 0 ? Math.Max(school.HighestClass, lo) : 12;
        if (cls < lo || cls > hi)
            return Fail($"{school.Name} teaches classes {lo}–{hi}. Please pick a class in that range.");

        return new EducationValidation(
            true, null,
            school.Id, school.Name,
            board.Id, board.Name,
            state.Name, district.Name,
            taluka.Id, taluka.Name);
    }
}
