using RioCommerce.Core.DTOs.Auth;
using RioCommerce.Core.DTOs.Common;
using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RioCommerce.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly RioCommerceDbContext _db;
    private readonly IConfiguration _config;
    private readonly ICustomerDuplicateService _dupes;
    private readonly IVerificationService _verify;

    public AuthController(RioCommerceDbContext db, IConfiguration config, ICustomerDuplicateService dupes, IVerificationService verify)
    {
        _db = db;
        _config = config;
        _dupes = dupes;
        _verify = verify;
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        // Find user by email or phone
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u =>
                (u.Email != null && u.Email.ToLower() == request.EmailOrPhone.ToLower()) ||
                (u.Phone != null && u.Phone == request.EmailOrPhone));

        if (user == null)
            return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid email/phone or password"));

        // A customer imported from the old website has no hash to check — the old site's passwords
        // were not migrated. Say so plainly instead of "invalid password", which would be a dead end
        // on credentials they know are right. The legacy_user_map row is what tells this apart from
        // any other account holding no hash. Browsers get steered to /set-password by the cookie
        // login endpoint; API clients get this message and should send the customer there.
        if (string.IsNullOrEmpty(user.PasswordHash)
            && user.Email != null
            && await _db.LegacyUserMaps.AnyAsync(m => m.NewId == user.Id))
            return Unauthorized(ApiResponse<AuthResponse>.Fail(
                "Your account has moved to our new website and needs a new password. " +
                "Open the login page and sign in there to set one."));

        // Verify password. BCrypt.Verify throws on a malformed hash rather than returning false,
        // which would surface as a 500 on a bad row — treat any parse failure as a wrong password.
        bool passwordOk;
        try
        {
            passwordOk = !string.IsNullOrEmpty(user.PasswordHash)
                         && BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        }
        catch { passwordOk = false; }

        if (!passwordOk)
            return Unauthorized(ApiResponse<AuthResponse>.Fail("Invalid email/phone or password"));

        if (!user.IsActive)
            return Unauthorized(ApiResponse<AuthResponse>.Fail(
                user.IsVerified
                    ? "Account is deactivated"
                    : "Your account isn't verified yet. Please complete the verification code we sent you."));

        // Update login info
        user.LastLoginAt = DateTime.UtcNow;
        user.LoginCount++;
        await _db.SaveChangesAsync();

        // Generate JWT
        var token = GenerateJwt(user);
        var roles = user.UserRoles.Where(ur => ur.IsActive).Select(ur => ur.Role.Name).ToList();

        var response = new AuthResponse(
            token,
            DateTime.UtcNow.AddMinutes(double.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "60")),
            new UserInfo(user.Id, user.FullName, user.Email, user.Phone, roles)
        );

        return Ok(ApiResponse<AuthResponse>.Ok(response, "Login successful"));
    }

    [HttpPost("register")]
    public async Task<ActionResult> Register([FromBody] RegisterRequest request)
    {
        // Centralised duplicate probe — returns the exact field that collided and a redacted
        // summary of the existing customer for the UI to render the "Already exists" panel.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var dup = await _dupes.CheckAsync(request.Email, request.Phone);
        if (dup.HasDuplicate)
        {
            await _dupes.LogDuplicateAttemptAsync(request.Email, request.Phone, "API",
                null, request.FullName, ip, dup.FieldName);
            return Conflict(new DuplicateApiResponse(
                Success: false,
                Message: BuildDuplicateMessage(dup.Field),
                DuplicateField: dup.FieldName,
                Existing: dup.Existing));
        }

        // Create user — set Id up-front so the UserRole foreign key is valid at save time.
        // Inactive + unverified until the customer confirms the OTP we send below; login blocks
        // on !IsActive, so the account can't be used until verification completes.
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            City = request.City,
            IsActive = false,
            IsVerified = false
        };

        _db.Users.Add(user);

        // Assign student role
        var studentRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "student");
        if (studentRole != null)
        {
            _db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = studentRole.Id,
                IsActive = true
            });
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: two concurrent registers slipped past CheckAsync and hit the unique index.
            var raceDup = await _dupes.CheckAsync(request.Email, request.Phone);
            await _dupes.LogDuplicateAttemptAsync(request.Email, request.Phone, "API-Race",
                null, request.FullName, ip, raceDup.FieldName);
            return Conflict(new DuplicateApiResponse(
                Success: false,
                Message: BuildDuplicateMessage(raceDup.Field),
                DuplicateField: raceDup.FieldName,
                Existing: raceDup.Existing));
        }

        // Send ONE 6-digit code to BOTH the email and phone the customer gave. Verifying the code
        // against either channel completes registration.
        var send = await _verify.SendBothAsync(
            RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup,
            user.Email, user.Phone, user.Id, user.FullName);

        if (!send.Success)
        {
            // Account exists but we couldn't deliver the code on any channel — let the client resend.
            return Ok(ApiResponse<object>.Ok(
                new { verificationRequired = true, channels = new[] { "email", "sms" }, email = user.Email, phone = user.Phone, codeSent = false },
                "Account created but we couldn't send the code. Use resend to try again."));
        }

        return Ok(ApiResponse<object>.Ok(
            new { verificationRequired = true, channels = new[] { "email", "sms" }, email = user.Email, phone = user.Phone, codeSent = true, expiresAt = send.ExpiresAt },
            "Verification code sent to your email and phone. Enter it to activate your account."));
    }

    /// <summary>Confirms a registration OTP. On success the account is activated and a JWT is issued.</summary>
    [HttpPost("verify-registration")]
    public async Task<ActionResult> VerifyRegistration([FromBody] VerifyRegistrationRequest request)
    {
        var check = await _verify.VerifyAsync(
            RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup, request.Target, request.Code);
        if (!check.Success)
            return BadRequest(ApiResponse<object>.Fail(check.ErrorMessage ?? "Verification failed."));

        var user = check.SubjectId is { } sid
            ? await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                .FirstOrDefaultAsync(u => u.Id == sid)
            : null;
        if (user == null)
            return BadRequest(ApiResponse<object>.Fail("Account not found for this verification."));

        user.IsVerified = true;
        user.IsActive = true;
        await _db.SaveChangesAsync();

        var roles = user.UserRoles.Where(ur => ur.IsActive).Select(ur => ur.Role.Name).ToList();
        var token = GenerateJwt(user);
        var response = new AuthResponse(
            token,
            DateTime.UtcNow.AddHours(1),
            new UserInfo(user.Id, user.FullName, user.Email, user.Phone, roles));
        return Ok(ApiResponse<AuthResponse>.Ok(response, "Account verified. You're all set."));
    }

    /// <summary>Re-sends the registration OTP to both the account's email and phone.</summary>
    [HttpPost("resend-code")]
    public async Task<ActionResult> ResendCode([FromBody] ResendCodeRequest request)
    {
        // Resolve the pending account by the supplied target so we can re-attach SubjectId + name.
        var normalized = request.Target.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == normalized) || u.Phone == request.Target.Trim());
        if (user == null)
            return BadRequest(ApiResponse<object>.Fail("No pending registration found for that contact."));
        if (user.IsActive)
            return BadRequest(ApiResponse<object>.Fail("This account is already verified."));

        var send = await _verify.SendBothAsync(
            RioCommerce.Core.Enums.VerificationPurpose.CustomerSignup, user.Email, user.Phone, user.Id, user.FullName);
        return send.Success
            ? Ok(ApiResponse<object>.Ok(new { codeSent = true, expiresAt = send.ExpiresAt }, "A new code is on its way to your email and phone."))
            : BadRequest(ApiResponse<object>.Fail(send.ErrorMessage ?? "Could not resend the code."));
    }

    /// <summary>Starts a password reset: sends a 6-digit code to the account's email or phone.
    /// Always returns success regardless of whether the account exists, to avoid revealing which
    /// emails/phones are registered (account-enumeration protection).</summary>
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var channel = string.Equals(request.Channel, "sms", StringComparison.OrdinalIgnoreCase)
            ? RioCommerce.Core.Enums.VerificationChannel.Sms
            : RioCommerce.Core.Enums.VerificationChannel.Email;

        var raw = request.Target.Trim();
        var normalized = raw.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            (u.Email != null && u.Email.ToLower() == normalized) || u.Phone == raw);

        // Only actually send when the account exists AND has the chosen contact channel.
        if (user != null)
        {
            var target = channel == RioCommerce.Core.Enums.VerificationChannel.Sms ? user.Phone : user.Email;
            if (!string.IsNullOrWhiteSpace(target))
            {
                await _verify.SendAsync(
                    RioCommerce.Core.Enums.VerificationPurpose.PasswordReset,
                    channel, target!, user.Id, user.FullName);
            }
        }

        // Uniform response — never reveal whether the contact is registered.
        return Ok(ApiResponse<object>.Ok(
            new { codeSent = true },
            "If that account exists, a reset code has been sent."));
    }

    /// <summary>Completes a password reset: verifies the code and sets the new password.</summary>
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest(ApiResponse<object>.Fail("Password must be at least 6 characters."));

        var check = await _verify.VerifyAsync(
            RioCommerce.Core.Enums.VerificationPurpose.PasswordReset, request.Target, request.Code);
        if (!check.Success)
            return BadRequest(ApiResponse<object>.Fail(check.ErrorMessage ?? "Verification failed."));

        var user = check.SubjectId is { } sid
            ? await _db.Users.FirstOrDefaultAsync(u => u.Id == sid)
            : null;
        if (user == null)
            return BadRequest(ApiResponse<object>.Fail("Account not found for this reset."));

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new { reset = true }, "Password updated. You can now sign in with your new password."));
    }

    /// <summary>Public inline-availability probe for the email field. Used by the live
    /// blur-validation on the registration form. Returns <see cref="FieldAvailability"/>.</summary>
    [HttpGet("check-email")]
    public async Task<ActionResult<ApiResponse<FieldAvailability>>> CheckEmail([FromQuery] string email)
        => Ok(ApiResponse<FieldAvailability>.Ok(await _dupes.CheckEmailAvailableAsync(email)));

    /// <summary>Public inline-availability probe for the phone field.</summary>
    [HttpGet("check-phone")]
    public async Task<ActionResult<ApiResponse<FieldAvailability>>> CheckPhone([FromQuery] string phone)
        => Ok(ApiResponse<FieldAvailability>.Ok(await _dupes.CheckPhoneAvailableAsync(phone)));

    private static string BuildDuplicateMessage(DuplicateField field) => field switch
    {
        DuplicateField.Email => "This email address is already registered. Please login or use another email address.",
        DuplicateField.Phone => "This mobile number is already linked with an existing account. Please login or use another mobile number.",
        DuplicateField.Both  => "The entered email address and mobile number are already registered. Please login to continue.",
        _                    => "Customer already exists."
    };

    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ApiResponse<UserInfo>>> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized(ApiResponse<UserInfo>.Fail("Not authenticated"));

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == Guid.Parse(userId));

        if (user == null) return NotFound(ApiResponse<UserInfo>.Fail("User not found"));

        var roles = user.UserRoles.Where(ur => ur.IsActive).Select(ur => ur.Role.Name).ToList();
        return Ok(ApiResponse<UserInfo>.Ok(new UserInfo(user.Id, user.FullName, user.Email, user.Phone, roles)));
    }

    private string GenerateJwt(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("phone", user.Phone ?? "")
        };

        // Add role claims
        if (user.UserRoles != null)
        {
            foreach (var ur in user.UserRoles.Where(ur => ur.IsActive))
                claims.Add(new Claim(ClaimTypes.Role, ur.Role.Name));
        }

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(double.Parse(_config["Jwt:AccessTokenExpiryMinutes"] ?? "60")),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
