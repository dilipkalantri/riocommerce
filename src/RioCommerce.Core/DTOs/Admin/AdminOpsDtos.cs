namespace RioCommerce.Core.DTOs.Admin;

public record RoleOption(Guid Id, string Name, string DisplayName);

// Customer-role management (admin).
public record RoleAdminItem(Guid Id, string Name, string DisplayName, bool IsActive, bool IsSystem, int UserCount);

public class RoleEditModel
{
    public Guid? Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;   // friendly "Name"
    public string SystemName { get; set; } = string.Empty;    // immutable system name
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public record AdminUserItem(
    Guid Id, string FullName, string? Email, string? Phone, List<string> Roles,
    bool IsActive, DateTime? LastLoginAt, DateTime CreatedAt);

// ── 👤 Smart Customer Search ──
// One row in the live-suggestion dropdown of the admin Customers page. Fields are the minimum
// the dropdown needs to show — avatar, identity, role, course count, status — plus enough to
// auto-fill the next screen on selection (Id, full name, mobile, email).
public record CustomerSuggestion(
    Guid Id,
    string FullName,
    string Initials,                 // "SP" — falls back to first two letters of FullName
    string? AvatarUrl,               // when null/empty the UI shows the initials circle
    string? Email,
    string? Phone,
    string? PrimaryRole,             // friendly display name of the most prominent active role
    string? CourseInterest,          // CA Foundation / CA Intermediate / null
    int CourseCount,                 // # of enrolments (drives the "Active Student" copy)
    bool IsActive,
    DateTime JoinedDate);

// Posted from the "➕ Create New Customer" modal in the smart search dropdown.
public class CustomerCreateRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Password { get; set; }     // optional — if blank the user can reset later
    public string? Role { get; set; }         // role system-name (e.g. "student"); null → default "student"
    public string? Notes { get; set; }        // saved as AdminComment
}

public record AdminUserStats(int Total, int Active, int Staff, int Students);

// ── Rich customer profile loaded after selecting a customer on the Create-Order wizard.
//    Drives the summary card (name, ID, stats) AND auto-populates the Billing step.
public record CustomerOrderProfile(
    Guid Id,
    string FriendlyId,              // e.g. "RIO-A1B2C3" — deterministic display id, not a column
    string FullName,
    string Initials,
    string? AvatarUrl,
    string? Email,
    string? Phone,
    bool IsActive,
    DateTime JoinedDate,
    // Stats
    int TotalOrders,
    decimal LifetimeValue,
    DateTime? LastOrderAt,
    // Default address (most recently saved address, or null)
    string? Address,
    string? City,
    string? State,
    string? Pincode,
    string Country,
    // Future-proofing — currently null but the GST / address rows can carry it later.
    string? GstNumber);

// Modal payload — superset of CustomerCreateRequest plus the default-address fields
// the Billing step will auto-populate from. The service composes the User + Address rows
// in a single transaction.
public class CreateOrderCustomerRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Password { get; set; }   // auto-generated when null/blank

    // Default address — saved as the customer's first CustomerAddress row
    public string? Address { get; set; }
    public string? City { get; set; }
    public string State { get; set; } = "Maharashtra";
    public string? Pincode { get; set; }
    public string Country { get; set; } = "India";
    public string? GstNumber { get; set; }

    // ── Optional metadata captured by the modal ─────────────────────────────
    // These travel with the request and the page reads them back after Save to
    // auto-seed the order's referral pick + preview. Persistence is best-effort:
    // ReferralSourceId is stored on the customer's profile note, DateOfBirth is
    // captured for future profile expansion. Neither blocks order creation.
    public Guid? ReferralSourceId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool IsBusinessInvoice { get; set; }
}

// Filter for the admin Customers directory.
public class CustomerFilter
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }     // role name
    public bool? Active { get; set; }      // null = all
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public record AuditLogItem(
    Guid Id, string ActorName, string Action, string EntityType, string? EntityId, string? Details, DateTime CreatedAt);

// GDPR export
public record ExportOrder(string OrderNumber, decimal Total, string Status, DateTime CreatedAt);
public record UserDataExport(
    Guid Id, string FullName, string? Email, string? Phone, string? City, string? State,
    List<string> Roles, List<ExportOrder> Orders, List<string> Enrollments, List<string> Reviews, List<string> Wishlist);
