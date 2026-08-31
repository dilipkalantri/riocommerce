using Microsoft.EntityFrameworkCore;
using RioCommerce.Core.DTOs.School;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;

namespace RioCommerce.Infrastructure.Services;

public class SchoolRegistrationService(
    RioCommerceDbContext db,
    IVerificationService verification) : ISchoolRegistrationService
{
    private readonly RioCommerceDbContext _db = db;
    private readonly IVerificationService _verification = verification;

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

        var send = await _verification.SendAsync(
            VerificationPurpose.SchoolRegistration,
            VerificationChannel.Email,
            user.Email!,
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

        return (true, null, user.Id);
    }

    public async Task<(bool ok, string? error)> ResendOtpAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLower();
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);

        if (user == null)
            return (true, null); // don't reveal non-existence

        var send = await _verification.SendAsync(
            VerificationPurpose.SchoolRegistration,
            VerificationChannel.Email,
            user.Email!,
            user.Id,
            user.FullName);

        return (send.Success, send.ErrorMessage);
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
