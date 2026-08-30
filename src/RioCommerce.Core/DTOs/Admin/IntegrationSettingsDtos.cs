namespace RioCommerce.Core.DTOs.Admin;

// Email (SMTP) delivery settings.
public class SmtpSettings
{
    public bool Enabled { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromEmail { get; set; }
    public string? FromName { get; set; }
}

// SMS gateway settings (provider-agnostic HTTP API: MSG91 / Twilio / TextLocal / etc.).
public class SmsSettings
{
    public bool Enabled { get; set; }
    public string? Provider { get; set; }     // e.g. MSG91, Twilio, TextLocal
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
    public string? SenderId { get; set; }      // DLT sender / header
    public string? BaseUrl { get; set; }       // provider endpoint
}

// Razorpay payment gateway settings.
// Mode (test/live) is auto-detected from the Key ID prefix (rzp_test_* vs rzp_live_*),
// so there's no manual toggle to keep in sync.
public class RazorpaySettings
{
    public bool Enabled { get; set; }
    public string? KeyId { get; set; }
    public string? KeySecret { get; set; }
    public string? WebhookSecret { get; set; }
}

// Easebuzz payment gateway settings. Mirrors the reference plugin: explicit endpoint
// + explicit webhook URL the merchant pastes into Easebuzz dashboard.
public class EasebuzzSettings
{
    public const string DefaultEndPoint = "https://pay.easebuzz.in";

    public bool Enabled { get; set; }
    public string? MerchantKey { get; set; }
    public string? Salt { get; set; }
    public string? EndPoint { get; set; }
    public string? WebhookUrl { get; set; }
}

// ─── Email provider router ────────────────────────────────────────────────────
// Which transport actually carries outbound mail. Only ONE provider is active at a
// time — the router reads this on every send so a switch in admin takes effect
// without an application restart.
public static class EmailTransport
{
    public const string Smtp = "smtp";
    public const string Api  = "api";
}

public static class EmailApiProvider
{
    public const string ZeptoMail = "zeptomail";
    public const string SendGrid  = "sendgrid";
    public const string Mailgun   = "mailgun";
    public const string Brevo     = "brevo";
    public const string AwsSes    = "awsses";
    public const string Custom    = "custom";
}

public class EmailProviderSettings
{
    /// <summary><see cref="EmailTransport.Smtp"/> or <see cref="EmailTransport.Api"/>. Defaults to SMTP for backwards compatibility.</summary>
    public string Transport { get; set; } = EmailTransport.Smtp;
    /// <summary>Selected HTTP-API provider key when <see cref="Transport"/> is API.</summary>
    public string ApiProvider { get; set; } = EmailApiProvider.ZeptoMail;
}

// ─── ZeptoMail (Zoho) HTTP API ────────────────────────────────────────────────
// Defaults mirror the brief — every value remains editable from the admin page so
// nothing about the endpoint is hard-coded in code.
public class ZeptoMailSettings
{
    public const string DefaultDomain      = "antargyan.com";
    public const string DefaultHost        = "api.zeptomail.com";
    public const string DefaultApiEndpoint = "https://api.zeptomail.com/v1.1/email";

    public bool Enabled { get; set; }
    public string? Domain { get; set; }        // sender domain (informational + helps fallback)
    public string? Host { get; set; }          // api.zeptomail.com
    public string? ApiEndpoint { get; set; }   // full POST URL
    public string? AgentAlias { get; set; }    // optional X-Sender-Alias header
    public string? Token { get; set; }         // Send-Mail Token — Authorization: Zoho-enczapikey <token>
    public string? FromName { get; set; }
    public string? FromEmail { get; set; }
    public string? ReplyTo { get; set; }
}
