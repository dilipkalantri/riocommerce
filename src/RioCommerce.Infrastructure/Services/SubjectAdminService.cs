using System.Text.RegularExpressions;
using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Manages the Subjects lookup that fills the Subject dropdown on the product editor.
///
/// <para>The eight original subjects arrived as EF seed data and there was no screen behind them,
/// so changing the list meant writing SQL against the database by hand. This service is the whole
/// of the missing management layer — nothing about how subjects are consumed changed: the dropdown
/// still reads <c>Subjects.Where(IsActive).OrderBy(DisplayOrder).ThenBy(Name)</c>.</para>
///
/// <para><b>Retiring a subject means deactivating it, not deleting it.</b> <c>Product.SubjectId</c>
/// is a foreign key, so deleting a subject that courses point at would either fail or orphan them.
/// Delete is therefore refused while the subject is in use, and the screen leads with the toggle.</para>
/// </summary>
public sealed class SubjectAdminService : ISubjectAdminService
{
    private readonly RioCommerceDbContext _db;
    public SubjectAdminService(RioCommerceDbContext db) => _db = db;

    public async Task<List<SubjectAdminItem>> ListAsync()
    {
        // Counted in one grouped query rather than per row, so the page is two reads whatever the
        // subject count.
        // Counted over ProductSubject, not products.SubjectId: a combo that lists this subject
        // second is just as much a user of it, and showing 0 there would invite an admin to retire
        // a subject that real courses still teach.
        var usage = await _db.ProductSubjects
            .Select(ps => new { ps.SubjectId, ps.ProductId })
            // Union with the legacy column so a product whose links are not written yet still counts.
            .Concat(_db.Products.Where(p => p.SubjectId != null)
                .Select(p => new { SubjectId = p.SubjectId!.Value, ProductId = p.Id }))
            .Distinct()
            .GroupBy(x => x.SubjectId)
            .Select(g => new { SubjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SubjectId, x => x.Count);

        // Inactive subjects are listed too — this is the management screen, and a retired subject
        // has to stay visible to be brought back.
        var rows = await _db.Subjects.AsNoTracking()
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Slug, s.Level, s.DisplayOrder, s.IsActive })
            .ToListAsync();

        return rows.Select(s => new SubjectAdminItem(
            s.Id, s.Name, s.Slug, s.Level, s.DisplayOrder, s.IsActive,
            usage.TryGetValue(s.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<SubjectEditModel?> GetAsync(Guid id)
    {
        var s = await _db.Subjects.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return null;
        return new SubjectEditModel
        {
            Id = s.Id, Name = s.Name, Slug = s.Slug, Level = s.Level,
            DisplayOrder = s.DisplayOrder, IsActive = s.IsActive
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(SubjectEditModel m)
    {
        _db.ChangeTracker.Clear();   // self-contained write on the circuit-lived context

        var name = (m.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return (false, "Name is required.", Guid.Empty);

        var slug = Slugify(string.IsNullOrWhiteSpace(m.Slug) ? name : m.Slug!);

        // Both are checked because both are user-facing in their own way: a duplicate name makes the
        // product dropdown ambiguous, and the slug is what report and filter URLs key on.
        if (await _db.Subjects.AnyAsync(s => s.Id != m.Id && s.Name.ToLower() == name.ToLower()))
            return (false, $"A subject named “{name}” already exists.", Guid.Empty);
        if (await _db.Subjects.AnyAsync(s => s.Id != m.Id && s.Slug == slug))
            return (false, $"The slug “{slug}” is already used by another subject.", Guid.Empty);

        Subject entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = (await _db.Subjects.FirstOrDefaultAsync(s => s.Id == id))!;
            if (entity == null) return (false, "Subject not found.", Guid.Empty);
            entity.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            entity = new Subject { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            // A new subject with no explicit position goes to the end instead of colliding on 0 and
            // silently reshuffling the dropdown.
            if (m.DisplayOrder <= 0)
                m.DisplayOrder = await _db.Subjects.AnyAsync()
                    ? await _db.Subjects.MaxAsync(s => s.DisplayOrder) + 1
                    : 1;
            _db.Subjects.Add(entity);
        }

        entity.Name = name;
        entity.Slug = slug;
        entity.Level = m.Level;
        entity.DisplayOrder = m.DisplayOrder;
        entity.IsActive = m.IsActive;

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task<(bool ok, string? error)> ToggleAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var s = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return (false, "Subject not found.");
        s.IsActive = !s.IsActive;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();

        // Both directions are foreign keys — products.SubjectId and ProductSubjects.SubjectId — so
        // either one still pointing here blocks the delete. Counted as distinct courses so an admin
        // reads "3 courses", not a row count that double-counts a combo.
        var used = await _db.Products
            .CountAsync(p => p.SubjectId == id || _db.ProductSubjects.Any(ps => ps.ProductId == p.Id && ps.SubjectId == id));
        if (used > 0)
            return (false, $"Can't delete — {used} course(s) still use this subject. "
                         + "Reassign them first, or just switch the subject off to hide it from the dropdown.");

        var s = await _db.Subjects.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return (false, "Subject not found.");
        _db.Subjects.Remove(s);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private static string Slugify(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? Guid.NewGuid().ToString("n")[..8] : s;
    }
}
