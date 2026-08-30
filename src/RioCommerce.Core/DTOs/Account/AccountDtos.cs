using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Account;

public record MyCourseItem(
    Guid ProductId, string Slug, string Title, string? FacultyName, string? ModeName,
    decimal ProgressPct, DateTime EnrolledAt, bool IsActive);

public record MyOrderItem(
    string OrderNumber, DateTime CreatedAt, string Summary, decimal Total,
    OrderStatus Status, PaymentStatus PaymentStatus, string? InvoiceNumber);

public class AccountDashboard
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int ActiveCourses { get; set; }
    public int TotalOrders { get; set; }
    public int PendingOrders { get; set; }
    public decimal TotalSpent { get; set; }
    public List<MyOrderItem> RecentOrders { get; set; } = new();
    public List<MyCourseItem> RecentCourses { get; set; } = new();
}

public class ProfileView
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
}

public class ProfileUpdate
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
}

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ReorderResult(int ItemsAdded);
