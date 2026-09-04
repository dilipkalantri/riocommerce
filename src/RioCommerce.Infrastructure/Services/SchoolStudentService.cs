using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RioCommerce.Core.DTOs.School;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// School Portal student management, scoped to the acting user's own school.
///
/// The scoping rule is enforced in exactly one place — <see cref="ResolveSchoolIdAsync"/> —
/// and every public method starts by calling it. Nothing here reads a SchoolId supplied by
/// the caller, so tampering with a request cannot widen access.
/// </summary>
public class SchoolStudentService(
    RioCommerceDbContext db,
    ILogger<SchoolStudentService> log) : ISchoolStudentService
{
    private readonly RioCommerceDbContext _db = db;
    private readonly ILogger<SchoolStudentService> _log = log;

    /// <summary>The single source of truth for "which school may this user touch".</summary>
    public async Task<Guid?> ResolveSchoolIdAsync(Guid actingUserId, CancellationToken ct = default)
    {
        var link = await _db.SchoolUsers.AsNoTracking()
            .Where(su => su.UserId == actingUserId
                      && su.IsActive
                      && (su.Role == SchoolUserRole.Principal || su.Role == SchoolUserRole.Coordinator))
            .Select(su => (Guid?)su.SchoolId)
            .FirstOrDefaultAsync(ct);
        return link;
    }

    public async Task<List<SchoolStudentListItem>> ListAsync(
        Guid actingUserId, string? search = null, CancellationToken ct = default)
    {
        var schoolId = await ResolveSchoolIdAsync(actingUserId, ct);
        // No school -> no rows. Never fall back to "all students".
        if (schoolId is null) return new();

        var q = _db.SchoolStudents.AsNoTracking()
            .Include(ss => ss.User)
            .Include(ss => ss.School)
            .Where(ss => ss.SchoolId == schoolId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            q = q.Where(ss => EF.Functions.ILike(ss.User.FullName, s)
                           || (ss.User.Email != null && EF.Functions.ILike(ss.User.Email, s))
                           || (ss.User.Phone != null && EF.Functions.ILike(ss.User.Phone, s))
                           || (ss.RollNumber != null && EF.Functions.ILike(ss.RollNumber, s)));
        }

        return await q
            .OrderBy(ss => ss.User.FullName)
            .Select(ss => new SchoolStudentListItem(
                ss.Id, ss.UserId, ss.User.FullName, ss.User.Email, ss.User.Phone,
                ss.StudentClass, ss.Section, ss.RollNumber, ss.School.Name,
                ss.IsActive, ss.User.IsVerified, ss.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(Guid actingUserId, CancellationToken ct = default)
    {
        var schoolId = await ResolveSchoolIdAsync(actingUserId, ct);
        if (schoolId is null) return 0;
        return await _db.SchoolStudents.CountAsync(ss => ss.SchoolId == schoolId.Value && ss.IsActive, ct);
    }

    // Two-tier pricing eligibility. A student on a roll OR active staff (Principal / Coordinator)
    // both count — a coordinator seeing the school price on the storefront and their students
    // paying that same price is the intent. Anonymous / direct-registered users are false.
    public async Task<bool> IsSchoolLinkedAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty) return false;
        if (await _db.SchoolStudents.AnyAsync(s => s.UserId == userId && s.IsActive, ct)) return true;
        return await _db.SchoolUsers.AnyAsync(su => su.UserId == userId && su.IsActive, ct);
    }

    public async Task<BulkAddSchoolStudentResult> AddManyAsync(
        Guid actingUserId, BulkAddSchoolStudentRequest request, CancellationToken ct = default)
    {
        var results = new List<BulkAddSchoolStudentRowResult>();
        var rows = request.Rows ?? new();

        // Cap the batch so a malicious or accidental submit can't tie up the connection.
        // 100 covers a typical section roll in one go, still fits comfortably in one request.
        const int MaxRows = 100;
        var effective = rows.Take(MaxRows).ToList();

        int created = 0, linked = 0, failed = 0;
        for (int i = 0; i < effective.Count; i++)
        {
            var row = effective[i];
            var name = (row?.FullName ?? string.Empty).Trim();
            try
            {
                var r = await AddAsync(actingUserId, row!, ct);
                if (r.Ok)
                {
                    if (r.LinkedExisting) linked++; else created++;
                    results.Add(new BulkAddSchoolStudentRowResult(i, true, null, r.LinkedExisting, name));
                }
                else
                {
                    failed++;
                    results.Add(new BulkAddSchoolStudentRowResult(i, false, r.Error, false, name));
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Bulk add: row {Index} failed", i);
                failed++;
                results.Add(new BulkAddSchoolStudentRowResult(i, false, "Unexpected error saving this row.", false, name));
            }
        }

        if (rows.Count > MaxRows)
        {
            // Every row beyond the cap is reported as skipped so the writer can see and re-submit them.
            for (int i = MaxRows; i < rows.Count; i++)
            {
                var name = (rows[i]?.FullName ?? string.Empty).Trim();
                failed++;
                results.Add(new BulkAddSchoolStudentRowResult(i, false, $"Batch limit is {MaxRows} rows per submit — re-submit this row.", false, name));
            }
        }

        return new BulkAddSchoolStudentResult(created, linked, failed, results);
    }

    public async Task<AddSchoolStudentResult> AddAsync(
        Guid actingUserId, AddSchoolStudentRequest request, CancellationToken ct = default)
    {
        var schoolId = await ResolveSchoolIdAsync(actingUserId, ct);
        if (schoolId is null)
            return new(false, "You are not linked to a school.", null, false);

        var name = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new(false, "Student name is required.", null, false);

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        // Every field on the principal's form is mandatory.
        if (email is null) return new(false, "Enter the student's email address.", null, false);
        if (phone is null) return new(false, "Enter the student's mobile number.", null, false);
        if (request.DateOfBirth is null) return new(false, "Select the student's date of birth.", null, false);
        if (string.IsNullOrWhiteSpace(request.Gender)) return new(false, "Select the student's gender.", null, false);
        if (string.IsNullOrWhiteSpace(request.StudentClass)) return new(false, "Select the class / standard.", null, false);
        if (string.IsNullOrWhiteSpace(request.Section)) return new(false, "Select the section.", null, false);
        if (string.IsNullOrWhiteSpace(request.RollNumber)) return new(false, "Enter the roll number.", null, false);

        // Match email and phone INDEPENDENTLY. A single OR-query would silently pick whichever
        // row it hit first and link the wrong student when the two contacts belong to different
        // accounts, so each is resolved on its own and the pair is then compared.
        var byEmail = await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email, ct);
        var byPhone = await _db.Users.FirstOrDefaultAsync(u => u.Phone == phone, ct);

        // CASE 4 — the email is one student and the mobile is another. Ambiguous: refuse rather
        // than guess which account the principal meant.
        if (byEmail is not null && byPhone is not null && byEmail.Id != byPhone.Id)
            return new(false,
                "The email address or mobile number is already associated with another student account. Please verify the details.",
                null, true);

        var user = byEmail ?? byPhone;
        var linkedExisting = user is not null;

        if (user is not null)
        {
            // Where does this existing student already sit?
            var membership = await _db.SchoolStudents.AsNoTracking()
                .Include(ss => ss.School)
                .FirstOrDefaultAsync(ss => ss.UserId == user.Id, ct);

            // CASE 1 — already on THIS school's roll.
            if (membership is not null && membership.SchoolId == schoolId.Value)
                return new(false, "This student is already registered with your school.", user.Id, true);

            // CASE 3 — on ANOTHER school's roll. Never silently transfer: moving a student
            // between schools is an admin action, not a side effect of typing their email.
            if (membership is not null && membership.SchoolId != schoolId.Value)
                return new(false,
                    "This student is already associated with another school. Please contact Vijaypath support/admin for transfer.",
                    user.Id, true);

            // CASE 2 — exists but belongs to no school yet: fall through and link them.
        }

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                FullName = name,
                Email = email,
                Phone = phone,
                StudentClass = request.StudentClass!.Trim(),
                DateOfBirth = DateTime.SpecifyKind(request.DateOfBirth!.Value, DateTimeKind.Utc),
                Gender = request.Gender!.Trim(),
                // Created by the school with NO password, so it cannot be logged into. The student
                // claims it through the EXISTING forgot-password / OTP flow — no OTP is sent here,
                // because a principal adding a class of students should not fire a code at each of
                // them, and no second OTP system is introduced.
                IsActive = true,
                IsVerified = false,
            };
            _db.Users.Add(user);
        }

        // Grant the "student" role — and ONLY that. A student linked to a school never receives
        // school_principal or school_coordinator, whatever else may already be on the account.
        var studentRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "student", ct);
        if (studentRole != null)
        {
            var hasRole = await _db.UserRoles
                .AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == studentRole.Id, ct);
            if (!hasRole)
                _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = studentRole.Id, IsActive = true });
        }

        _db.SchoolStudents.Add(new SchoolStudent
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId.Value,
            UserId = user.Id,
            StudentClass = request.StudentClass!.Trim(),
            Section = request.Section!.Trim(),
            RollNumber = request.RollNumber!.Trim(),
            IsActive = true,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _log.LogError(ex, "Adding student to school {SchoolId} failed", schoolId);
            return new(false, "Could not save the student. The email or phone may already be in use.", null, linkedExisting);
        }

        return new(true, null, user.Id, linkedExisting);
    }

    public async Task<(bool ok, string? error)> RemoveAsync(
        Guid actingUserId, Guid schoolStudentId, CancellationToken ct = default)
    {
        var schoolId = await ResolveSchoolIdAsync(actingUserId, ct);
        if (schoolId is null) return (false, "You are not linked to a school.");

        // Matched on BOTH id and school: an id belonging to another school simply does not match,
        // so a guessed/copied id cannot delete someone else's record.
        var row = await _db.SchoolStudents
            .FirstOrDefaultAsync(ss => ss.Id == schoolStudentId && ss.SchoolId == schoolId.Value, ct);
        if (row is null) return (false, "Student not found for your school.");

        _db.SchoolStudents.Remove(row);
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }
}
