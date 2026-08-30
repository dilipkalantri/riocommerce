namespace RioCommerce.Core.Entities;

/// <summary>
/// Every outbound email/SMS/WhatsApp message gets one row here.
///
/// Fields written by NotificationService.DispatchAsync after the transport returns:
///   • Provider — "smtp" / "zeptomail" / "sendgrid" / "msg91" etc — captured from IEmailRouter
///   • Response — raw provider response body (truncated to ~2 KB at write time)
///   • DurationMs — total time spent in the transport call
///   • TriggeredBy — "Registration", "Order Placed", "Admin Test", "Resend", etc
///   • RequestPayload — the serialised EmailMessage (sender, recipient, subject, IsHtml)
///   • OriginalLogId — set when this row was created by a Resend; points to the parent log
/// </summary>
public class NotificationLog : BaseEntity
{
    public string? TemplateKey { get; set; }
    public string Channel { get; set; } = "Email";
    public string Recipient { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool IsHtml { get; set; }

    /// <summary>Sent | Failed | Resent — additional values reserved for queue/webhook integration.</summary>
    public string Status { get; set; } = "Sent";
    public string? Error { get; set; }

    /// <summary>Lower-case provider key — "smtp" / "zeptomail" / "sendgrid" / ... .</summary>
    public string? Provider { get; set; }
    /// <summary>Truncated raw provider response (~2 KB). Useful for debugging delivery failures.</summary>
    public string? Response { get; set; }
    public long? DurationMs { get; set; }

    /// <summary>Free-text trigger label — set by the caller of DispatchAsync.</summary>
    public string? TriggeredBy { get; set; }
    /// <summary>Compact JSON snapshot of the EmailMessage that was attempted.</summary>
    public string? RequestPayload { get; set; }

    /// <summary>0 on first attempt; +1 every Resend.</summary>
    public int RetryCount { get; set; }
    /// <summary>Set on Resend — points to the original log row that failed.</summary>
    public Guid? OriginalLogId { get; set; }
}
