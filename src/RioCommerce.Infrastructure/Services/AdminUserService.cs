using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Customers;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class AdminUserService : IAdminUserService
{
    private static readonly Guid PrimaryAdminId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly string[] StaffRoles = { "super_admin", "admin", "operations", "faculty", "franchise_admin" };

    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICustomerDuplicateService _dupes;
    public AdminUserService(RioCommerceDbContext db, IAuditService audit, ICustomerDuplicateService dupes)
    { _db = db; _audit = audit; _dupes = dupes; }

    public async Task<AdminUserStats> StatsAsync()
    {
        var users = await _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).ToListAsync();
        return new AdminUserStats(
            users.Count,
            users.Count(u => u.IsActive),
            users.Count(u => u.UserRoles.Any(r => r.IsActive && r.Role != null && StaffRoles.Contains(r.Role.Name))),
            users.Count(u => u.UserRoles.Any(r => r.IsActive && r.Role != null && r.Role.Name == "student")));
    }

    public async Task<List<AdminUserItem>> ListAsync(string? search)
    {
        var q = _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => EF.Functions.ILike(u.FullName, $"%{s}%")
                || (u.Email != null && EF.Functions.ILike(u.Email, $"%{s}%"))
                || (u.Phone != null && EF.Functions.ILike(u.Phone, $"%{s}%")));
        }
        var users = await q.OrderByDescending(u => u.CreatedAt).Take(200).ToListAsync();
        return users.Select(u => new AdminUserItem(
            u.Id, u.FullName, u.Email, u.Phone,
            u.UserRoles.Where(r => r.IsActive && r.Role != null).Select(r => r.Role!.Name).ToList(),
            u.IsActive, u.LastLoginAt, u.CreatedAt)).ToList();
    }

    public async Task<(IReadOnlyList<AdminUserItem> rows, int total)> ListCustomersAsync(CustomerFilter f)
    {
        var q = _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(f.Email)) { var v = f.Email.Trim(); q = q.Where(u => u.Email != null && EF.Functions.ILike(u.Email, $"%{v}%")); }
        if (!string.IsNullOrWhiteSpace(f.FirstName)) { var v = f.FirstName.Trim(); q = q.Where(u => u.FirstName != null && EF.Functions.ILike(u.FirstName, $"%{v}%")); }
        if (!string.IsNullOrWhiteSpace(f.LastName)) { var v = f.LastName.Trim(); q = q.Where(u => u.LastName != null && EF.Functions.ILike(u.LastName, $"%{v}%")); }
        if (!string.IsNullOrWhiteSpace(f.Phone)) { var v = f.Phone.Trim(); q = q.Where(u => u.Phone != null && EF.Functions.ILike(u.Phone, $"%{v}%")); }
        if (!string.IsNullOrWhiteSpace(f.Role)) q = q.Where(u => u.UserRoles.Any(r => r.IsActive && r.Role != null && r.Role.Name == f.Role));
        if (f.Active.HasValue) q = q.Where(u => u.IsActive == f.Active.Value);

        var total = await q.CountAsync();
        var page = Math.Max(1, f.Page);
        var size = Math.Clamp(f.PageSize, 1, 200);
        var users = await q.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync();
        var rows = users.Select(u => new AdminUserItem(
            u.Id, u.FullName, u.Email, u.Phone,
            u.UserRoles.Where(r => r.IsActive && r.Role != null).Select(r => r.Role!.Name).ToList(),
            u.IsActive, u.LastLoginAt, u.CreatedAt)).ToList();
        return (rows, total);
    }

    public async Task<List<RoleOption>> RolesAsync() =>
        await _db.Roles.Where(r => r.IsActive).OrderBy(r => r.Name)
            .Select(r => new RoleOption(r.Id, r.Name, r.DisplayName)).ToListAsync();

    // ── Customer-role CRUD ──
    public async Task<List<RoleAdminItem>> ListRoleDetailsAsync()
    {
        var counts = await _db.UserRoles.Where(ur => ur.IsActive)
            .GroupBy(ur => ur.RoleId).Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.DisplayName).ToListAsync();
        return roles.Select(r => new RoleAdminItem(r.Id, r.Name, r.DisplayName, r.IsActive, r.IsSystem,
            counts.TryGetValue(r.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<RoleEditModel?> GetRoleAsync(Guid id)
    {
        var r = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return r == null ? null : new RoleEditModel
        {
            Id = r.Id, DisplayName = r.DisplayName, SystemName = r.Name, Description = r.Description,
            IsActive = r.IsActive, IsSystem = r.IsSystem
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveRoleAsync(RoleEditModel m, Guid? actorId, string actorName)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(m.DisplayName)) return (false, "Name is required.", Guid.Empty);

        Role entity;
        if (m.Id is { } id && id != Guid.Empty)
            entity = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id) ?? throw new InvalidOperationException("Role not found.");
        else { entity = new Role { IsSystem = false }; _db.Roles.Add(entity); }

        entity.DisplayName = m.DisplayName.Trim();
        entity.Description = string.IsNullOrWhiteSpace(m.Description) ? null : m.Description.Trim();
        entity.IsActive = m.IsActive;

        // System roles keep their immutable system name; others get a slugged, unique system name.
        if (!entity.IsSystem)
        {
            var sys = SlugName(string.IsNullOrWhiteSpace(m.SystemName) ? m.DisplayName : m.SystemName);
            if (string.IsNullOrEmpty(sys)) return (false, "Could not derive a system name.", Guid.Empty);
            if (await _db.Roles.AnyAsync(r => r.Name == sys && r.Id != entity.Id)) return (false, "Another role already uses that system name.", Guid.Empty);
            entity.Name = sys;
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, m.Id == null ? "RoleCreated" : "RoleUpdated", "Role", entity.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { entity.Name, entity.DisplayName, entity.IsActive }));
        return (true, null, entity.Id);
    }

    public async Task<(bool ok, string? error)> DeleteRoleAsync(Guid id, Guid? actorId, string actorName)
    {
        _db.ChangeTracker.Clear();
        var r = await _db.Roles.FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return (false, "Role not found.");
        if (r.IsSystem) return (false, "System roles can't be deleted.");
        if (await _db.UserRoles.AnyAsync(ur => ur.RoleId == id && ur.IsActive))
            return (false, "Can't delete — customers are still assigned to this role.");
        _db.Roles.Remove(r);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "RoleDeleted", "Role", id.ToString(), r.Name);
        return (true, null);
    }

    private static string SlugName(string s)
    {
        var chars = (s ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var name = new string(chars);
        while (name.Contains("__")) name = name.Replace("__", "_");
        return name.Trim('_');
    }

    public async Task<(bool ok, string? error)> GrantRoleAsync(Guid userId, string roleName, Guid actorId, string actorName)
    {
        var user = await _db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName);
        if (role == null) return (false, "Role not found.");

        var existing = user.UserRoles.FirstOrDefault(r => r.RoleId == role.Id);
        if (existing != null && existing.IsActive) return (true, null);   // already has it
        if (existing != null) existing.IsActive = true;
        else _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, IsActive = true });
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "RoleGranted", "User", userId.ToString(), $"{roleName} → {user.FullName}");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> RevokeRoleAsync(Guid userId, string roleName, Guid actorId, string actorName)
    {
        if (userId == PrimaryAdminId && roleName == "super_admin")
            return (false, "Can't remove super_admin from the primary admin account.");

        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        var ur = user.UserRoles.FirstOrDefault(r => r.Role != null && r.Role.Name == roleName && r.IsActive);
        if (ur == null) return (true, null);   // doesn't have it

        _db.UserRoles.Remove(ur);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "RoleRevoked", "User", userId.ToString(), $"{roleName} ✕ {user.FullName}");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ToggleActiveAsync(Guid userId, Guid actorId, string actorName)
    {
        if (userId == PrimaryAdminId) return (false, "The primary admin account can't be deactivated.");
        if (userId == actorId) return (false, "You can't deactivate your own account.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");
        user.IsActive = !user.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, user.IsActive ? "UserActivated" : "UserDeactivated", "User", userId.ToString(), user.FullName);
        return (true, null);
    }

    public async Task<UserDataExport?> ExportUserDataAsync(Guid userId)
    {
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(r => r.Role).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;

        var orders = await _db.Orders.Where(o => o.UserId == userId)
            .Select(o => new ExportOrder(o.OrderNumber, o.TotalAmount, o.Status.ToString(), o.CreatedAt)).ToListAsync();
        var enrollments = await _db.Enrollments.Where(e => e.UserId == userId).Include(e => e.Product)
            .Select(e => e.Product.Title).ToListAsync();
        var reviews = await _db.Reviews.Where(r => r.UserId == userId).Select(r => r.Title ?? r.Comment).ToListAsync();
        var wishlist = await _db.WishlistItems.Where(w => w.UserId == userId).Include(w => w.Product)
            .Select(w => w.Product.Title).ToListAsync();

        return new UserDataExport(
            user.Id, user.FullName, user.Email, user.Phone, user.City, user.State,
            user.UserRoles.Where(r => r.IsActive && r.Role != null).Select(r => r.Role!.Name).ToList(),
            orders, enrollments, reviews, wishlist);
    }

    public async Task<(bool ok, string? error)> AnonymizeUserAsync(Guid userId, Guid actorId, string actorName)
    {
        if (userId == PrimaryAdminId) return (false, "The primary admin account can't be anonymised.");
        if (userId == actorId) return (false, "You can't anonymise your own account.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.");

        var tag = userId.ToString("N")[..8];
        user.FullName = "Deleted User";
        user.Email = $"deleted+{tag}@riocommerce.invalid";
        user.Phone = null;
        user.City = null; user.State = null;
        user.PasswordHash = null;
        user.IsActive = false;
        user.GoogleId = null;

        // Scrub PII from this user's orders (history retained for accounting).
        var orders = await _db.Orders.Where(o => o.UserId == userId).ToListAsync();
        foreach (var o in orders)
        {
            o.StudentName = "Deleted User";
            o.StudentEmail = null;
            o.StudentPhone = "0000000000";
            o.BillingName = null; o.BillingAddress = null;
        }
        // Remove transient personal data.
        _db.WishlistItems.RemoveRange(_db.WishlistItems.Where(w => w.UserId == userId));
        _db.CartItems.RemoveRange(_db.CartItems.Where(c => c.UserId == userId));

        await _db.SaveChangesAsync();
        await _audit.LogAsync(actorId, actorName, "UserAnonymized", "User", userId.ToString(), $"{orders.Count} order(s) scrubbed");
        return (true, null);
    }

    // ── 👤 Smart Customer Search ─────────────────────────────────────────────────────────────
    // Powers the live-suggestion dropdown on the admin Customers page AND the
    // /api/admin/customers/search endpoint. Ranking is computed server-side so the client
    // never receives more than `take` rows, and the WHERE clause uses ILIKE + indexed equality
    // checks so the query plan stays sublinear on the user table.
    //
    // Priority order:
    //   exact phone    →  1000
    //   exact email    →   900
    //   StartsWith(name) ≅ 500 / 400 / 350  (FullName / FirstName / LastName)
    //   Contains(name)   ≅ 100
    //   Contains(email)  ≅  80
    //   Contains(phone)  ≅  60
    public async Task<List<CustomerSuggestion>> SearchCustomersAsync(string q, int take = 10)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return new();
        q = q.Trim();
        var qLower = q.ToLowerInvariant();
        var fuzzy = $"%{q}%";
        var starts = $"{q}%";
        take = Math.Clamp(take, 1, 25);   // hard cap so a hostile client can't drain the table

        // Pre-filter using OR'd ILIKE clauses — PostgreSQL collapses these into a single index
        // scan on the trigram (or btree) indexes we have on email/phone/name. Then compute the
        // relevance score per row, sort, and take the top N.
        var query = _db.Users
            .Include(u => u.UserRoles).ThenInclude(r => r.Role)
            .Where(u =>
                (u.Phone != null && u.Phone == q) ||
                (u.Email != null && u.Email.ToLower() == qLower) ||
                (u.Phone != null && EF.Functions.ILike(u.Phone, fuzzy)) ||
                (u.Email != null && EF.Functions.ILike(u.Email, fuzzy)) ||
                (u.FullName != null && EF.Functions.ILike(u.FullName, fuzzy)) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, fuzzy)) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, fuzzy)));

        var scored = await query
            .Select(u => new
            {
                User = u,
                Score =
                    (u.Phone != null && u.Phone == q                                                ? 1000 : 0) +
                    (u.Email != null && u.Email.ToLower() == qLower                                 ?  900 : 0) +
                    (u.FullName != null && EF.Functions.ILike(u.FullName, starts)                   ?  500 : 0) +
                    (u.FirstName != null && EF.Functions.ILike(u.FirstName, starts)                 ?  400 : 0) +
                    (u.LastName != null && EF.Functions.ILike(u.LastName, starts)                   ?  350 : 0) +
                    (u.FullName != null && EF.Functions.ILike(u.FullName, fuzzy)                    ?  100 : 0) +
                    (u.Email != null && EF.Functions.ILike(u.Email, fuzzy)                          ?   80 : 0) +
                    (u.Phone != null && EF.Functions.ILike(u.Phone, fuzzy)                          ?   60 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.User.CreatedAt)
            .Take(take)
            .ToListAsync();

        // Course count = number of enrolments. Single GROUP BY query, not N+1.
        var ids = scored.Select(x => x.User.Id).ToList();
        var counts = await _db.Enrollments.Where(e => ids.Contains(e.UserId))
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        return scored.Select(x =>
        {
            var u = x.User;
            // Primary role: pick the first ACTIVE role's display name, prefer non-default ones for visibility.
            var primary = u.UserRoles
                .Where(r => r.IsActive && r.Role != null)
                .Select(r => r.Role!.DisplayName)
                .FirstOrDefault();
            return new CustomerSuggestion(
                u.Id,
                string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? "Unknown") : u.FullName,
                InitialsOf(u),
                u.AvatarUrl,
                u.Email, u.Phone,
                primary,
                u.CourseInterest?.ToString(),
                counts.GetValueOrDefault(u.Id),
                u.IsActive,
                u.CreatedAt);
        }).ToList();
    }

    // Two-letter initials for the avatar fallback chip — name first, then email-local, then "?".
    private static string InitialsOf(User u)
    {
        var name = u.FullName;
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(u.Email))
            name = u.Email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        return name.Trim().Length >= 2 ? name.Trim()[..2].ToUpperInvariant() : name.Trim().ToUpperInvariant();
    }

    public async Task<(bool ok, string? error, Guid id, DuplicateCheckResult? duplicate)> CreateCustomerAsync(
        CustomerCreateRequest req, Guid actorId, string actorName, string? ipAddress = null)
    {
        if (req == null) return (false, "Empty request.", Guid.Empty, null);
        var name = (req.FullName ?? "").Trim();
        var phone = (req.Phone ?? "").Trim();
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Customer name is required.", Guid.Empty, null);
        if (string.IsNullOrWhiteSpace(phone)) return (false, "Mobile number is required.", Guid.Empty, null);

        // Centralised duplicate probe — single source of truth shared with /api/auth/register,
        // the /account/register form post, and any future entry point. Even Super Admin cannot
        // bypass this; the unique indexes on Users.Email + Users.Phone are the final guard rail.
        var dup = await _dupes.CheckAsync(email, phone);
        if (dup.HasDuplicate)
        {
            await _dupes.LogDuplicateAttemptAsync(email, phone, "Admin", actorId, actorName, ipAddress, dup.FieldName);
            var msg = dup.Field switch
            {
                DuplicateField.Email => "This email address is already registered.",
                DuplicateField.Phone => "This mobile number is already linked with an existing account.",
                DuplicateField.Both  => "The entered email address and mobile number are already registered.",
                _                    => "Customer already exists."
            };
            return (false, msg, Guid.Empty, dup);
        }

        _db.ChangeTracker.Clear();
        var nameParts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var u = new User
        {
            Id = Guid.NewGuid(),
            FullName = name,
            FirstName = nameParts.ElementAtOrDefault(0),
            LastName = nameParts.ElementAtOrDefault(1),
            Phone = phone,
            Email = email,
            IsActive = true,
            IsVerified = false,
            AdminComment = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PasswordHash = string.IsNullOrWhiteSpace(req.Password) ? null : BCrypt.Net.BCrypt.HashPassword(req.Password)
        };
        _db.Users.Add(u);

        // Resolve role — default to "student" if the caller passed nothing or an unknown name.
        var roleName = string.IsNullOrWhiteSpace(req.Role) ? "student" : req.Role.Trim();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName && r.IsActive)
                ?? await _db.Roles.FirstOrDefaultAsync(r => r.Name == "student" && r.IsActive);
        if (role != null)
            _db.UserRoles.Add(new UserRole { User = u, RoleId = role.Id, IsActive = true });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: another caller created the same customer between CheckAsync and SaveChanges.
            // Re-probe to surface the latest state to the UI and audit the blocked attempt.
            var raceDup = await _dupes.CheckAsync(email, phone);
            await _dupes.LogDuplicateAttemptAsync(email, phone, "Admin-Race", actorId, actorName, ipAddress, raceDup.FieldName);
            return (false, "A customer with these details already exists (race condition).", Guid.Empty, raceDup);
        }

        await _audit.LogAsync(actorId, actorName, "CustomerCreated", "User", u.Id.ToString(),
            System.Text.Json.JsonSerializer.Serialize(new { u.FullName, u.Phone, u.Email, role = role?.Name }));
        return (true, null, u.Id, null);
    }

    // ── Display id helper — derives a stable "RIO-A1B2C3" code from the user's GUID ──
    private static string FriendlyIdFor(Guid id)
    {
        // First 6 hex characters of the GUID — stable, unique-enough for display.
        // Real CustomerCode column can replace this later without touching callers.
        return $"RIO-{id.ToString("N")[..6].ToUpperInvariant()}";
    }

    public async Task<List<CustomerSuggestion>> RecentCustomersAsync(int take = 10)
    {
        take = Math.Clamp(take, 1, 25);
        var students = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(r => r.Role)
            .Where(u => u.IsActive
                && u.UserRoles.Any(ur => ur.IsActive && ur.Role != null && ur.Role.Name == "student"))
            .OrderByDescending(u => u.CreatedAt)
            .Take(take)
            .ToListAsync();

        var ids = students.Select(u => u.Id).ToList();
        var counts = await _db.Enrollments.Where(e => ids.Contains(e.UserId))
            .GroupBy(e => e.UserId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return students.Select(u =>
        {
            var primary = u.UserRoles.Where(r => r.IsActive && r.Role != null).Select(r => r.Role!.DisplayName).FirstOrDefault();
            return new CustomerSuggestion(
                u.Id,
                string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? "Unknown") : u.FullName,
                InitialsOf(u), u.AvatarUrl, u.Email, u.Phone, primary,
                u.CourseInterest?.ToString(),
                counts.GetValueOrDefault(u.Id),
                u.IsActive, u.CreatedAt);
        }).ToList();
    }

    public async Task<CustomerOrderProfile?> GetCustomerOrderProfileAsync(Guid customerId)
    {
        var u = await _db.Users
            .Include(x => x.UserRoles).ThenInclude(r => r.Role)
            .FirstOrDefaultAsync(x => x.Id == customerId);
        if (u == null) return null;

        // Aggregate order stats — single query.
        var stats = await _db.Orders
            .Where(o => o.UserId == customerId)
            .GroupBy(o => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Lifetime = g.Where(o => o.PaymentStatus == PaymentStatus.Success).Sum(o => (decimal?)o.TotalAmount) ?? 0m,
                LastAt = g.Max(o => (DateTime?)o.CreatedAt)
            })
            .FirstOrDefaultAsync();

        // Default address — most recently saved row. Country falls back to "India".
        var addr = await _db.Set<CustomerAddress>()
            .Where(a => a.UserId == customerId)
            .OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        return new CustomerOrderProfile(
            u.Id, FriendlyIdFor(u.Id),
            string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? "Unknown") : u.FullName,
            InitialsOf(u), u.AvatarUrl, u.Email, u.Phone,
            u.IsActive, u.CreatedAt,
            stats?.Count ?? 0,
            stats?.Lifetime ?? 0m,
            stats?.LastAt,
            addr?.Address1, addr?.City, addr?.State, addr?.Pincode,
            addr?.Country ?? "India",
            GstNumber: null);
    }

    public async Task<(bool ok, string? error, Guid id, DuplicateCheckResult? duplicate)> CreateCustomerForOrderAsync(
        CreateOrderCustomerRequest req, Guid actorId, string actorName, string? ipAddress = null)
    {
        if (req == null) return (false, "Empty request.", Guid.Empty, null);

        var password = req.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            var n = Random.Shared.Next(1000, 9999);
            password = $"RIO@{n}";
        }

        // Reuse the proven create-customer pipeline — duplicate check, role, hash, audit.
        var (ok, error, newId, dup) = await CreateCustomerAsync(new CustomerCreateRequest
        {
            FullName = req.FullName,
            Phone = req.Phone,
            Email = req.Email,
            Password = password,
            Role = "student",
            Notes = "Created from /admin/orders/create wizard",
        }, actorId, actorName, ipAddress);

        if (!ok) return (false, error, newId, dup);

        // Save the default address row in the same flow so the Billing step gets a pre-fill.
        var hasAnyAddressField =
            !string.IsNullOrWhiteSpace(req.Address) || !string.IsNullOrWhiteSpace(req.City)
            || !string.IsNullOrWhiteSpace(req.Pincode) || !string.IsNullOrWhiteSpace(req.GstNumber);

        if (hasAnyAddressField)
        {
            var nameParts = (req.FullName ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            _db.Set<CustomerAddress>().Add(new CustomerAddress
            {
                UserId = newId,
                FirstName = nameParts.ElementAtOrDefault(0),
                LastName = nameParts.ElementAtOrDefault(1),
                Email = req.Email,
                Phone = req.Phone,
                Address1 = req.Address,
                City = req.City,
                State = string.IsNullOrWhiteSpace(req.State) ? "Maharashtra" : req.State,
                Pincode = req.Pincode,
                Country = string.IsNullOrWhiteSpace(req.Country) ? "India" : req.Country,
            });
            await _db.SaveChangesAsync();
        }

        return (true, null, newId, null);
    }
}
