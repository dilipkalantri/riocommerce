namespace RioCommerce.Core.Security;

/// <summary>
/// The platform's permission tree, modelled for the ERP-style table view in /admin/access-control.
/// Each <see cref="PermissionGroup"/> is a vertical section (Catalog, Sales, …); each
/// <see cref="PermissionRow"/> is a row in the section. Every row produces SIX permission keys
/// — one per action (View/Create/Edit/Delete/Approve/Export) — so the admin grid is a clean
/// rows × actions matrix with no collapsed/hidden cells.
/// Naming: <c>{row.Key}.{action}</c>, lowercase. Example: <c>sales.orders.refund</c>.
/// Adding a new row is one line in <see cref="Groups"/> below; the action columns and the
/// stored keys are derived automatically.
/// </summary>
public static class PermissionCatalog
{
    public static readonly string[] Actions = { "view", "create", "edit", "delete", "approve", "export" };

    public static IReadOnlyList<PermissionGroup> Groups { get; } = new[]
    {
        new PermissionGroup("dashboard", "Dashboard", "📊", new PermissionRow[]
        {
            new("dashboard", "Dashboard"),
        }),
        new PermissionGroup("catalog", "Catalog", "📦", new PermissionRow[]
        {
            new("catalog.products",         "Products"),
            new("catalog.categories",       "Categories"),
            new("catalog.faculty",          "Faculty"),
            new("catalog.revenue_sharing",  "Revenue Sharing"),
            new("catalog.attributes",       "Attributes"),
        }),
        new PermissionGroup("sales", "Sales", "💰", new PermissionRow[]
        {
            new("sales.orders",             "Orders"),
            new("sales.create_order",       "Create Order"),
            new("sales.franchise_orders",   "Franchise Orders"),
            new("sales.dispatch",           "Dispatch"),
            new("sales.returns",            "Returns"),
        }),
        new PermissionGroup("customers", "Customers", "👥", new PermissionRow[]
        {
            new("customers",                "Customers"),
            new("customers.roles",          "Customer Roles"),
            new("customers.leads",          "Leads"),
            new("customers.reviews",        "Reviews"),
        }),
        new PermissionGroup("promotions", "Promotions", "🎁", new PermissionRow[]
        {
            new("promotions.coupons",       "Coupons"),
            new("promotions.discounts",     "Discounts"),
            new("promotions.offers",        "Offer Recommendation"),
        }),
        new PermissionGroup("content", "Content Management", "📝", new PermissionRow[]
        {
            new("content.pages",            "Pages"),
            new("content.blogs",            "Blogs"),
            new("content.faqs",             "FAQs"),
            new("content.media",            "Media"),
        }),
        new PermissionGroup("reports", "Reports", "📈", new PermissionRow[]
        {
            new("reports",                  "Reports"),
        }),
        new PermissionGroup("settings", "Settings", "⚙️", new PermissionRow[]
        {
            new("settings.users",                "Users"),
            new("settings.roles",                "Roles"),
            new("settings.access_control",       "Admin Access Control"),
            new("settings.website_config",       "Website Configuration"),
            new("settings.notifications",        "Notification"),
        }),
        new PermissionGroup("franchise", "Franchise", "🏬", new PermissionRow[]
        {
            new("franchise",                "Franchise"),
            new("franchise.commission",     "Commission"),
        }),
        new PermissionGroup("finance", "Finance", "🧾", new PermissionRow[]
        {
            new("finance.gst",              "GST"),
            new("finance.payment",          "Payment"),
        }),
        new PermissionGroup("integrations", "Integrations", "🔌", new PermissionRow[]
        {
            new("integrations.serial_keys",    "Serial Keys"),
            new("integrations.serial_tenants", "Serial Key Tenants"),
            new("integrations.superclass",          "Superclass Registrations"),
            new("integrations.superclass_settings", "Superclass Settings"),
        }),
    };

    /// <summary>Every key declared in the catalogue, computed once at startup.</summary>
    public static IReadOnlySet<string> AllKeys { get; } =
        Groups.SelectMany(g => g.Rows)
              .SelectMany(r => Actions.Select(a => $"{r.Key}.{a}"))
              .ToHashSet();

    /// <summary>All rows flattened — useful for search.</summary>
    public static IEnumerable<PermissionRow> AllRows => Groups.SelectMany(g => g.Rows);
}

/// <summary>A vertical section in the permission grid (Catalog, Sales, …).</summary>
public record PermissionGroup(string Key, string Label, string Icon, IReadOnlyList<PermissionRow> Rows);

/// <summary>One row in the table — produces six keys (view/create/edit/delete/approve/export).</summary>
public record PermissionRow(string Key, string Label)
{
    public string KeyFor(string action) => $"{Key}.{action}";
    public IEnumerable<string> AllKeys() => PermissionCatalog.Actions.Select(KeyFor);
}
