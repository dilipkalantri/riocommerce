using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RioCommerce.Core.DTOs.School;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

public class SchoolRegistrationService(
    RioCommerceDbContext db,
    IVerificationService verification,
    IEmailRouter email,
    ISmsSender sms,
    ILogger<SchoolRegistrationService> log) : ISchoolRegistrationService
{
    private readonly RioCommerceDbContext _db = db;
    private readonly IVerificationService _verification = verification;
    private readonly IEmailRouter _email = email;
    private readonly ISmsSender _sms = sms;
    private readonly ILogger<SchoolRegistrationService> _log = log;

    private const string BrandName = "Vijaypath";

    /// <summary>
    /// Body for the "registration successful" SMS. DELIBERATELY EMPTY.
    ///
    /// Both currently-approved DLT templates are verification/OTP ones, so this different text
    /// would be rejected by DLT — and rejected AFTER the gateway answers HTTP 200, i.e. silently.
    /// Rather than ship a message that looks sent and never arrives, the send is skipped while
    /// this is blank.
    ///
    /// To switch it on: get a template approved (e.g. "Dear Customer your school registration
    /// with Rioplay is successful. UDISE: {#var#}"), put its templateId on the gateway URL, then
    /// paste the approved text here with {0} where the UDISE variable goes. Nothing else changes.
    /// </summary>
    private static readonly string RegistrationSuccessSmsTemplate = string.Empty;

    public async Task<SchoolLookupResult?> LookupByUdiseAsync(string udiseCode)
    {
        var code = udiseCode.Trim();
        var school = await _db.Schools
            .AsNoTracking()
            .Include(s => s.Taluka)
            .Include(s => s.District)
            .Include(s => s.State)
            .FirstOrDefaultAsync(s => s.UdiseCode == code);

        if (school == null) return null;

        var hasPrincipal = await _db.SchoolUsers
            .AnyAsync(su => su.SchoolId == school.Id
                         && su.Role == SchoolUserRole.Principal
                         && su.IsActive);

        return new SchoolLookupResult(
            school.Id,
            school.UdiseCode,
            school.Name,
            school.Address,
            school.SchoolType.ToString(),
            school.LowestClass,
            school.HighestClass,
            school.CityOrVillage,
            school.Taluka?.Name,
            school.District?.Name,
            school.State?.Name,
            hasPrincipal);
    }

    public async Task<(bool ok, string? error)> RegisterPrincipalAsync(SchoolPrincipalRegisterRequest request)
    {
        _db.ChangeTracker.Clear();

        var udise = request.UdiseCode.Trim();
        var email = request.Email.Trim().ToLower();
        var phone = request.Phone.Trim();

        var school = await _db.Schools.FirstOrDefaultAsync(s => s.UdiseCode == udise);
        if (school == null)
            return (false, "School not found for the given UDISE code.");

        if (!school.IsActive)
            return (false, "This school is currently inactive. Please contact the administrator.");

        var existingPrincipal = await _db.SchoolUsers
            .AnyAsync(su => su.SchoolId == school.Id
                         && su.Role == SchoolUserRole.Principal
                         && su.IsActive);
        if (existingPrincipal)
            return (false, "A principal is already registered for this school.");

        var emailTaken = await _db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email);
        if (emailTaken)
            return (false, "An account with this email already exists. Please login instead.");

        var phoneTaken = await _db.Users.AnyAsync(u => u.Phone == phone);
        if (phoneTaken)
            return (false, "An account with this phone number already exists. Please login instead.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive = false,
            IsVerified = false
        };
        _db.Users.Add(user);

        var principalRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "school_principal");
        if (principalRole != null)
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = principalRole.Id, IsActive = true });

        _db.SchoolUsers.Add(new SchoolUser
        {
            SchoolId = school.Id,
            UserId = user.Id,
            Role = SchoolUserRole.Principal,
            IsActive = true
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return (false, "Registration failed due to a conflict. The email or phone may already be in use.");
        }

        // One code, delivered to BOTH the principal's email and phone. SendBothAsync writes a
        // row per channel sharing a single hash, so the code entered at the Verify step matches
        // whichever channel it arrived on. Succeeds when at least one channel lands, so a dead
        // SMS gateway cannot block a registration that the email already reached.
        var send = await _verification.SendBothAsync(
            VerificationPurpose.SchoolRegistration,
            user.Email,
            user.Phone,
            user.Id,
            user.FullName);

        if (!send.Success)
            return (false, send.ErrorMessage ?? "Failed to send verification code. Please try again.");

        return (true, null);
    }

    public async Task<(bool ok, string? error, Guid? userId)> VerifyOtpAsync(string email, string code)
    {
        _db.ChangeTracker.Clear();

        var normalizedEmail = email.Trim().ToLower();
        var check = await _verification.VerifyAsync(
            VerificationPurpose.SchoolRegistration, normalizedEmail, code.Trim());

        if (!check.Success)
            return (false, check.ErrorMessage ?? "Invalid or expired code.", null);

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);
        if (user == null)
            return (false, "Account not found.", null);

        user.IsActive = true;
        user.IsVerified = true;
        user.LastLoginAt = DateTime.UtcNow;
        user.LoginCount = 1;
        await _db.SaveChangesAsync();

        // Confirmation is best-effort and deliberately AFTER the save: the account is already
        // active and the caller is about to be signed in, so a dead mail server or SMS gateway
        // must never turn a completed registration into a failed one.
        await SendRegistrationSuccessAsync(user);

        return (true, null, user.Id);
    }

    public async Task<(bool ok, string? error)> ResendOtpAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLower();
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);

        if (user == null)
            return (true, null); // don't reveal non-existence

        var send = await _verification.SendBothAsync(
            VerificationPurpose.SchoolRegistration,
            user.Email,
            user.Phone,
            user.Id,
            user.FullName);

        return (send.Success, send.ErrorMessage);
    }

    /// <summary>
    /// Confirms a completed school registration on email (live) and SMS (dormant until a DLT
    /// template is approved — see <see cref="RegistrationSuccessSmsTemplate"/>). Never throws.
    /// </summary>
    private async Task SendRegistrationSuccessAsync(User user)
    {
        try
        {
            var link = await _db.SchoolUsers
                .AsNoTracking()
                .Include(su => su.School).ThenInclude(s => s.District)
                .FirstOrDefaultAsync(su => su.UserId == user.Id && su.IsActive);

            var school = link?.School;
            var schoolName = school?.Name ?? "your school";
            var udise = school?.UdiseCode ?? "—";

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                // Email is not governed by DLT, so it carries the real brand and full detail.
                // Email is not bound by DLT, so it carries the real brand and full detail.
                // Built with string.Join rather than embedded newline escapes: plain, and
                // it keeps the template readable as a list of lines.
                var lines = new List<string>
                {
                    $"Hi {user.FullName},",
                    "",
                    $"Your school registration on {BrandName} is complete.",
                    "",
                    $"School   : {schoolName}",
                    $"UDISE    : {udise}",
                };
                if (!string.IsNullOrWhiteSpace(school?.District?.Name))
                    lines.Add($"District : {school!.District!.Name}");
                lines.Add($"Login    : {user.Email}");
                lines.Add("");
                lines.Add("You can now sign in to the school portal to add coordinators and manage students.");
                lines.Add("");
                lines.Add($"— {BrandName}");
                var body = string.Join(Environment.NewLine, lines);

                var res = await _email.SendAsync(new EmailMessage(
                    user.Email!, user.FullName,
                    $"Your {BrandName} school registration is complete", body));

                if (!res.Ok)
                    _log.LogWarning("School welcome email failed for {UserId}: {Err}", user.Id, res.Error);
            }

            if (!string.IsNullOrWhiteSpace(RegistrationSuccessSmsTemplate)
                && !string.IsNullOrWhiteSpace(user.Phone))
            {
                var text = string.Format(RegistrationSuccessSmsTemplate, udise);
                var (ok, err) = await _sms.SendAsync(user.Phone!, text);
                if (!ok) _log.LogWarning("School welcome SMS failed for {UserId}: {Err}", user.Id, err);
            }
        }
        catch (Exception ex)
        {
            // Swallowed on purpose — the registration itself already succeeded.
            _log.LogError(ex, "School registration confirmation failed for {UserId}", user.Id);
        }
    }

    public async Task<SchoolPortalDashboard?> GetDashboardAsync(Guid userId)
    {
        var link = await _db.SchoolUsers
            .AsNoTracking()
            .Include(su => su.School).ThenInclude(s => s.District)
            .FirstOrDefaultAsync(su => su.UserId == userId && su.IsActive);

        if (link == null) return null;

        var school = link.School;
        var coordinatorCount = await _db.SchoolUsers
            .CountAsync(su => su.SchoolId == school.Id && su.Role == SchoolUserRole.Coordinator && su.IsActive);

        return new SchoolPortalDashboard(
            school.Name,
            school.UdiseCode,
            school.District?.Name,
            link.Role.ToString(),
            0, // student count — Phase 3
            coordinatorCount,
            0); // enrollment count — Phase 3
    }
}
