namespace RioCommerce.Core.DTOs.Admin;

public sealed class EmailLogFilter
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
    public string? TemplateKey { get; set; }
    public string? Provider { get; set; }
    public string? Status { get; set; }
    public string? TriggeredBy { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed record EmailLogRow(
    Guid Id,
    DateTime SentAt,
    string Recipient,
    string? Subject,
    string? TemplateKey,
    string? Provider,
    string Status,
    string? Response,
    string? TriggeredBy,
    long? DurationMs);

public sealed record EmailLogDetail(
    Guid Id,
    DateTime SentAt,
    string Recipient,
    string? Subject,
    string? Body,
    bool IsHtml,
    string? TemplateKey,
    string Channel,
    string? Provider,
    string Status,
    string? Error,
    string? Response,
    long? DurationMs,
    string? TriggeredBy,
    string? RequestPayload,
    int RetryCount,
    Guid? OriginalLogId);

public sealed record EmailLogStats(
    int Total,
    int Delivered,
    int Failed,
    int Pending,
    int Today,
    decimal SuccessRatePct,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    string? LastSuccessResponse,
    string? LastFailureResponse,
    string? LastFailureError);

public sealed record EmailLogPage(IReadOnlyList<EmailLogRow> Items, int TotalCount, int Page, int PageSize);

public sealed record EmailResendResult(bool Ok, string? Error, Guid? NewLogId);
