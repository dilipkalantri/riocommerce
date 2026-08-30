using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

// Backs the admin "Edit customer details" page: profile + roles + newsletter, password reset,
// the customer's orders, and their saved addresses.
public interface ICustomerAdminService
{
    Task<CustomerEditModel?> GetAsync(Guid id);
    Task<List<RoleOption>> RolesAsync();
    Task<(bool ok, string? error)> SaveAsync(CustomerEditModel model, Guid? actorId, string actorName);
    Task<(bool ok, string? error)> SetPasswordAsync(Guid id, string newPassword);
    Task<List<CustomerOrderRow>> OrdersAsync(Guid id);

    Task<List<CustomerAddressEdit>> AddressesAsync(Guid id);
    Task<(bool ok, string? error)> SaveAddressAsync(Guid userId, CustomerAddressEdit address);
    Task DeleteAddressAsync(Guid addressId);
}
