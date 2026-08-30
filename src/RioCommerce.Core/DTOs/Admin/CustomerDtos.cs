using RioCommerce.Core.Enums;
namespace RioCommerce.Core.DTOs.Admin;

// Full customer record for the admin "Edit customer details" editor.
public class CustomerEditModel
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public CourseLevel? Level { get; set; }       // stored on User.CourseInterest
    public string? Attempt { get; set; }
    public string? AdminComment { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Newsletter { get; set; }
    public List<string> Roles { get; set; } = new();   // selected role names
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivity { get; set; }
}

public record CustomerOrderRow(Guid OrderId, string OrderNumber, decimal Total, string OrderStatus, string PaymentStatus, DateTime CreatedAt);

public class CustomerAddressEdit
{
    public Guid? Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FaxNumber { get; set; }
    public string? Address1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string Country { get; set; } = "India";
}
