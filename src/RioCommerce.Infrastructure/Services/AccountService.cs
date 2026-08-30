using RioCommerce.Core.DTOs.Account;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class AccountService : IAccountService
{
    private readonly RioCommerceDbContext _db;
    private readonly ICartService _cart;

    public AccountService(RioCommerceDbContext db, ICartService cart)
    {
        _db = db;
        _cart = cart;
    }

    public async Task<AccountDashboard> GetDashboardAsync(Guid userId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found.");

        var dash = new AccountDashboard
        {
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            ActiveCourses = await _db.Enrollments.CountAsync(e => e.UserId == userId && e.IsActive),
            TotalOrders = await _db.Orders.CountAsync(o => o.UserId == userId),
            PendingOrders = await _db.Orders.CountAsync(o => o.UserId == userId && o.Status == OrderStatus.Pending),
            TotalSpent = await _db.Orders
                .Where(o => o.UserId == userId && o.PaymentStatus == PaymentStatus.Success)
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0
        };

        var recentOrders = await _db.Orders.Where(o => o.UserId == userId)
            .Include(o => o.Items).Include(o => o.Invoice)
            .OrderByDescending(o => o.CreatedAt).Take(5).ToListAsync();
        dash.RecentOrders = recentOrders.Select(ToOrderItem).ToList();

        var recentCourses = await _db.Enrollments.Where(e => e.UserId == userId)
            .Include(e => e.Product).ThenInclude(p => p.PrimaryFaculty)
            .OrderByDescending(e => e.CreatedAt).Take(4).ToListAsync();
        dash.RecentCourses = recentCourses.Select(ToCourseItem).ToList();

        return dash;
    }

    public async Task<List<MyCourseItem>> GetMyCoursesAsync(Guid userId)
    {
        var rows = await _db.Enrollments.Where(e => e.UserId == userId)
            .Include(e => e.Product).ThenInclude(p => p.PrimaryFaculty)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();
        return rows.Select(ToCourseItem).ToList();
    }

    public async Task<List<MyOrderItem>> GetMyOrdersAsync(Guid userId)
    {
        var rows = await _db.Orders.Where(o => o.UserId == userId)
            .Include(o => o.Items).Include(o => o.Invoice)
            .OrderByDescending(o => o.CreatedAt).ToListAsync();
        return rows.Select(ToOrderItem).ToList();
    }

    public async Task<ProfileView?> GetProfileAsync(Guid userId)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        return u == null ? null : new ProfileView
        {
            FullName = u.FullName, Email = u.Email, Phone = u.Phone, City = u.City, State = u.State
        };
    }

    public async Task<(bool ok, string? error)> UpdateProfileAsync(Guid userId, ProfileUpdate update)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        if (string.IsNullOrWhiteSpace(update.FullName)) return (false, "Name is required.");

        var email = string.IsNullOrWhiteSpace(update.Email) ? null : update.Email.Trim();
        var phone = string.IsNullOrWhiteSpace(update.Phone) ? null : update.Phone.Trim();

        if (email != null && await _db.Users.AnyAsync(u => u.Id != userId && u.Email != null && u.Email.ToLower() == email.ToLower()))
            return (false, "That email is already in use.");
        if (phone != null && await _db.Users.AnyAsync(u => u.Id != userId && u.Phone == phone))
            return (false, "That phone number is already in use.");

        user.FullName = update.FullName.Trim();
        user.Email = email;
        user.Phone = phone;
        user.City = string.IsNullOrWhiteSpace(update.City) ? null : update.City.Trim();
        user.State = string.IsNullOrWhiteSpace(update.State) ? null : update.State.Trim();
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        if (string.IsNullOrEmpty(user.PasswordHash)) return (false, "No password is set on this account.");
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return (false, "New password must be at least 6 characters.");

        bool currentOk;
        try { currentOk = BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash); }
        catch { currentOk = false; }
        if (!currentOk) return (false, "Your current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<ReorderResult> ReorderAsync(Guid userId, string orderNumber)
    {
        var order = await _db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber && o.UserId == userId);
        if (order == null) throw new InvalidOperationException("Order not found.");

        var added = 0;
        foreach (var item in order.Items)
        {
            var (ok, _) = await _cart.AddAsync(userId, item.ProductId, item.ProductModeId);
            if (ok) added++;
        }
        return new ReorderResult(added);
    }

    // ── mapping helpers ──

    private static MyOrderItem ToOrderItem(Order o) => new(
        o.OrderNumber, o.CreatedAt, Summarize(o), o.TotalAmount, o.Status, o.PaymentStatus, o.Invoice?.InvoiceNumber);

    private static MyCourseItem ToCourseItem(Enrollment e) => new(
        e.ProductId, e.Product.Slug, e.Product.Title, e.Product.PrimaryFaculty?.DisplayName,
        ModeLabel(e.Mode), e.ProgressPct, e.CreatedAt, e.IsActive);

    private static string Summarize(Order o) => o.Items.Count == 0
        ? "—"
        : o.Items.First().ProductTitle + (o.Items.Count > 1 ? $" +{o.Items.Count - 1} more" : "");

    private static string? ModeLabel(LectureMode? m) => m switch
    {
        LectureMode.LiveStreaming => "Live Streaming",
        LectureMode.Recorded => "Recorded",
        LectureMode.LivePlusRecorded => "Live + Recorded",
        LectureMode.Pendrive => "Pendrive",
        LectureMode.FaceToFace => "Face to Face",
        _ => null
    };
}
