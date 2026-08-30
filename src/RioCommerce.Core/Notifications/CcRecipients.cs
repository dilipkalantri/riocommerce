using System.Net.Mail;

namespace RioCommerce.Core.Notifications;

/// <summary>
/// Parsing and validation for the optional CC list an admin configures on a message template.
///
/// <para>Lives in Core and is the single definition of the rules, so the Admin editor rejects the
/// same input the sender would have rejected. Validating in only one of the two places is how a
/// template gets saved with three addresses and then silently fails at dispatch time.</para>
/// </summary>
public static class CcRecipients
{
    /// <summary>An admin CC list is for a handful of staff, not a mailing list.</summary>
    public const int MaxAddresses = 2;

    /// <summary>Fits two addresses at the RFC-5321 limit of 254 each, plus the separator.</summary>
    public const int MaxStoredLength = 512;

    public sealed record ParseResult(bool Ok, string? Error, IReadOnlyList<string> Addresses)
    {
        /// <summary>Canonical form to persist — what <see cref="Parse"/> accepted, comma-separated.</summary>
        public string? Normalised => Addresses.Count == 0 ? null : string.Join(", ", Addresses);
    }

    private static readonly ParseResult Empty = new(true, null, Array.Empty<string>());

    /// <summary>
    /// Validates a raw "a@x.com, b@y.com" string from the admin form.
    ///
    /// <para>Empty is valid and yields an empty list — CC is optional everywhere.</para>
    /// </summary>
    public static ParseResult Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Empty;

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var accepted = new List<string>();
        foreach (var part in parts)
        {
            // A CR/LF inside an address is an attempt to inject extra headers into the outgoing
            // message. Rejected here rather than relied on the transport to strip.
            if (part.Any(c => c is '\r' or '\n' or '\0'))
                return new ParseResult(false, "Email addresses cannot contain line breaks.", Array.Empty<string>());

            if (!IsValidEmail(part))
                return new ParseResult(false, $"“{part}” is not a valid email address.", Array.Empty<string>());

            // Case-insensitive: STAFF@x.com and staff@x.com are the same mailbox, so the second one
            // is dropped rather than counted towards the limit.
            if (accepted.Any(a => a.Equals(part, StringComparison.OrdinalIgnoreCase))) continue;

            accepted.Add(part);
        }

        if (accepted.Count > MaxAddresses)
            return new ParseResult(false, $"Maximum {MaxAddresses} CC email addresses are allowed.", Array.Empty<string>());

        return new ParseResult(true, null, accepted);
    }

    /// <summary>
    /// The addresses to actually put on the CC header for one send.
    ///
    /// <para>Excludes <paramref name="primaryRecipient"/> — a staff member who happens to also be the
    /// customer on this order should get the mail once, in To, not twice. Invalid stored data yields
    /// an empty list rather than an exception: a mis-saved template must not stop the customer's
    /// email going out.</para>
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? raw, string? primaryRecipient)
    {
        var parsed = Parse(raw);
        if (!parsed.Ok || parsed.Addresses.Count == 0) return Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(primaryRecipient)) return parsed.Addresses;

        var primary = primaryRecipient.Trim();
        return parsed.Addresses
            .Where(a => !a.Equals(primary, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Deliberately stricter than <see cref="MailAddress"/> alone, which happily accepts a bare
    /// "staff@invalid" host. A CC that never resolves is a silently-lost copy, so a dotted domain
    /// is required.
    /// </summary>
    private static bool IsValidEmail(string candidate)
    {
        if (candidate.Length > 254) return false;
        if (!MailAddress.TryCreate(candidate, out var parsed)) return false;
        if (!string.Equals(parsed!.Address, candidate, StringComparison.Ordinal)) return false; // no display names

        var at = candidate.LastIndexOf('@');
        var domain = candidate[(at + 1)..];
        return domain.Contains('.')
            && !domain.StartsWith('.') && !domain.EndsWith('.')
            && !domain.Contains("..");
    }
}
