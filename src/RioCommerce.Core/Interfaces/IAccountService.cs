using RioCommerce.Core.DTOs.Account;
namespace RioCommerce.Core.Interfaces;

public interface IAccountService
{
    Task<AccountDashboard> GetDashboardAsync(Guid userId);
    Task<List<MyCourseItem>> GetMyCoursesAsync(Guid userId);
    Task<List<MyOrderItem>> GetMyOrdersAsync(Guid userId);
    Task<ProfileView?> GetProfileAsync(Guid userId);
    Task<(bool ok, string? error)> UpdateProfileAsync(Guid userId, ProfileUpdate update);
    Task<(bool ok, string? error)> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    /// <summary>Adds every line of a past order back into the user's cart. Returns how many were added.</summary>
    Task<ReorderResult> ReorderAsync(Guid userId, string orderNumber);
}
