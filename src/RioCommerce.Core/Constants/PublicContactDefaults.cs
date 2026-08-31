namespace RioCommerce.Core.Constants;

/// <summary>
/// The official public contact details, used as a FALLBACK only.
///
/// Anything configured in Admin → Settings (<c>ISiteSettingsService</c>) always wins; these
/// values exist so the storefront never renders an empty contact bar or footer when a setting
/// has not been filled in yet. Kept in one place so the phone number and address cannot drift
/// apart between the components that show them.
/// </summary>
public static class PublicContactDefaults
{
    public const string Phone = "+91 84118 82618";
    public const string Email = "contact@vijaypath.org";
    public const string Address = "India International Multiversity, 1A, I Space, Off Mumbai-Pune Bypass Road, Bavdhan Khurd, Pune – 411021, Maharashtra, India";
    public const string BusinessHours = "Monday – Saturday, 10:00 AM – 5:00 PM";

    /// <summary>
    /// True for seeded placeholder addresses that must never be published as a public contact.
    ///
    /// example.com / example.org / example.net are reserved by IANA (RFC 2606) for documentation
    /// and cannot receive mail, so an address on one of them is always leftover seed data — this
    /// database still carries <c>company_email = admin@example.com</c> from SeedData.cs. Treating
    /// it as "not configured" keeps a dead address off the storefront without overriding a real
    /// address an admin has actually set.
    /// </summary>
    public static bool IsPlaceholderEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && (email.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase)
         || email.EndsWith("@example.org", StringComparison.OrdinalIgnoreCase)
         || email.EndsWith("@example.net", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Turns a display number into a dialable <c>tel:</c> value — digits only, keeping a leading
    /// "+" so the country code survives. "+91 84118 82618" becomes "+918411882618".
    /// </summary>
    public static string TelHref(string? displayNumber)
    {
        if (string.IsNullOrWhiteSpace(displayNumber)) return string.Empty;
        var digits = new System.Text.StringBuilder();
        foreach (var ch in displayNumber)
        {
            if (char.IsDigit(ch)) digits.Append(ch);
        }
        return displayNumber.TrimStart().StartsWith("+") ? "+" + digits : digits.ToString();
    }
}
