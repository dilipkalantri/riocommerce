namespace RioCommerce.Core.DTOs.Reporting;

/// <summary>
/// The eight reports and how they present themselves — slug, title, description.
///
/// <para>Lives in Core rather than beside the builders because three layers need it and only one of
/// them can see Infrastructure: the admin menu and the report page (Web), the route validation
/// (API), and the builders themselves. One list means the menu, the routes, the page headings and
/// the export filenames cannot drift apart.</para>
/// </summary>
public static class ReportCatalog
{
    /// <param name="MenuLabel">
    /// Short form for the sidebar. The nav column is narrow enough that the full titles ellipsize
    /// — "Product-Wise / Subject-…" tells a reader nothing — so each report carries a label that
    /// fits. The full <paramref name="Title"/> still heads the page and the exported file.
    /// </param>
    /// <param name="Icon">Single glyph for the menu row, so the group scans by shape as well as text.</param>
    public sealed record Entry(
        ReportType Type, string Slug, string Title, string MenuLabel, string Icon, string Description);

    public static IReadOnlyList<Entry> All { get; } = new[]
    {
        new Entry(ReportType.Sales, "sales", "Sales Report", "Sales", "🧾",
            "Date-wise and order-wise sales with product, customer, GST and payment detail."),
        new Entry(ReportType.Gst, "gst", "GST Report", "GST", "🏛️",
            "Invoice-wise taxable value, CGST / SGST / IGST and total invoice amount."),
        new Entry(ReportType.Faculty, "faculty", "Faculty-Wise Report", "Faculty-Wise", "👩‍🏫",
            "Earned faculty shares by course — quantity, net sales, rate and payout. Only courses the faculty earns a share on."),
        new Entry(ReportType.Franchisee, "franchisee", "Franchisee-Wise Report", "Franchisee-Wise", "🏬",
            "Orders and product sales per franchisee, with the franchisee discount broken out."),
        new Entry(ReportType.ProductSubject, "product-subject", "Product-Wise / Subject-Wise Report", "Product / Subject", "📚",
            "Sales aggregated by course and subject — quantity, orders, gross, discount and net."),
        new Entry(ReportType.FranchiseeReInvoice, "franchisee-reinvoice", "Franchisee Re-Invoice Report", "Re-Invoices", "📄",
            "Each original invoice against the franchisee re-invoice raised for it, with GST detail."),
        new Entry(ReportType.Shipping, "shipping", "Shipping Report", "Shipping", "📦",
            "Dispatched books by order — address, courier partner, AWB number and status."),
        new Entry(ReportType.TeacherSettlement, "teacher-settlement", "Teacher Settlement Report", "Teacher Settlement", "💰",
            "What each faculty is owed — sales, share, payable, paid and balance. Includes earnings not yet settled.")
    };

    /// <summary>
    /// Reports that have a management screen behind them, and what that screen is called.
    ///
    /// <para>These are ACTIONS, not reports, so they are surfaced as a button on the report they
    /// belong to rather than as their own sidebar rows — a menu listing "Raise Re-Invoice" next to
    /// eight reports invites the reader to treat it as a ninth.</para>
    /// </summary>
    public static string? ManageLabelFor(ReportType type) => type switch
    {
        ReportType.FranchiseeReInvoice => "Raise Re-Invoice",
        ReportType.TeacherSettlement => "Manage Settlements",
        _ => null
    };

    public static string? ManageUrlFor(ReportType type) =>
        ManageLabelFor(type) is null ? null : $"/admin/reports/{SlugOf(type)}/manage";

    public static ReportType? FromSlug(string? slug)
    {
        var hit = All.FirstOrDefault(e => string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return hit?.Type;
    }

    public static Entry Get(ReportType type) => All.First(e => e.Type == type);

    public static string SlugOf(ReportType type) => Get(type).Slug;

    public static string Slugs => string.Join(" | ", All.Select(e => e.Slug));
}
