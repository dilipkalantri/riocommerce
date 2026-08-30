namespace RioCommerce.Core.Shipping;

/// <summary>One courier the business despatches with.</summary>
/// <param name="Name">Stored verbatim in <c>Shipment.Courier</c> — this string IS the persisted value.</param>
/// <param name="TrackingUrl">Public tracking page, or null for a courier that has no tracking at all.</param>
public sealed record CourierOption(string Name, string? TrackingUrl)
{
    /// <summary>Whether a consignment with this courier must carry a tracking number.
    /// Derived from the URL rather than stored separately — a courier with no tracking page has
    /// nothing to look a number up on.</summary>
    public bool RequiresTracking => TrackingUrl != null;
}

/// <summary>
/// The couriers available for despatch, and where each one is tracked.
///
/// <para><b>Single source of truth.</b> The admin dropdown, the server-side validation, the dispatch
/// list, the customer-facing tracking link and any email all read this list — so adding or retiring a
/// courier is one edit here, and no screen can drift out of step with another.</para>
///
/// <para>Tracking URLs are the couriers' landing pages, NOT deep links: neither vendor has a verified
/// per-consignment URL format, so the number is shown next to the link for the customer to paste.
/// Do not "helpfully" append the tracking number — an invented URL shape silently 404s.</para>
/// </summary>
public static class Couriers
{
    public const string Trackon = "Trackon";
    public const string IndiaPost = "India Post";
    public const string Pcmc = "PCMC - 1 Day Delivery";

    public static readonly IReadOnlyList<CourierOption> All = new[]
    {
        new CourierOption(Trackon,   "https://www.trackon.in/courier-tracking"),
        new CourierOption(IndiaPost, "https://www.indiapost.gov.in/tracking"),
        // Local same-city delivery — no consignment number, no tracking page.
        new CourierOption(Pcmc,      null),
    };

    /// <summary>The option for a stored courier string, or null when it is blank or unrecognised
    /// (older shipments may hold a free-text courier from before this list existed).</summary>
    public static CourierOption? Find(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : All.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when this courier needs a tracking number. Unknown/legacy couriers are treated as
    /// tracked, which is the safe default: it asks for a number rather than losing one.</summary>
    public static bool RequiresTracking(string? name) => Find(name)?.RequiresTracking ?? true;

    /// <summary>Tracking page for a courier, or null when it has none.</summary>
    public static string? TrackingUrl(string? name) => Find(name)?.TrackingUrl;
}
