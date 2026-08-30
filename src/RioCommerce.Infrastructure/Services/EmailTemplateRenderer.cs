using System.Text.RegularExpressions;
using RioCommerce.Core.Interfaces;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// {{snake_case}} replacement with case-insensitive matching. Unknown tokens are
/// left in place so admins can spot typos in the Preview modal. Standalone class
/// (not a state-holding service) — all calls are pure.
/// </summary>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Regex TokenPattern =
        new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    // Curated list of the placeholders the brief calls out, plus a few common ones used
    // by existing templates. Showing this in the Preview modal helps admins author copy.
    private static readonly string[] _knownTokens =
    {
        "name", "email", "mobile",
        "order_number", "total", "amount", "balance",
        "course_name", "invoice_number",
        "items", "status", "franchise",
        "reset_link", "verification_link",
        "company", "support_email",
    };

    public IReadOnlyList<string> KnownTokens => _knownTokens;

    public string? Render(string? rawText, IDictionary<string, string?> values)
    {
        if (string.IsNullOrEmpty(rawText) || values is null || values.Count == 0)
            return rawText;

        return TokenPattern.Replace(rawText, m =>
        {
            var key = m.Groups[1].Value;
            // Case-insensitive lookup so admin copy can use {{Name}} or {{NAME}} freely.
            foreach (var kvp in values)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value ?? string.Empty;
            }
            return m.Value;   // leave unknown tokens intact
        });
    }

    public IDictionary<string, string?> BuildSampleData(IDictionary<string, string?>? overrides = null)
    {
        var sample = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"]            = "Riya Sharma",
            ["email"]           = "riya.sharma@example.com",
            ["mobile"]          = "9876543210",
            ["order_number"]    = "RIO-2026-0457",
            ["total"]           = "₹12,500",
            ["amount"]          = "₹2,500",
            ["balance"]         = "₹0",
            ["course_name"]     = "CA Foundation Economics — Regular Batch",
            ["invoice_number"]  = "INV/2026/0457",
            ["items"]           = "CA Foundation Economics × 1",
            ["status"]          = "Confirmed",
            ["franchise"]       = "Sample Centre",
            ["reset_link"]      = "https://example.com/account/reset?token=sample",
            ["verification_link"]= "https://example.com/account/verify?token=sample",
            ["company"]         = "My Store",
            ["support_email"]   = "support@example.com",
        };
        if (overrides != null)
            foreach (var kvp in overrides)
                sample[kvp.Key] = kvp.Value;
        return sample;
    }
}
