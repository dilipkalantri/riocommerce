using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services.Customers;

/// <inheritdoc cref="IStudentAccountProvisioner"/>
public class StudentAccountProvisioner : IStudentAccountProvisioner
{
    /// <summary>The role every self-service customer carries. Matches the seed data and the public
    /// registration path, so a provisioned student appears in the same admin lists as one who signed
    /// up on the website — not as a role-less orphan.</summary>
    private const string StudentRoleName = "student";

    private readonly RioCommerceDbContext _db;
    private readonly ILogger<StudentAccountProvisioner> _log;

    public StudentAccountProvisioner(RioCommerceDbContext db, ILogger<StudentAccountProvisioner> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<StudentAccountResult> ResolveOrCreateAsync(StudentAccountRequest request, CancellationToken ct = default)
    {
        // Nothing here may throw. The caller is mid-order: a student who cannot be provisioned is a
        // support ticket, an order that fails to save is lost money.
        try
        {
            var phone = Canonical(request.Phone);
            if (phone == null)
                return StudentAccountResult.Skipped($"Phone '{request.Phone}' is not usable as an identity key.");

            var existing = await FindByPhoneAsync(phone, ct);
            if (existing != null)
            {
                // Deliberately nothing else. See the interface docs: an existing customer's profile is
                // not the franchisee's to edit through an order form.
                return new StudentAccountResult(existing.Id, StudentAccountOutcome.LinkedExisting,
                    $"Matched existing user {existing.Id} on phone.");
            }

            return await CreateAsync(request, phone, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Student account provisioning threw for Phone={Phone} Name={Name} Source={Source}",
                request.Phone, request.FullName, request.SourceNote);
            return StudentAccountResult.Failed(ex.Message);
        }
    }

    public async Task<StudentPhoneMatch> FindCustomerByPhoneAsync(string? phone, CancellationToken ct = default)
    {
        var canonical = Canonical(phone);
        if (canonical == null) return new StudentPhoneMatch(false, null, null);

        var existing = await FindByPhoneAsync(canonical, ct);
        return new StudentPhoneMatch(true, existing?.Id, canonical);
    }

    // ── Creation ───────────────────────────────────────────────────────────────
    private async Task<StudentAccountResult> CreateAsync(StudentAccountRequest request, string phone, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(request.FullName) ? phone : request.FullName.Trim();

        // Email is recorded, never matched on — and only when it is actually free. The users table has
        // a unique index on Email too, so re-using a family member's address here would fail the whole
        // insert. The address is not lost either way: it stays on the order as StudentEmail.
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email != null)
        {
            var lowered = email.ToLowerInvariant();
            if (await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == lowered, ct))
            {
                _log.LogInformation("Student account for phone {Phone} created without email — {Email} belongs to another account.", phone, email);
                email = null;
            }
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = name,
            Phone = phone,
            Email = email,
            City = NullIfBlank(request.City),
            State = NullIfBlank(request.State),
            // No password is generated. Handing out credentials nobody asked for is worse than having
            // none: the student sets one through the ordinary forgot-password flow, which works fine
            // against a null hash.
            PasswordHash = null,
            IsActive = true,
            // They have proved nothing yet — no OTP, no click. Verification is theirs to complete.
            IsVerified = false,
            AdminComment = AppendComment(null, request.SourceNote)
        };

        _db.Users.Add(user);

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == StudentRoleName, ct);
        UserRole? link = null;
        if (role != null)
        {
            link = new UserRole { Id = Guid.NewGuid(), UserId = user.Id, RoleId = role.Id, IsActive = true };
            _db.UserRoles.Add(link);
        }
        else
        {
            _log.LogWarning("Role '{Role}' is missing — student account {UserId} was created without it.", StudentRoleName, user.Id);
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Provisioned student account {UserId} for phone {Phone} ({Source}).", user.Id, phone, request.SourceNote);
            return new StudentAccountResult(user.Id, StudentAccountOutcome.CreatedNew, request.SourceNote);
        }
        catch (DbUpdateException ex)
        {
            // Lost a race against the unique phone index (or something else rejected the row). Drop our
            // failed insert BEFORE anything else — the caller shares this DbContext and its own
            // SaveChangesAsync would otherwise retry this doomed row and take the order down with it.
            Detach(user, link);

            var winner = await FindByPhoneAsync(phone, ct);
            if (winner != null)
            {
                _log.LogInformation(ex, "Concurrent creation for phone {Phone} — linked to the winning account {UserId} instead.", phone, winner.Id);
                return new StudentAccountResult(winner.Id, StudentAccountOutcome.LinkedExisting, "Recovered from a duplicate-key race.");
            }

            _log.LogError(ex, "Student account insert failed for phone {Phone} and no existing account explains it.", phone);
            return StudentAccountResult.Failed(ex.Message);
        }
        catch
        {
            Detach(user, link);
            throw;   // handled (and logged) by ResolveOrCreateAsync
        }
    }

    private void Detach(User user, UserRole? link)
    {
        _db.Entry(user).State = EntityState.Detached;
        if (link != null) _db.Entry(link).State = EntityState.Detached;
    }

    // ── Matching ───────────────────────────────────────────────────────────────
    /// <summary>
    /// The one and only identity probe: phone. An exact hit wins; failing that a number whose last ten
    /// digits are the same is treated as the same person, because the platform has stored mobiles both
    /// bare and with a country code and "same human, two rows" is precisely what we are here to stop.
    /// </summary>
    /// <remarks>
    /// Reads with NO tracking, deliberately. The caller only ever wants the id, and an existing
    /// customer that is never tracked is an existing customer that cannot be accidentally modified by
    /// anyone's later SaveChanges — the rule this whole class exists to keep. It is also what lets the
    /// backfill's dry run finish with an empty change tracker.
    /// </remarks>
    private async Task<User?> FindByPhoneAsync(string phone, CancellationToken ct)
    {
        var exact = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Phone == phone, ct);
        if (exact != null) return exact;

        var last10 = Last10(phone);
        if (last10 == null) return null;

        var near = await _db.Users.AsNoTracking()
            .Where(u => u.Phone != null && u.Phone.EndsWith(last10))
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        // EndsWith is a cheap pre-filter; confirm on digits only so a separator can't slip through.
        var match = near.FirstOrDefault(u => Last10(u.Phone!) == last10);
        if (match != null)
            _log.LogInformation("Phone {Phone} matched existing account {UserId} on its last 10 digits ({Stored}).", phone, match.Id, match.Phone);
        return match;
    }

    /// <summary>Trimmed phone, or null when there is nothing identity-worthy in it. Ten digits is the
    /// Indian mobile length the franchise form already enforces.</summary>
    private static string? Canonical(string? phone)
    {
        var p = phone?.Trim();
        if (string.IsNullOrEmpty(p)) return null;
        return Last10(p) == null ? null : p;
    }

    private static string? Last10(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return digits.Length < 10 ? null : digits[^10..];
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Adds the provenance line to an admin note without ever losing what was already there.</summary>
    internal static string? AppendComment(string? existing, string? note)
    {
        var addition = NullIfBlank(note);
        if (addition == null) return existing;
        var current = NullIfBlank(existing);
        if (current == null) return addition;
        return current.Contains(addition, StringComparison.OrdinalIgnoreCase) ? current : $"{current}\n{addition}";
    }

    // ── Dry run (READ-ONLY) ────────────────────────────────────────────────────
    public async Task<StudentLinkDryRunReport> BackfillDryRunAsync(OrderSource? source = null, CancellationToken ct = default)
    {
        var q = _db.Orders.AsNoTracking();
        if (source.HasValue) q = q.Where(o => o.Source == source.Value);

        var orders = await q
            .OrderBy(o => o.OrderNumber)
            .Select(o => new { o.Id, o.OrderNumber, o.UserId, o.StudentName, o.StudentPhone, o.StudentEmail })
            .ToListAsync(ct);

        // Everything below is in-memory arithmetic over that snapshot plus one read of the matching
        // users. No entity is tracked, so there is nothing a stray SaveChanges could ever persist.
        var candidatePhones = orders
            .Where(o => o.UserId == null)
            .Select(o => Canonical(o.StudentPhone))
            .Where(p => p != null)
            .Select(p => p!)
            .Distinct()
            .ToList();

        var byExact = await _db.Users.AsNoTracking()
            .Where(u => u.Phone != null && candidatePhones.Contains(u.Phone))
            .Select(u => new { u.Id, u.FullName, u.Phone })
            .ToListAsync(ct);

        // Second pass for numbers stored in another shape (country code, spacing). Cheap at this size,
        // and it is exactly the case that would otherwise create a duplicate person.
        var stillMissing = candidatePhones
            .Where(p => !byExact.Any(u => u.Phone == p))
            .Select(p => Last10(p))
            .Where(s => s != null)
            .Select(s => s!)
            .Distinct()
            .ToList();

        var nearMatches = new List<(string Last10, Guid Id, string Name, string Phone)>();
        foreach (var suffix in stillMissing)
        {
            var hits = await _db.Users.AsNoTracking()
                .Where(u => u.Phone != null && u.Phone.EndsWith(suffix))
                .Select(u => new { u.Id, u.FullName, u.Phone })
                .ToListAsync(ct);
            foreach (var h in hits.Where(h => Last10(h.Phone!) == suffix))
                nearMatches.Add((suffix, h.Id, h.FullName, h.Phone!));
        }

        var rows = new List<StudentLinkDryRunRow>(orders.Count);
        // Which phones a new account would have to be created for — counted once, not once per order,
        // so two orders for the same student never inflate the "accounts to create" figure.
        var wouldCreate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wouldLink = new HashSet<Guid>();

        foreach (var o in orders)
        {
            if (o.UserId != null)
            {
                rows.Add(Row(o.Id, o.OrderNumber, o.StudentName, o.StudentPhone, o.StudentEmail,
                    o.UserId, null, null, StudentLinkAction.NoAction, "Order is already linked to a customer."));
                continue;
            }

            var phone = Canonical(o.StudentPhone);
            if (phone == null)
            {
                rows.Add(Row(o.Id, o.OrderNumber, o.StudentName, o.StudentPhone, o.StudentEmail,
                    null, null, null, StudentLinkAction.ReviewRequired,
                    "No usable mobile number on the order — identity cannot be established."));
                continue;
            }

            var exact = byExact.FirstOrDefault(u => u.Phone == phone);
            if (exact != null)
            {
                wouldLink.Add(exact.Id);
                rows.Add(Row(o.Id, o.OrderNumber, o.StudentName, o.StudentPhone, o.StudentEmail,
                    exact.Id, exact.FullName, exact.Phone, StudentLinkAction.LinkExisting,
                    "An account already exists with this exact mobile number."));
                continue;
            }

            var suffix = Last10(phone);
            var near = nearMatches.Where(n => n.Last10 == suffix).ToList();
            if (near.Count == 1)
            {
                wouldLink.Add(near[0].Id);
                rows.Add(Row(o.Id, o.OrderNumber, o.StudentName, o.StudentPhone, o.StudentEmail,
                    near[0].Id, near[0].Name, near[0].Phone, StudentLinkAction.LinkExisting,
                    $"Same mobile stored in a different format ({near[0].Phone})."));
                continue;
            }
            if (near.Count > 1)
            {
                rows.Add(Row(o.Id, o.OrderNumber, o.StudentName, o.StudentPhone, o.StudentEmail,
                    null, string.Join(" | ", near.Select(n => n.Name)), null, StudentLinkAction.ReviewRequired,
                    $"{near.Count} accounts share this mobile number — a human must pick one."));
                continue;
            }

            wouldCreate.Add(phone);
            rows.Add(Row(o.Id, o.OrderNumber, o.StudentName, o.StudentPhone, o.StudentEmail,
                null, null, null, StudentLinkAction.CreateNew, "No account exists for this mobile number."));
        }

        // Students appearing on more than one order — the duplicate-account trap this whole change is
        // about. Grouped on phone, because that is the identity; names vary in spelling.
        var duplicates = orders
            .Select(o => new { o.OrderNumber, o.StudentName, Phone = Canonical(o.StudentPhone) })
            .Where(x => x.Phone != null)
            .GroupBy(x => x.Phone!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => new StudentLinkDuplicateGroup(
                g.Key,
                string.Join(" | ", g.Select(x => x.StudentName).Distinct(StringComparer.OrdinalIgnoreCase)),
                g.Select(x => x.OrderNumber).OrderBy(n => n).ToList()))
            .OrderBy(g => g.Phone)
            .ToList();

        var distinctStudents = orders
            .Select(o => Canonical(o.StudentPhone))
            .Where(p => p != null)
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new StudentLinkDryRunReport(
            OrdersScanned: orders.Count,
            DistinctStudents: distinctStudents,
            StudentsWithExistingAccount: wouldLink.Count,
            StudentsNeedingNewAccount: wouldCreate.Count,
            OrdersAlreadyLinked: rows.Count(r => r.Action == StudentLinkAction.NoAction),
            OrdersNeedingReview: rows.Count(r => r.Action == StudentLinkAction.ReviewRequired),
            Rows: rows,
            DuplicateStudents: duplicates);
    }

    private static StudentLinkDryRunRow Row(
        Guid id, string number, string name, string phone, string? email,
        Guid? matchedId, string? matchedName, string? matchedPhone, StudentLinkAction action, string reason)
        => new(id, number, name, phone, email, matchedId, matchedName, matchedPhone, action, reason);
}
