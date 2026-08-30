namespace RioCommerce.Core.Security;

/// <summary>
/// Convenience constants for the most commonly-checked permission keys. The single source of
/// truth is <see cref="PermissionCatalog"/>; these are just shortcuts for compile-time
/// references (e.g. <c>[RequirePermission(Permissions.SalesOrdersRefund)]</c>). Add more here
/// as you wire individual pages/APIs to the RBAC layer — they MUST also exist in the catalog.
/// </summary>
public static class Permissions
{
    // Format: {row.Key}.{action}
    public const string SalesOrdersView    = "sales.orders.view";
    public const string SalesOrdersEdit    = "sales.orders.edit";
    public const string SalesOrdersDelete  = "sales.orders.delete";
    public const string SalesOrdersApprove = "sales.orders.approve";
    public const string SalesOrdersExport  = "sales.orders.export";

    public const string CustomersView    = "customers.view";
    public const string CustomersCreate  = "customers.create";
    public const string CustomersEdit    = "customers.edit";
    public const string CustomersDelete  = "customers.delete";

    public const string SettingsAccessControl = "settings.access_control.view";
}
