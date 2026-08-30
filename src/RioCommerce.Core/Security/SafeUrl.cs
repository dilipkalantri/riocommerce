namespace RioCommerce.Core.Security;

/// <summary>
/// Allowlist for editor-supplied links and asset references that will end up in an href or src.
///
/// Allowlist rather than a blocklist on purpose — "does not start with javascript:" is trivially
/// defeated by casing, leading whitespace or control characters, entity encoding, or a
/// "java\tscript:" split. "Must parse as absolute http/https, or begin with a single slash"
/// cannot express a script URL at all.
/// </summary>
public static class SafeUrl
{
    /// <summary>
    /// True for a site-relative path ("/ca-foundation") or an absolute http/https URL.
    /// Everything else — javascript:, data:, vbscript:, file:, mailto:, and protocol-relative
    /// "//host/x" (which inherits the page scheme and is not a path) — is rejected.
    /// </summary>
    public static bool IsHttpOrRelative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (v.StartsWith("//", StringComparison.Ordinal)) return false;
        if (v.StartsWith('/')) return true;
        return Uri.TryCreate(v, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// True when the link leaves this site, i.e. it is absolute http/https rather than a relative
    /// path. Drives target="_blank" + rel="noopener noreferrer" on the storefront.
    /// Only meaningful for values that already passed <see cref="IsHttpOrRelative"/>.
    /// </summary>
    public static bool IsExternal(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
