namespace RioCommerce.Core.Interfaces;

/// <summary>
/// Token-replacement renderer used by:
///   • Notification dispatch — fills real values into a template before send
///   • Admin Preview modal — fills sample values so the admin can eyeball the render
///   • Admin Send-Test modal — same, with the recipient overridden
///
/// Tokens are {{snake_case}} placeholders inside the Subject and Body.
/// Unknown tokens are left in place so a typo is visible in the preview rather than
/// being silently swallowed.
/// </summary>
public interface IEmailTemplateRenderer
{
    /// <summary>Apply a values dictionary to the supplied raw text. Returns null when input is null.</summary>
    string? Render(string? rawText, IDictionary<string, string?> values);

    /// <summary>Reasonable sample data for preview / test. Caller can override individual keys.</summary>
    IDictionary<string, string?> BuildSampleData(IDictionary<string, string?>? overrides = null);

    /// <summary>The well-known token list — used by the Preview modal to surface the available placeholders.</summary>
    IReadOnlyList<string> KnownTokens { get; }
}
