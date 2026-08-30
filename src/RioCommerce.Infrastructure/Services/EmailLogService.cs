using System.Text;
using ClosedXML.Excel;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public sealed class EmailLogService : IEmailLogService
{
    private readonly RioCommerceDbContext _db;
    private readonly IEmailRouter _email;

    public EmailLogService(RioCommerceDbContext db, IEmailRouter email)
    {
        _db = db;
        _email = email;
    }

    // ─── Filter + paged list ────────────────────────────────────────────────
    public async Task<EmailLogPage> ListAsync(EmailLogFilter filter, CancellationToken ct = default)
    {
        var q = Filtered(filter);
        var total = await q.CountAsync(ct);

        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 1, 200);

        var rows = await q
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(l => new EmailLogRow(
                l.Id, l.CreatedAt, l.Recipient, l.Subject, l.TemplateKey,
                l.Provider, l.Status, l.Response, l.TriggeredBy, l.DurationMs))
            .ToListAsync(ct);

        return new EmailLogPage(rows, total, page, size);
    }

    public async Task<EmailLogDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var l = await _db.Set<NotificationLog>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l == null) return null;
        return new EmailLogDetail(
            l.Id, l.CreatedAt, l.Recipient, l.Subject, l.Body, l.IsHtml,
            l.TemplateKey, l.Channel, l.Provider, l.Status, l.Error,
            l.Response, l.DurationMs, l.TriggeredBy, l.RequestPayload,
            l.RetryCount, l.OriginalLogId);
    }

    // ─── Resend ────────────────────────────────────────────────────────────
    // Reuses the original rendered subject + body. The new row records its parent
    // via OriginalLogId so the admin can trace retry chains in the grid.
    public async Task<EmailResendResult> ResendAsync(Guid originalId, CancellationToken ct = default)
    {
        var original = await _db.Set<NotificationLog>().FirstOrDefaultAsync(x => x.Id == originalId, ct);
        if (original == null) return new EmailResendResult(false, "Original log not found.", null);
        if (!string.Equals(original.Channel, "Email", StringComparison.OrdinalIgnoreCase))
            return new EmailResendResult(false, "Only Email rows can be resent.", null);
        if (string.IsNullOrWhiteSpace(original.Recipient))
            return new EmailResendResult(false, "Original log has no recipient.", null);

        var msg = new EmailMessage(
            ToEmail: original.Recipient,
            ToName: null,
            Subject: original.Subject ?? string.Empty,
            Body: original.Body ?? string.Empty,
            IsHtml: original.IsHtml);

        var provider = await _email.GetActiveProviderKeyAsync(ct);
        var result = await _email.SendAsync(msg, ct);

        var newLog = new NotificationLog
        {
            TemplateKey   = original.TemplateKey,
            Channel       = original.Channel,
            Recipient     = original.Recipient,
            Subject       = original.Subject,
            Body          = original.Body,
            IsHtml        = original.IsHtml,
            Status        = result.Ok ? "Sent" : "Failed",
            Error         = Truncate(result.Error, 500),
            Provider      = provider,
            Response      = Truncate(result.Response, 2000),
            DurationMs    = result.DurationMs,
            TriggeredBy   = "Resend",
            RequestPayload = original.RequestPayload,
            RetryCount    = original.RetryCount + 1,
            OriginalLogId = original.Id,
        };
        _db.Set<NotificationLog>().Add(newLog);
        if (result.Ok && string.Equals(original.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            original.Status = "Resent";   // mark the parent so the grid doesn't keep nagging
        await _db.SaveChangesAsync(ct);

        return new EmailResendResult(result.Ok, result.Error, newLog.Id);
    }

    // ─── Stats ─────────────────────────────────────────────────────────────
    public async Task<EmailLogStats> StatsAsync(CancellationToken ct = default)
    {
        var q = _db.Set<NotificationLog>().Where(l => l.Channel == "Email");

        var total     = await q.CountAsync(ct);
        var delivered = await q.CountAsync(l => l.Status == "Sent" || l.Status == "Delivered" || l.Status == "Resent", ct);
        var failed    = await q.CountAsync(l => l.Status == "Failed" || l.Status == "Rejected", ct);
        var pending   = await q.CountAsync(l => l.Status == "Queued" || l.Status == "Sending" || l.Status == "Retry Pending", ct);

        var since = DateTime.UtcNow.Date;
        var today = await q.CountAsync(l => l.CreatedAt >= since, ct);

        var lastSuccess = await q
            .Where(l => l.Status == "Sent" || l.Status == "Delivered" || l.Status == "Resent")
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.CreatedAt, l.Response })
            .FirstOrDefaultAsync(ct);

        var lastFailure = await q
            .Where(l => l.Status == "Failed" || l.Status == "Rejected")
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.CreatedAt, l.Response, l.Error })
            .FirstOrDefaultAsync(ct);

        var success = total == 0 ? 0m : Math.Round((decimal)delivered * 100 / total, 1);

        return new EmailLogStats(
            total, delivered, failed, pending, today, success,
            lastSuccess?.CreatedAt, lastFailure?.CreatedAt,
            lastSuccess?.Response, lastFailure?.Response, lastFailure?.Error);
    }

    // ─── Exports ───────────────────────────────────────────────────────────
    public async Task<byte[]> ExportXlsxAsync(EmailLogFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).OrderByDescending(l => l.CreatedAt).Take(10_000).ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Email Logs");
        ws.Row(1).Style.Font.Bold = true;
        ws.Range("A1:I1").Style.Fill.BackgroundColor = XLColor.FromHtml("#0F2D5C");
        ws.Range("A1:I1").Style.Font.FontColor = XLColor.White;

        ws.Cell(1, 1).Value = "Sent (UTC)";
        ws.Cell(1, 2).Value = "Recipient";
        ws.Cell(1, 3).Value = "Subject";
        ws.Cell(1, 4).Value = "Template";
        ws.Cell(1, 5).Value = "Provider";
        ws.Cell(1, 6).Value = "Status";
        ws.Cell(1, 7).Value = "Triggered By";
        ws.Cell(1, 8).Value = "Duration (ms)";
        ws.Cell(1, 9).Value = "Response";

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i]; var row = i + 2;
            ws.Cell(row, 1).Value = r.CreatedAt;
            ws.Cell(row, 2).Value = r.Recipient;
            ws.Cell(row, 3).Value = r.Subject ?? "";
            ws.Cell(row, 4).Value = r.TemplateKey ?? "";
            ws.Cell(row, 5).Value = r.Provider ?? "";
            ws.Cell(row, 6).Value = r.Status;
            ws.Cell(row, 7).Value = r.TriggeredBy ?? "";
            // Renamed from `ms` to avoid CS0136 — the MemoryStream below also wants `ms`.
            if (r.DurationMs is long durMs) ws.Cell(row, 8).Value = durMs; else ws.Cell(row, 8).Value = "";
            ws.Cell(row, 9).Value = r.Response ?? "";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> ExportCsvAsync(EmailLogFilter filter, CancellationToken ct = default)
    {
        var rows = await Filtered(filter).OrderByDescending(l => l.CreatedAt).Take(50_000).ToListAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("Sent (UTC),Recipient,Subject,Template,Provider,Status,Triggered By,Duration (ms),Response");
        foreach (var r in rows)
        {
            sb.Append(r.CreatedAt.ToString("u")).Append(',')
              .Append(Csv(r.Recipient)).Append(',')
              .Append(Csv(r.Subject)).Append(',')
              .Append(Csv(r.TemplateKey)).Append(',')
              .Append(Csv(r.Provider)).Append(',')
              .Append(Csv(r.Status)).Append(',')
              .Append(Csv(r.TriggeredBy)).Append(',')
              .Append(r.DurationMs?.ToString() ?? "").Append(',')
              .Append(Csv(r.Response))
              .AppendLine();
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ─── Helpers ───────────────────────────────────────────────────────────
    private IQueryable<NotificationLog> Filtered(EmailLogFilter f)
    {
        var q = _db.Set<NotificationLog>().Where(l => l.Channel == "Email");
        if (f.From is { } from) q = q.Where(l => l.CreatedAt >= from);
        if (f.To   is { } to)   q = q.Where(l => l.CreatedAt <= to);
        if (!string.IsNullOrWhiteSpace(f.Recipient))   q = q.Where(l => l.Recipient.Contains(f.Recipient!));
        if (!string.IsNullOrWhiteSpace(f.Subject))     q = q.Where(l => l.Subject != null && l.Subject.Contains(f.Subject!));
        if (!string.IsNullOrWhiteSpace(f.TemplateKey)) q = q.Where(l => l.TemplateKey == f.TemplateKey);
        if (!string.IsNullOrWhiteSpace(f.Provider))    q = q.Where(l => l.Provider == f.Provider);
        if (!string.IsNullOrWhiteSpace(f.Status))      q = q.Where(l => l.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.TriggeredBy)) q = q.Where(l => l.TriggeredBy == f.TriggeredBy);
        return q;
    }

    private static string Csv(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var needsQuotes = s.Contains(',') || s.Contains('"') || s.Contains('\n');
        var v = s.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{v}\"" : v;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}
