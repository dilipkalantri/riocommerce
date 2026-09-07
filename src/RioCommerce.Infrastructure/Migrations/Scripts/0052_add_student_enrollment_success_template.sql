-- 0052 — "student_enrollment_success" message template (Email).
--
-- Fires from CheckoutService.SendSchoolEnrollmentNotificationsAsync after a school-purchased
-- enrolment is FRESHLY confirmed (never on registration/add-student/payment-initiation/failure,
-- and never twice on a replayed webhook — see the ConfirmedAt idempotency gate in
-- CheckoutService.GrantSchoolEnrollmentAsync). Previously this HTML lived hard-coded in
-- CheckoutService as BuildEnrollmentEmailHtml; this row is now the single source of truth and
-- CheckoutService resolves/sends it through the existing NotificationService/{{token}} pipeline,
-- the same one every other transactional email (order_confirmation, franchisee_registered, ...)
-- already uses. No new template engine, no schema change.
--
-- {{password_line}} is pre-composed in code (never a template conditional, since the renderer
-- does plain {{token}} substitution with no branching) so a principal-added student with no
-- password yet is pointed at the existing Forgot Password / OTP flow, and a self-registered
-- student is told to use their existing password — the exact same secure behaviour as before.
--
-- Idempotent: ON CONFLICT on the (Key, Channel) unique index leaves an existing/edited template
-- alone, so re-running this script (or re-seeding) never overwrites wording an admin has changed.
-- Email only, deliberately — SMS for this event stays dormant until a DLT-approved template
-- exists (CheckoutService.EnrollmentSuccessSmsTemplate), unrelated to this row.

INSERT INTO message_templates ("Id", "Key", "Name", "Channel", "Subject", "Body", "IsActive")
VALUES (
    gen_random_uuid(),
    'student_enrollment_success',
    'Student — Enrollment Success (Email)',
    'Email',
    'Vijaypath – Your Learner Account & Course Access Details',
    '<!doctype html>
<html lang="en">
<body style="margin:0;padding:0;background:#F0F2F5;font-family:Arial,Helvetica,sans-serif;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F0F2F5;padding:24px 12px;">
    <tr><td align="center">
      <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:#FFFFFF;border-radius:12px;overflow:hidden;border:1px solid #E5E7EB;">

        <!-- Header -->
        <tr>
          <td style="background:#1A3A5C;padding:22px 28px;">
            <span style="font-size:22px;font-weight:700;color:#FFFFFF;letter-spacing:.3px;">Vijaypath</span>
          </td>
        </tr>

        <!-- Body -->
        <tr>
          <td style="padding:28px;">
            <p style="margin:0 0 14px;font-size:16px;color:#1A1A1A;">Hi {{name}},</p>
            <p style="margin:0 0 22px;font-size:15px;color:#374151;line-height:1.6;">
              You have been successfully registered as a Learner on Vijaypath.
            </p>

            <!-- Login details -->
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F9FAFB;border:1px solid #E5E7EB;border-radius:10px;margin-bottom:20px;">
              <tr><td style="padding:16px 18px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#1A3A5C;text-transform:uppercase;letter-spacing:.4px;">Login Details</p>
                <p style="margin:0 0 6px;font-size:14.5px;color:#1A1A1A;"><strong>Login ID:</strong> {{login_id}}</p>
                <p style="margin:0;font-size:14.5px;color:#1A1A1A;"><strong>Password:</strong> {{password_line}}</p>
              </td></tr>
            </table>

            <p style="margin:0 0 22px;font-size:14.5px;color:#374151;line-height:1.6;">
              Please sign in to Vijaypath using the above credentials and change your password from your profile.
            </p>

            <!-- Course assigned -->
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F9FAFB;border:1px solid #E5E7EB;border-radius:10px;margin-bottom:24px;">
              <tr><td style="padding:16px 18px;">
                <p style="margin:0 0 10px;font-size:13px;font-weight:700;color:#1A3A5C;text-transform:uppercase;letter-spacing:.4px;">Course Assigned</p>
                <p style="margin:0 0 6px;font-size:14.5px;color:#1A1A1A;"><strong>Course:</strong> {{course_name}}</p>
                <p style="margin:0;font-size:14.5px;color:#1A1A1A;"><strong>Enrollment Status:</strong> {{enrollment_status}}</p>
              </td></tr>
            </table>

            <!-- CTA -->
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 auto 24px;">
              <tr><td align="center" style="background:#E8722A;border-radius:8px;">
                <a href="{{login_url}}" style="display:inline-block;padding:13px 32px;font-size:15px;font-weight:700;color:#FFFFFF;text-decoration:none;">Login to Vijaypath</a>
              </td></tr>
            </table>

            <p style="margin:0 0 18px;font-size:14px;color:#374151;line-height:1.6;">
              We will send you the mobile app link and access details by 15th September.
            </p>

            <p style="margin:0 0 18px;font-size:14px;color:#374151;line-height:1.6;">
              Please keep your login credentials safe and do not share them with anyone.
            </p>

            <p style="margin:0 0 22px;font-size:14px;color:#374151;line-height:1.6;">
              If you face any difficulty accessing your account, please contact the Vijaypath support team.
            </p>

            <p style="margin:0;font-size:15px;color:#1A1A1A;">Happy Learning! &#127891;</p>
          </td>
        </tr>

        <!-- Footer -->
        <tr>
          <td style="background:#F9FAFB;border-top:1px solid #E5E7EB;padding:20px 28px;">
            <p style="margin:0 0 6px;font-size:13.5px;font-weight:700;color:#1A3A5C;">Vijaypath Team</p>
            <p style="margin:0 0 3px;font-size:12.5px;color:#6B7280;">&#128222; {{support_mobile}}</p>
            <p style="margin:0 0 3px;font-size:12.5px;color:#6B7280;">&#128231; {{support_email}}</p>
            <p style="margin:0;font-size:12.5px;color:#6B7280;">&#127760; {{website_url}}</p>
          </td>
        </tr>

      </table>
    </td></tr>
  </table>
</body>
</html>',
    true
)
ON CONFLICT ("Key", "Channel") DO NOTHING;
