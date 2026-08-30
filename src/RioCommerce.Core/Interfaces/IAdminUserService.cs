using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Customers;
namespace RioCommerce.Core.Interfaces;

public interface IAdminUserService
{
    Task<AdminUserStats> StatsAsync();
    Task<List<AdminUserItem>> ListAsync(string? search);
    Task<(IReadOnlyList<AdminUserItem> rows, int total)> ListCustomersAsync(CustomerFilter filter);
    Task<List<RoleOption>> RolesAsync();

    // Customer-role CRUD
    Task<List<RoleAdminItem>> ListRoleDetailsAsync();
    Task<RoleEditModel?> GetRoleAsync(Guid id);
    Task<(bool ok, string? error, Guid id)> SaveRoleAsync(RoleEditModel model, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> DeleteRoleAsync(Guid id, Guid? actorId, string actorName);

    Task<(bool ok, string? error)> GrantRoleAsync(Guid userId, string roleName, Guid actorId, string actorName);
    Task<(bool ok, string? error)> RevokeRoleAsync(Guid userId, string roleName, Guid actorId, string actorName);
    Task<(bool ok, string? error)> ToggleActiveAsync(Guid userId, Guid actorId, string actorName);
    Task<UserDataExport?> ExportUserDataAsync(Guid userId);
    /// <summary>GDPR erasure — anonymises the user + scrubs PII from their orders (history retained).</summary>
    Task<(bool ok, string? error)> AnonymizeUserAsync(Guid userId, Guid actorId, string actorName);

    // ── 👤 Smart Customer Search ──
    /// <summary>Live-suggestion search used by the admin Customers page. Returns up to <paramref name="take"/>
    /// rows (default 10) ranked by relevance: exact phone → exact email → name StartsWith → fuzzy contains.
    /// Returns an empty list for queries shorter than 2 characters so the dropdown is never noisy.</summary>
    Task<List<CustomerSuggestion>> SearchCustomersAsync(string q, int take = 10);

    /// <summary>Create a new customer from the "➕ Create New Customer" dropdown CTA. When a duplicate
    /// is detected, <c>duplicate</c> carries the existing customer's identity so the admin modal can
    /// render the "Customer Already Exists" panel with View / Use Existing / Cancel actions. Even
    /// Super Admin cannot bypass this — single customer identity is enforced platform-wide.</summary>
    Task<(bool ok, string? error, Guid id, DuplicateCheckResult? duplicate)> CreateCustomerAsync(
        CustomerCreateRequest req, Guid actorId, string actorName, string? ipAddress = null);

    /// <summary>The N most-recently joined active customers — shown as the initial
    /// "Recent Customers" list when the smart-search dropdown opens with no query.</summary>
    Task<List<CustomerSuggestion>> RecentCustomersAsync(int take = 10);

    /// <summary>Rich profile for the selected customer: identity + order stats
    /// (count, lifetime value, last order) + the customer's default address
    /// (the most recent CustomerAddress row). Used to auto-populate the Billing step
    /// and to render the post-selection summary card.</summary>
    Task<CustomerOrderProfile?> GetCustomerOrderProfileAsync(Guid customerId);

    /// <summary>Creates a User + the default CustomerAddress row + auto-generates a
    /// password when blank, in one transaction. Returns the new id (or the existing
    /// customer's id when a duplicate phone/email is detected, with <c>duplicate</c>
    /// set so the modal can show the "use existing" panel).</summary>
    Task<(bool ok, string? error, Guid id, DuplicateCheckResult? duplicate)> CreateCustomerForOrderAsync(
        CreateOrderCustomerRequest req, Guid actorId, string actorName, string? ipAddress = null);
}
