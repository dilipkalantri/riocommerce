namespace RioCommerce.Core.Interfaces;


// ─── Email provider abstraction ─────────────────────────────────────────────
// Any transport that can deliver a single email implements this interface and
// declares its <see cref="Key"/> (e.g. "smtp", "zeptomail"). The router picks
// the one matching the admin's current selection on every send so switching
// providers in the UI takes effect immediately, without restart.
//
// Adding a new provider (SendGrid, Mailgun, Brevo, AWS SES, etc.) is a single
// new class + a DI registration; existing code stays untouched.

public interface IEmailProvider
{
    /// <summary>Stable key used to match the admin's selected provider (e.g. "smtp", "zeptomail").</summary>
    string Key { get; }

    /// <summary>Friendly display label for admin UIs and logs.</summary>
    string DisplayName { get; }

    /// <summary>True if the provider has the minimum configuration required to attempt a send.</summary>
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);

    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(
    string ToEmail,
    string? ToName,
    string Subject,
    string Body,
    bool IsHtml = false,
    string? FromEmailOverride = null,
    string? FromNameOverride = null,
    string? ReplyToOverride = null,
    /// <summary>Real CC header recipients — one message addressed to several people, not several
    /// messages. Null/empty sends no CC header at all. Last parameter with a default so every
    /// existing construction of this record is unaffected.</summary>
    IReadOnlyList<string>? Cc = null);

public sealed record EmailSendResult(bool Ok, string? Error, string? Response, long DurationMs)
{
    public static EmailSendResult Success(string? response, long durationMs) => new(true, null, response, durationMs);
    public static EmailSendResult Failure(string error, string? response, long durationMs) => new(false, error, response, durationMs);
}

// Routes an outbound email to whichever provider the admin currently selected.
// Wraps provider resolution + per-send logging + duration timing.
public interface IEmailRouter
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct = default);

    /// <summary>Sends a one-off test email using the supplied provider key
    /// (skips the persisted "selected provider" lookup so admins can probe a
    /// provider without flipping the live transport).</summary>
    Task<EmailSendResult> SendTestAsync(string providerKey, EmailMessage message, CancellationToken ct = default);

    /// <summary>Returns the provider key the router would use right now — useful for the UI's "active" badge.</summary>
    Task<string> GetActiveProviderKeyAsync(CancellationToken ct = default);
}
