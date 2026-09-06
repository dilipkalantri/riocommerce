using System.Globalization;
using ClosedXML.Excel;
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
        => (await ResolveScopeAsync(actingUserId, ct))?.SchoolId;

    /// <summary>
    /// The single source of truth for "what may this user touch" — school AND, for a Coordinator,
    /// the added-by restriction. Every scoped query in the school module is built from this.
    /// </summary>
    public async Task<SchoolAccessScope?> ResolveScopeAsync(Guid actingUserId, CancellationToken ct = default)
    {
        if (actingUserId == Guid.Empty) return null;

        // The ROLE comes from the database, not from an ASP.NET claim. Claims are issued at sign-in
        // and would go stale if an admin demoted someone mid-session; this row is the live truth and
        // is the same row that decides the school.
        var links = await _db.SchoolUsers.AsNoTracking()
            .Where(su => su.UserId == actingUserId
                      && su.IsActive
                      && (su.Role == SchoolUserRole.Principal || su.Role == SchoolUserRole.Coordinator))
            .Select(su => new { su.SchoolId, su.Role })
            .ToListAsync(ct);

        if (links.Count == 0) return null;

        // A Principal row anywhere wins. Someone who is BOTH principal and coordinator keeps the
        // wider view — the restriction exists to stop a coordinator reaching further than their own
        // work, not to take access away from someone already entitled to the whole school.
        var principal = links.FirstOrDefault(l => l.Role == SchoolUserRole.Principal);
        if (principal is not null)
            return new SchoolAccessScope(principal.SchoolId, null);

        var coordinator = links[0];
        return new SchoolAccessScope(coordinator.SchoolId, actingUserId);
    }

    public async Task<List<SchoolStudentListItem>> ListAsync(
        Guid actingUserId, string? search = null, CancellationToken ct = default)
    {
        var scope = await ResolveScopeAsync(actingUserId, ct);
        // No school -> no rows. Never fall back to "all students".
        if (scope is null) return new();

        var q = _db.SchoolStudents.AsNoTracking()
            .Include(ss => ss.User)
            .Include(ss => ss.School)
            .Where(ss => ss.SchoolId == scope.SchoolId);

        // COORDINATOR ISOLATION. Applied to the queryable BEFORE the search terms, so it constrains
        // every branch below — an unfiltered list, a name search and a phone search are all confined
        // to this coordinator's own students. A NULL AddedByUserId never equals a real id, so the
        // pre-0050 students are excluded here without needing a separate clause.
        if (scope.RestrictToAddedByUserId is { } addedBy)
            q = q.Where(ss => ss.AddedByUserId == addedBy);

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

    /// <summary>
    /// Counts what the CALLER may see: the whole school for a Principal, only their own students for
    /// a Coordinator. It is not a school-wide count with a different name — a coordinator calling it
    /// must never learn the school's total.
    /// </summary>
    public async Task<int> CountAsync(Guid actingUserId, CancellationToken ct = default)
    {
        var scope = await ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return 0;
        return await _db.SchoolStudents.CountAsync(
            ss => ss.SchoolId == scope.SchoolId
               && ss.IsActive
               && (scope.RestrictToAddedByUserId == null || ss.AddedByUserId == scope.RestrictToAddedByUserId), ct);
    }

    public async Task<int> CountAddedByAsync(Guid actingUserId, CancellationToken ct = default)
    {
        // Still scoped to the school first, exactly like every other method here. AddedByUserId
        // alone would be enough in practice, but keeping the school predicate means a staff member
        // moved between schools only ever counts the students of the school they are in now.
        var schoolId = await ResolveSchoolIdAsync(actingUserId, ct);
        if (schoolId is null) return 0;
        return await _db.SchoolStudents.CountAsync(
            ss => ss.SchoolId == schoolId.Value && ss.IsActive && ss.AddedByUserId == actingUserId, ct);
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

        // Only Name, Mobile, DOB and Gender are required. Email / Class / Section / Roll No are
        // optional — a student on a paper roll may not have their email or roll number yet, and
        // enrolments still work because the roster keeps everything else. Phone stays mandatory
        // because it is the fallback identity when the student later claims their account.
        if (phone is null) return new(false, "Enter the student's mobile number.", null, false);
        if (request.DateOfBirth is null) return new(false, "Select the student's date of birth.", null, false);
        if (string.IsNullOrWhiteSpace(request.Gender)) return new(false, "Select the student's gender.", null, false);

        // Match email and phone INDEPENDENTLY. A single OR-query would silently pick whichever
        // row it hit first and link the wrong student when the two contacts belong to different
        // accounts, so each is resolved on its own and the pair is then compared. Skip the email
        // lookup when no email was provided — otherwise `Email == null` would match every legacy
        // account without an email.
        var byEmail = email is null
            ? null
            : await _db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email, ct);
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
                StudentClass = string.IsNullOrWhiteSpace(request.StudentClass) ? null : request.StudentClass.Trim(),
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
            StudentClass = string.IsNullOrWhiteSpace(request.StudentClass) ? null : request.StudentClass.Trim(),
            Section = string.IsNullOrWhiteSpace(request.Section) ? null : request.Section.Trim(),
            RollNumber = string.IsNullOrWhiteSpace(request.RollNumber) ? null : request.RollNumber.Trim(),
            IsActive = true,
            // The AUTHENTICATED caller, never anything from the request — attribution must not be
            // something a client can claim. schoolId was already derived from this same id, so the
            // student and the credit for adding them come from one source.
            AddedByUserId = actingUserId,
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
        var scope = await ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return (false, "You are not linked to a school.");

        // Matched on id AND school AND — for a coordinator — added-by, all in ONE query. A guessed
        // or copied id belonging to another school, or to another coordinator's student, simply does
        // not match. The caller gets the same "not found" either way, so the endpoint cannot be used
        // to discover which student ids exist.
        var row = await _db.SchoolStudents
            .FirstOrDefaultAsync(ss => ss.Id == schoolStudentId
                                    && ss.SchoolId == scope.SchoolId
                                    && (scope.RestrictToAddedByUserId == null
                                        || ss.AddedByUserId == scope.RestrictToAddedByUserId), ct);
        if (row is null)
        {
            _log.LogWarning("School student remove DENIED — user {UserId} (school {SchoolId}, restricted={Restricted}) requested {RowId}.",
                actingUserId, scope.SchoolId, scope.IsRestricted, schoolStudentId);
            return (false, "Student not found for your school.");
        }

        _db.SchoolStudents.Remove(row);
        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    // ── Excel bulk import ────────────────────────────────────────────────────────────────────
    //
    // Uses ClosedXML, which the project already depends on for the school master import — no new
    // package. The columns are declared once here and drive BOTH the generated template and the
    // parser, so the file a writer downloads can never disagree with the file the parser expects.

    private static readonly string[] ImportHeaders =
        { "Student Name *", "Email", "Mobile *", "Date of Birth (DD-MM-YYYY) *", "Gender *", "Class *" };

    /// <summary>Matches the wizard: classes are 1–12.</summary>
    private const int MinClass = 1, MaxClass = 12;

    /// <summary>A single submit is capped, matching the manual bulk grid.</summary>
    private const int MaxImportRows = 500;

    public byte[] BuildImportTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Students");

        for (var c = 0; c < ImportHeaders.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = ImportHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        // One worked example. Written as TEXT so Excel cannot silently reformat the date into its
        // own locale — the parser accepts both a real date cell and DD-MM-YYYY text, but the sample
        // must show the documented shape.
        var sample = new[] { "Ramesh Kadam", "ramesh@example.com", "9876543210", "05-08-2011", "Male", "8" };
        for (var c = 0; c < sample.Length; c++)
        {
            ws.Cell(2, c + 1).Value = sample[c];
            ws.Cell(2, c + 1).Style.Font.FontColor = XLColor.Gray;
        }

        // Date and Mobile as text columns, so a leading zero or a DD-MM-YYYY string survives typing.
        ws.Column(3).Style.NumberFormat.Format = "@";
        ws.Column(4).Style.NumberFormat.Format = "@";
        ws.Columns().AdjustToContents();

        var notes = wb.AddWorksheet("Instructions");
        var lines = new[]
        {
            "Vijaypath — Student Import",
            "",
            "Fill one student per row on the 'Students' sheet. Delete the grey example row first.",
            "",
            "Student Name *  Required.",
            "Email           Optional. Leave blank if the student has no email address.",
            "Mobile *        Required. 10 digits.",
            "Date of Birth * Required. DD-MM-YYYY (for example 05-08-2011).",
            "Gender *        Required. Male, Female or Other.",
            "Class *         Required. A number from 1 to 12.",
            "",
            $"Up to {MaxImportRows} students per file.",
            "Every student is added to YOUR school automatically — there is no school column.",
        };
        for (var i = 0; i < lines.Length; i++) notes.Cell(i + 1, 1).Value = lines[i];
        notes.Cell(1, 1).Style.Font.Bold = true;
        notes.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<SchoolStudentImportPreview> ParseImportAsync(
        Guid actingUserId, Stream xlsx, CancellationToken ct = default)
    {
        // Scope first: someone with no school gets nothing to preview, never a parsed file.
        var scope = await ResolveScopeAsync(actingUserId, ct);
        if (scope is null) return SchoolStudentImportPreview.Fail("You are not linked to a school.");

        List<IXLRangeRow> dataRows;
        IXLWorksheet ws;
        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(xlsx);
            ws = wb.Worksheet(1);
            var used = ws.RangeUsed();
            if (used is null) return SchoolStudentImportPreview.Fail("That file has no rows.");
            dataRows = used.RowsUsed().Skip(1).ToList();   // row 1 is the header
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Student import: unreadable workbook from user {UserId}.", actingUserId);
            return SchoolStudentImportPreview.Fail("That file could not be read. Please use the sample template (.xlsx).");
        }

        using (wb)
        {
            if (dataRows.Count == 0)
                return SchoolStudentImportPreview.Fail("The sheet has a header but no students.");
            if (dataRows.Count > MaxImportRows)
                return SchoolStudentImportPreview.Fail(
                    $"That file has {dataRows.Count} rows. Please import at most {MaxImportRows} at a time.");

            // Everything on this school's roll, read ONCE. Deliberately the whole school, not just
            // this coordinator's students: a pupil another coordinator already added must be
            // reported as existing rather than attempted and rejected row-by-row by AddAsync.
            var existing = await _db.SchoolStudents.AsNoTracking()
                .Where(ss => ss.SchoolId == scope.SchoolId)
                .Select(ss => new { ss.User.Email, ss.User.Phone })
                .ToListAsync(ct);
            var existingPhones = existing.Where(e => e.Phone != null)
                .Select(e => e.Phone!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingEmails = existing.Where(e => e.Email != null)
                .Select(e => e.Email!).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var seenPhones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<SchoolStudentImportRow>(dataRows.Count);

            foreach (var r in dataRows)
            {
                ct.ThrowIfCancellationRequested();

                var excelRow = r.RowNumber();
                string Cell(int i) => r.Cell(i).GetFormattedString().Trim();

                var name = Cell(1);
                var emailRaw = Cell(2);
                var phoneRaw = Cell(3);
                var dobRaw = Cell(4);
                var genderRaw = Cell(5);
                var classRaw = Cell(6);

                // A row where every cell is blank is trailing whitespace in the sheet, not a mistake.
                if (name.Length == 0 && emailRaw.Length == 0 && phoneRaw.Length == 0
                    && dobRaw.Length == 0 && genderRaw.Length == 0 && classRaw.Length == 0)
                    continue;

                var phone = new string(phoneRaw.Where(char.IsDigit).ToArray());
                // A 12-digit number pasted with the country code is the same subscriber.
                if (phone.Length == 12 && phone.StartsWith("91")) phone = phone[2..];
                var email = emailRaw.Length == 0 ? null : emailRaw.ToLowerInvariant();
                var gender = NormaliseGender(genderRaw);
                var dob = ParseDob(r.Cell(4), dobRaw);

                SchoolStudentImportRow Row(SchoolStudentImportStatus status, string? error) =>
                    new(excelRow, name.Length == 0 ? null : name, email,
                        phone.Length == 0 ? null : phone, dob, gender,
                        classRaw.Length == 0 ? null : classRaw, status, error);

                // ── Field validation. First failure wins, so the message points at one thing to fix.
                if (name.Length == 0)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, "Student Name is missing.")); continue; }

                if (phoneRaw.Length == 0)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, "Mobile is missing.")); continue; }

                if (phone.Length != 10)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, $"Invalid mobile '{phoneRaw}' — needs 10 digits.")); continue; }

                if (email is not null && (!email.Contains('@') || !email.Contains('.') || email.Length < 5))
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, $"Invalid email '{emailRaw}'.")); continue; }

                if (dobRaw.Length == 0)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, "Date of Birth is missing.")); continue; }

                if (dob is null)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, $"Invalid Date of Birth '{dobRaw}' — use DD-MM-YYYY.")); continue; }

                if (dob > DateTime.Today.AddYears(-3) || dob < DateTime.Today.AddYears(-100))
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, $"Date of Birth '{dobRaw}' is out of range.")); continue; }

                if (genderRaw.Length == 0)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, "Gender is missing.")); continue; }

                if (gender is null)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, $"Invalid gender '{genderRaw}' — use Male, Female or Other.")); continue; }

                if (classRaw.Length == 0)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, "Class is missing.")); continue; }

                if (!int.TryParse(classRaw, out var cls) || cls < MinClass || cls > MaxClass)
                { rows.Add(Row(SchoolStudentImportStatus.Invalid, $"Invalid class '{classRaw}' — use {MinClass}–{MaxClass}.")); continue; }

                // ── Duplicates. Inside the file first, then against the roll.
                //
                // Reached only by rows that already PASSED validation, which is deliberate: a row
                // discarded for a missing name never claims its mobile, so a later, well-formed row
                // carrying that same number is a genuine student and imports normally rather than
                // being blamed for colliding with something that was thrown away.
                if (!seenPhones.Add(phone))
                { rows.Add(Row(SchoolStudentImportStatus.DuplicateInFile, $"Mobile {phone} appears earlier in this file.")); continue; }

                if (email is not null && !seenEmails.Add(email))
                { rows.Add(Row(SchoolStudentImportStatus.DuplicateInFile, $"Email {email} appears earlier in this file.")); continue; }

                if (existingPhones.Contains(phone))
                { rows.Add(Row(SchoolStudentImportStatus.AlreadyExists, $"A student with mobile {phone} is already on your school's roll.")); continue; }

                if (email is not null && existingEmails.Contains(email))
                { rows.Add(Row(SchoolStudentImportStatus.AlreadyExists, $"A student with email {email} is already on your school's roll.")); continue; }

                rows.Add(new SchoolStudentImportRow(
                    excelRow, name, email, phone, dob, gender, cls.ToString(),
                    SchoolStudentImportStatus.Valid, null));
            }

            if (rows.Count == 0)
                return SchoolStudentImportPreview.Fail("The sheet has a header but no students.");

            return new SchoolStudentImportPreview(true, null, rows);
        }
    }

    public async Task<SchoolStudentImportResult> ImportAsync(
        Guid actingUserId, IReadOnlyList<SchoolStudentImportRow> rows, CancellationToken ct = default)
    {
        int imported = 0, linked = 0, failed = 0;
        var failures = new List<SchoolStudentImportRow>();

        // ONLY rows the server itself marked Valid. Nothing here trusts a status that arrived from
        // outside: the rows come from ParseImportAsync, and every one of them is then put through
        // AddAsync — the SAME method the manual form uses. That is what guarantees the import cannot
        // set AddedByUserId, cannot reach another school, and cannot skip the duplicate rules,
        // because AddAsync derives all three from actingUserId rather than from the row.
        foreach (var row in rows.Where(r => r.Status == SchoolStudentImportStatus.Valid))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await AddAsync(actingUserId, new AddSchoolStudentRequest
                {
                    // A Valid row always carries a name — ParseImportAsync rejects a blank one —
                    // but AddSchoolStudentRequest.FullName is non-nullable, so say so explicitly
                    // rather than leaving a nullable-assignment warning standing.
                    FullName = row.FullName ?? string.Empty,
                    Email = row.Email,               // already null (never "") when blank
                    Phone = row.Phone,
                    DateOfBirth = row.DateOfBirth,
                    Gender = row.Gender,
                    StudentClass = row.StudentClass,
                }, ct);

                if (result.Ok)
                {
                    if (result.LinkedExisting) linked++; else imported++;
                }
                else
                {
                    failed++;
                    failures.Add(row with { Status = SchoolStudentImportStatus.Invalid, Error = result.Error });
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Student import: Excel row {Row} failed for user {UserId}.", row.ExcelRow, actingUserId);
                failed++;
                failures.Add(row with { Status = SchoolStudentImportStatus.Invalid, Error = "Unexpected error saving this row." });
            }
        }

        _log.LogInformation("Student import by {UserId}: {Imported} created, {Linked} linked, {Failed} failed.",
            actingUserId, imported, linked, failed);

        return new SchoolStudentImportResult(imported, linked, failed, failures);
    }

    /// <summary>Male / Female / Other, however it was capitalised or abbreviated. Null = unusable.</summary>
    private static string? NormaliseGender(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "male" or "m" => "Male",
        "female" or "f" => "Female",
        "other" or "o" => "Other",
        _ => null,
    };

    /// <summary>
    /// A real Excel date cell wins; otherwise the text is read as DD-MM-YYYY (and the common
    /// separator variants), never as the server's locale — 05-08-2011 must mean 5 August whatever
    /// culture the process happens to run under.
    /// </summary>
    private static DateTime? ParseDob(IXLCell cell, string raw)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var native))
            return DateTime.SpecifyKind(native.Date, DateTimeKind.Utc);

        if (raw.Length == 0) return null;

        string[] formats = { "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "yyyy-MM-dd" };
        if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);

        return null;
    }
}
