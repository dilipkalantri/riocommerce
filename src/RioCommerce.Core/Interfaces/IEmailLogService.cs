using RioCommerce.Core.DTOs.Admin;

namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Read + replay surface for the notification_logs table, scoped to Email-channel rows
/// (the existing INotificationService still owns templates and dispatch).
///
/// Resend reuses the original rendered subject + body verbatim — no re-templating — so
/// the new row is byte-identical to the failed original.
/// </summary>
public interface IEmailLogService
{
    Task<EmailLogPage> ListAsync(EmailLogFilter filter, CancellationToken ct = default);
    Task<EmailLogDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<EmailResendResult> ResendAsync(Guid originalId, CancellationToken ct = default);
    Task<EmailLogStats> StatsAsync(CancellationToken ct = default);
    Task<byte[]> ExportXlsxAsync(EmailLogFilter filter, CancellationToken ct = default);
    Task<byte[]> ExportCsvAsync(EmailLogFilter filter, CancellationToken ct = default);
}
