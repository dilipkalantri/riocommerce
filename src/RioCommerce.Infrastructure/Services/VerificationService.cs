using System.Security.Cryptography;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// OTP issue + verify for registration flows. Codes are 6 random digits, stored as a BCrypt hash,
/// valid for <see cref="CodeLifetime"/>, and locked after <see cref="MaxAttempts"/> wrong tries.
/// Delivery uses the existing email router / SMS sender directly so the code goes to exactly the
/// one channel the user picked (not fanned across channels like template notifications).
/// </summary>
public class VerificationService : IVerificationService
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 5;

    // How many codes one address/number may be sent for a given purpose before it has to wait.
    // None of the issuing endpoints require the caller to already hold the account — forgot-password
    // and legacy password setup both take a bare identifier — so without a cap any of them can be
    // driven in a loop to bomb a stranger's inbox. Counted off the stored rows rather than memory so
    // it survives a restart and applies across every entry point at once. Five in a quarter of an
    // hour leaves plenty of room for a customer who genuinely mistypes or loses the first mail.
    private static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(15);
    private const int MaxSendsPerWindow = 5;

    private readonly RioCommerceDbContext _db;
    private readonly IEmailRouter _email;
    private readonly ISmsSender _sms;
    private readonly ILogger<VerificationService> _log;

    public VerificationService(
        RioCommerceDbContext db,
        IEmailRouter email,
        ISmsSender sms,
        ILogger<VerificationService> log)
    {
        _db = db;
        _email = email;
        _sms = sms;
        _log = log;
    }

    public async Task<VerificationSendResult> SendAsync(
        VerificationPurpose purpose, VerificationChannel channel, string target,
        Guid? subjectId, string recipientName, CancellationToken ct = default)
    {
        target = Normalise(channel, target);
        if (string.IsNullOrWhiteSpace(target))
            return VerificationSendResult.Fail("A valid email or phone is required.");

        if (await IsSendThrottledAsync(purpose, target, ct))
            return VerificationSendResult.Fail(ThrottleMessage);

        // Invalidate any previous live codes for this (purpose, target) so only the newest works.
        var prior = await _db.VerificationCodes
            .Where(v => v.Purpose == purpose && v.Target == target && v.ConsumedAt == null)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var p in prior) { p.ConsumedAt = now; p.UpdatedAt = now; }

        var code = GenerateCode();
        var entity = new VerificationCode
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            Channel = channel,
            Target = target,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            SubjectId = subjectId,
            ExpiresAt = now.Add(CodeLifetime),
            AttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.VerificationCodes.Add(entity);
        await _db.SaveChangesAsync(ct);

        // Deliver on the chosen channel.
        var (ok, error) = await DeliverAsync(channel, target, recipientName, code, purpose, ct);
        if (!ok)
        {
            _log.LogError("Verification send failed purpose={Purpose} channel={Channel} err={Err}", purpose, channel, error);
            return VerificationSendResult.Fail(error ?? "Could not send the verification code. Please try again.");
        }

        _log.LogInformation("Verification code sent purpose={Purpose} channel={Channel}", purpose, channel);
        return VerificationSendResult.Ok(entity.ExpiresAt);
    }

    public async Task<VerificationSendResult> SendBothAsync(
        VerificationPurpose purpose, string? email, string? phone,
        Guid? subjectId, string recipientName, CancellationToken ct = default)
    {
        var emailTarget = Normalise(VerificationChannel.Email, email ?? string.Empty);
        var phoneTarget = Normalise(VerificationChannel.Sms, phone ?? string.Empty);
        if (string.IsNullOrWhiteSpace(emailTarget) && string.IsNullOrWhiteSpace(phoneTarget))
            return VerificationSendResult.Fail("A valid email or phone is required.");

        if (await IsSendThrottledAsync(purpose, emailTarget, ct) ||
            await IsSendThrottledAsync(purpose, phoneTarget, ct))
            return VerificationSendResult.Fail(ThrottleMessage);

        var now = DateTime.UtcNow;

        // Invalidate any prior live codes for this purpose against either target.
        var prior = await _db.VerificationCodes
            .Where(v => v.Purpose == purpose && v.ConsumedAt == null
                        && (v.Target == emailTarget || v.Target == phoneTarget))
            .ToListAsync(ct);
        foreach (var p in prior) { p.ConsumedAt = now; p.UpdatedAt = now; }

        // ONE code, ONE hash — shared across both channel rows so entering it against either works.
        var code = GenerateCode();
        var hash = BCrypt.Net.BCrypt.HashPassword(code);
        var expiresAt = now.Add(CodeLifetime);

        if (!string.IsNullOrWhiteSpace(emailTarget))
            _db.VerificationCodes.Add(new VerificationCode
            {
                Id = Guid.NewGuid(), Purpose = purpose, Channel = VerificationChannel.Email,
                Target = emailTarget, CodeHash = hash, SubjectId = subjectId,
                ExpiresAt = expiresAt, AttemptCount = 0, CreatedAt = now, UpdatedAt = now,
            });
        if (!string.IsNullOrWhiteSpace(phoneTarget))
            _db.VerificationCodes.Add(new VerificationCode
            {
                Id = Guid.NewGuid(), Purpose = purpose, Channel = VerificationChannel.Sms,
                Target = phoneTarget, CodeHash = hash, SubjectId = subjectId,
                ExpiresAt = expiresAt, AttemptCount = 0, CreatedAt = now, UpdatedAt = now,
            });
        await _db.SaveChangesAsync(ct);

        // Deliver to whichever channels we have a target for. Succeed if at least one lands.
        var delivered = 0;
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(emailTarget))
        {
            var (ok, err) = await DeliverAsync(VerificationChannel.Email, emailTarget, recipientName, code, purpose, ct);
            if (ok) delivered++; else errors.Add($"email: {err}");
        }
        if (!string.IsNullOrWhiteSpace(phoneTarget))
        {
            var (ok, err) = await DeliverAsync(VerificationChannel.Sms, phoneTarget, recipientName, code, purpose, ct);
            if (ok) delivered++; else errors.Add($"sms: {err}");
        }

        if (delivered == 0)
        {
            _log.LogError("Verification send-both failed purpose={Purpose} errors={Err}", purpose, string.Join("; ", errors));
            return VerificationSendResult.Fail("Could not send the verification code on any channel. " + string.Join("; ", errors));
        }

        if (errors.Count > 0)
            _log.LogWarning("Verification send-both partial purpose={Purpose} errors={Err}", purpose, string.Join("; ", errors));
        else
            _log.LogInformation("Verification code sent to both channels purpose={Purpose}", purpose);

        return VerificationSendResult.Ok(expiresAt);
    }

    public async Task<VerificationCheckResult> VerifyAsync(
        VerificationPurpose purpose, string target, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return VerificationCheckResult.Fail("Enter the 6-digit code.");

        // Match on (purpose, target) for either channel form — normalise both ways and try.
        var emailTarget = Normalise(VerificationChannel.Email, target);
        var smsTarget = Normalise(VerificationChannel.Sms, target);

        var entity = await _db.VerificationCodes
            .Where(v => v.Purpose == purpose
                        && v.ConsumedAt == null
                        && (v.Target == emailTarget || v.Target == smsTarget))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (entity == null)
            return VerificationCheckResult.Fail("No active verification code found. Request a new one.");

        var now = DateTime.UtcNow;
        if (entity.ExpiresAt < now)
            return VerificationCheckResult.Fail("This code has expired. Request a new one.");

        if (entity.AttemptCount >= MaxAttempts)
            return VerificationCheckResult.Fail("Too many incorrect attempts. Request a new code.");

        if (!BCrypt.Net.BCrypt.Verify(code.Trim(), entity.CodeHash))
        {
            entity.AttemptCount++;
            entity.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            var left = Math.Max(0, MaxAttempts - entity.AttemptCount);
            return VerificationCheckResult.Fail($"Incorrect code. {left} attempt(s) left.");
        }

        entity.ConsumedAt = now;
        entity.UpdatedAt = now;

        // If this code was sent to both channels (two rows, same hash), consume the sibling too so
        // it can't be reused on the other channel.
        var siblings = await _db.VerificationCodes
            .Where(v => v.Purpose == purpose && v.ConsumedAt == null && v.Id != entity.Id
                        && (v.Target == emailTarget || v.Target == smsTarget))
            .ToListAsync(ct);
        foreach (var s in siblings)
        {
            if (BCrypt.Net.BCrypt.Verify(code.Trim(), s.CodeHash))
            {
                s.ConsumedAt = now;
                s.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
        return VerificationCheckResult.Ok(entity.SubjectId);
    }

    public async Task<VerificationCheckResult> PeekAsync(
        VerificationPurpose purpose, string target, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return VerificationCheckResult.Fail("Enter the 6-digit code.");

        var emailTarget = Normalise(VerificationChannel.Email, target);
        var smsTarget = Normalise(VerificationChannel.Sms, target);

        var entity = await _db.VerificationCodes
            .Where(v => v.Purpose == purpose
                        && v.ConsumedAt == null
                        && (v.Target == emailTarget || v.Target == smsTarget))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (entity == null)
            return VerificationCheckResult.Fail("No active verification code found. Request a new one.");
        if (entity.ExpiresAt < DateTime.UtcNow)
            return VerificationCheckResult.Fail("This code has expired. Request a new one.");
        if (entity.AttemptCount >= MaxAttempts)
            return VerificationCheckResult.Fail("Too many incorrect attempts. Request a new code.");

        // Read-only validation — does NOT consume or increment attempts. The authoritative
        // consume happens later in VerifyAsync when the new password is submitted.
        if (!BCrypt.Net.BCrypt.Verify(code.Trim(), entity.CodeHash))
            return VerificationCheckResult.Fail("Incorrect code. Please check and try again.");

        return VerificationCheckResult.Ok(entity.SubjectId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string ThrottleMessage =>
        $"Too many code requests for this address. Please wait {SendWindow.TotalMinutes:N0} minutes and try again.";

    /// <summary>
    /// True when <paramref name="target"/> has already been sent <see cref="MaxSendsPerWindow"/>
    /// codes for this purpose inside <see cref="SendWindow"/>. Blank targets are never throttled —
    /// SendBothAsync passes one empty side whenever the subject has only an email or only a phone.
    /// </summary>
    private async Task<bool> IsSendThrottledAsync(
        VerificationPurpose purpose, string target, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;

        var windowStart = DateTime.UtcNow - SendWindow;
        var recent = await _db.VerificationCodes
            .CountAsync(v => v.Purpose == purpose && v.Target == target && v.CreatedAt >= windowStart, ct);

        if (recent < MaxSendsPerWindow) return false;

        _log.LogWarning("Verification send throttled purpose={Purpose} sends={Count}", purpose, recent);
        return true;
    }

    private async Task<(bool ok, string? error)> DeliverAsync(
        VerificationChannel channel, string target, string name, string code,
        VerificationPurpose purpose, CancellationToken ct)
    {
        var context = purpose switch
        {
            VerificationPurpose.FranchiseeApplication => "franchise application",
            VerificationPurpose.PasswordReset => "password reset",
            VerificationPurpose.LegacyPasswordSetup => "setting up your password",
            _ => "registration",
        };

        if (channel == VerificationChannel.Email)
        {
            var subject = $"Your RioCommerce verification code: {code}";
            // Migrated customers never asked for this code — they just tried to log in with their old
            // credentials. Without a line explaining why, the mail looks unsolicited and gets ignored.
            var preamble = purpose == VerificationPurpose.LegacyPasswordSetup
                ? "Your RioCommerce account has moved to our new website. Your old password could not be " +
                  "carried across, so please set a new one.\n\n"
                : string.Empty;
            var body =
                $"Hi {name},\n\n" +
                preamble +
                $"Your verification code for {context} is: {code}\n\n" +
                $"It expires in {CodeLifetime.TotalMinutes:N0} minutes. If you didn't request this, ignore this email.\n\n" +
                "— RioCommerce";
            var result = await _email.SendAsync(new EmailMessage(target, name, subject, body), ct);
            return (result.Ok, result.Error);
        }

        var smsBody = $"RioCommerce: your verification code is {code}. Valid for {CodeLifetime.TotalMinutes:N0} min.";
        return await _sms.SendAsync(target, smsBody);
    }

    private static string GenerateCode()
    {
        // Cryptographically-strong 6-digit code, zero-padded.
        var n = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return n.ToString("D6");
    }

    private static string Normalise(VerificationChannel channel, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        target = target.Trim();
        return channel == VerificationChannel.Email
            ? target.ToLowerInvariant()
            : new string(target.Where(char.IsDigit).ToArray());
    }
}
