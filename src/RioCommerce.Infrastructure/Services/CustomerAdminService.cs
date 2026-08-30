using System.Text.Json;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class CustomerAdminService : ICustomerAdminService
{
    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    public CustomerAdminService(RioCommerceDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    public Task<List<RoleOption>> RolesAsync() =>
        _db.Roles.Where(r => r.IsActive).OrderBy(r => r.Name)
            .Select(r => new RoleOption(r.Id, r.Name, r.DisplayName)).ToListAsync();

    public async Task<CustomerEditModel?> GetAsync(Guid id)
    {
        var u = await _db.Users.AsNoTracking().Include(x => x.UserRoles).ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return null;
        var newsletter = u.Email != null && await _db.NewsletterSubscribers.AnyAsync(s => s.Email == u.Email && s.IsActive);
        return new CustomerEditModel
        {
            Id = u.Id, Email = u.Email, FullName = u.FullName, FirstName = u.FirstName, LastName = u.LastName,
            Phone = u.Phone, Gender = u.Gender, Level = u.CourseInterest, Attempt = u.Attempt, AdminComment = u.AdminComment,
            IsActive = u.IsActive, Newsletter = newsletter, CreatedAt = u.CreatedAt, LastActivity = u.LastLoginAt,
            Roles = u.UserRoles.Where(r => r.IsActive && r.Role != null).Select(r => r.Role!.Name).ToList()
        };
    }

    public async Task<(bool ok, string? error)> SaveAsync(CustomerEditModel m, Guid? actorId, string actorName)
    {
        _db.ChangeTracker.Clear();
        var u = await _db.Users.Include(x => x.UserRoles).FirstOrDefaultAsync(x => x.Id == m.Id);
        if (u == null) return (false, "Customer not found.");

        var email = string.IsNullOrWhiteSpace(m.Email) ? null : m.Email.Trim();
        if (email != null && await _db.Users.AnyAsync(x => x.Id != m.Id && x.Email == email))
            return (false, "Another customer already uses that email.");

        u.Email = email;
        u.FirstName = string.IsNullOrWhiteSpace(m.FirstName) ? null : m.FirstName.Trim();
        u.LastName = string.IsNullOrWhiteSpace(m.LastName) ? null : m.LastName.Trim();
        var full = $"{u.FirstName} {u.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) u.FullName = full;
        u.Phone = string.IsNullOrWhiteSpace(m.Phone) ? null : m.Phone.Trim();
        u.Gender = string.IsNullOrWhiteSpace(m.Gender) ? null : m.Gender;
        u.CourseInterest = m.Level;
        u.Attempt = string.IsNullOrWhiteSpace(m.Attempt) ? null : m.Attempt.Trim();
        u.AdminComment = string.IsNullOrWhiteSpace(m.AdminComment) ? null : m.AdminComment.Trim();
        u.IsActive = m.IsActive;

        // ── roles ──
        var roles = await _db.Roles.ToListAsync();
        foreach (var role in roles)
        {
            var selected = m.Roles.Contains(role.Name);
            var ur = u.UserRoles.FirstOrDefault(x => x.RoleId == role.Id);
            if (selected) { if (ur == null) u.UserRoles.Add(new UserRole { RoleId = role.Id, IsActive = true }); else ur.IsActive = true; }
            else if (ur != null) ur.IsActive = false;
        }

        // ── newsletter ──
        if (email != null)
        {
            var sub = await _db.NewsletterSubscribers.FirstOrDefaultAsync(s => s.Email == email);
            if (m.Newsletter)
            {
                if (sub == null) _db.NewsletterSubscribers.Add(new NewsletterSubscriber { Email = email, Name = u.FullName, Source = "admin", IsActive = true });
                else { sub.IsActive = true; sub.UnsubscribedAt = null; }
            }
            else if (sub is { IsActive: true }) { sub.IsActive = false; sub.UnsubscribedAt = DateTime.UtcNow; }
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "CustomerUpdated", "User", u.Id.ToString(),
            JsonSerializer.Serialize(new { u.Email, u.FullName, roles = m.Roles }));
        return (true, null);
    }

    public async Task<(bool ok, string? error)> SetPasswordAsync(Guid id, string newPassword)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6) return (false, "Password must be at least 6 characters.");
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (u == null) return (false, "Customer not found.");
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public Task<List<CustomerOrderRow>> OrdersAsync(Guid id) =>
        _db.Orders.Where(o => o.UserId == id).OrderByDescending(o => o.CreatedAt)
            .Select(o => new CustomerOrderRow(o.Id, o.OrderNumber, o.TotalAmount, o.Status.ToString(), o.PaymentStatus.ToString(), o.CreatedAt))
            .ToListAsync();

    // ── Addresses ──
    public Task<List<CustomerAddressEdit>> AddressesAsync(Guid id) =>
        _db.CustomerAddresses.Where(a => a.UserId == id).OrderBy(a => a.CreatedAt)
            .Select(a => new CustomerAddressEdit
            {
                Id = a.Id, FirstName = a.FirstName, LastName = a.LastName, Email = a.Email, Phone = a.Phone,
                FaxNumber = a.FaxNumber, Address1 = a.Address1, City = a.City, State = a.State, Pincode = a.Pincode, Country = a.Country
            }).ToListAsync();

    public async Task<(bool ok, string? error)> SaveAddressAsync(Guid userId, CustomerAddressEdit a)
    {
        _db.ChangeTracker.Clear();
        CustomerAddress entity;
        if (a.Id is { } id && id != Guid.Empty)
            entity = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId)
                ?? throw new InvalidOperationException("Address not found.");
        else { entity = new CustomerAddress { UserId = userId }; _db.CustomerAddresses.Add(entity); }

        entity.FirstName = a.FirstName?.Trim(); entity.LastName = a.LastName?.Trim();
        entity.Email = a.Email?.Trim(); entity.Phone = a.Phone?.Trim(); entity.FaxNumber = a.FaxNumber?.Trim();
        entity.Address1 = a.Address1?.Trim(); entity.City = a.City?.Trim(); entity.State = a.State?.Trim();
        entity.Pincode = a.Pincode?.Trim(); entity.Country = string.IsNullOrWhiteSpace(a.Country) ? "India" : a.Country.Trim();
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task DeleteAddressAsync(Guid addressId)
    {
        _db.ChangeTracker.Clear();
        var a = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == addressId);
        if (a == null) return;
        _db.CustomerAddresses.Remove(a);
        await _db.SaveChangesAsync();
    }
}
