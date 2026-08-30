using RioCommerce.Core.DTOs.Content;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class MenuAdminService : IMenuAdminService
{
    private readonly RioCommerceDbContext _db;
    public MenuAdminService(RioCommerceDbContext db) => _db = db;

    public async Task<List<MenuItemNode>> GetTreeAsync(bool enabledOnly = false)
    {
        var q = _db.MenuItems.AsNoTracking().AsQueryable();
        if (enabledOnly) q = q.Where(m => m.IsEnabled);
        var all = await q.OrderBy(m => m.DisplayOrder).ToListAsync();
        var byParent = all.ToLookup(m => m.ParentId);
        List<MenuItemNode> Build(Guid? pid) => byParent[pid].OrderBy(m => m.DisplayOrder)
            .Select(m => new MenuItemNode(m.Id, m.ParentId, m.Title, m.Url, m.IsEnabled, m.OpenInNewTab, m.DisplayOrder, Build(m.Id))).ToList();
        return Build(null);
    }

    public Task<List<MenuPagePick>> AvailablePagesAsync() =>
        _db.CmsPages.AsNoTracking().OrderBy(p => p.Title)
            .Select(p => new MenuPagePick(p.Id, p.Title, p.Slug)).ToListAsync();

    public Task<List<MenuPagePick>> AvailableCategoriesAsync() =>
        _db.Categories.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new MenuPagePick(c.Id, c.Name, c.Slug)).ToListAsync();

    public async Task AddPageItemAsync(Guid pageId, Guid? parentId)
    {
        _db.ChangeTracker.Clear();
        var p = await _db.CmsPages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pageId);
        if (p == null) return;
        await AddAsync(p.Title, $"/page/{p.Slug}", parentId);
    }

    public async Task AddCategoryItemAsync(Guid categoryId, Guid? parentId)
    {
        _db.ChangeTracker.Clear();
        var c = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == categoryId);
        if (c == null) return;
        await AddAsync(c.Name, $"/{c.Slug}", parentId);
    }

    public Task AddCustomItemAsync(string title, string url, Guid? parentId)
    {
        _db.ChangeTracker.Clear();
        return AddAsync(string.IsNullOrWhiteSpace(title) ? "Link" : title.Trim(),
            string.IsNullOrWhiteSpace(url) ? "#" : url.Trim(), parentId);
    }

    private async Task AddAsync(string title, string url, Guid? parentId)
    {
        if (parentId == Guid.Empty) parentId = null;
        var next = (await _db.MenuItems.Where(m => m.ParentId == parentId).Select(m => (int?)m.DisplayOrder).MaxAsync() ?? -1) + 1;
        _db.MenuItems.Add(new MenuItem { ParentId = parentId, Title = title, Url = url, DisplayOrder = next, IsEnabled = true });
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> UpdateItemAsync(Guid id, string title, string url, bool isEnabled, bool openInNewTab)
    {
        _db.ChangeTracker.Clear();
        if (string.IsNullOrWhiteSpace(title)) return (false, "Title is required.");
        var m = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return (false, "Menu item not found.");
        m.Title = title.Trim();
        m.Url = string.IsNullOrWhiteSpace(url) ? "#" : url.Trim();
        m.IsEnabled = isEnabled;
        m.OpenInNewTab = openInNewTab;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task MoveAsync(Guid id, bool up)
    {
        _db.ChangeTracker.Clear();
        var item = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item == null) return;
        var siblings = await _db.MenuItems.Where(m => m.ParentId == item.ParentId).OrderBy(m => m.DisplayOrder).ToListAsync();
        var idx = siblings.FindIndex(s => s.Id == id);
        var swap = up ? idx - 1 : idx + 1;
        if (idx < 0 || swap < 0 || swap >= siblings.Count) return;
        (siblings[idx].DisplayOrder, siblings[swap].DisplayOrder) = (siblings[swap].DisplayOrder, siblings[idx].DisplayOrder);
        await _db.SaveChangesAsync();
    }

    public async Task IndentAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var item = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is not { ParentId: null }) return;   // only top-level items can be indented (keep 2 levels)
        var prev = await _db.MenuItems.Where(m => m.ParentId == null && m.DisplayOrder < item.DisplayOrder)
            .OrderByDescending(m => m.DisplayOrder).FirstOrDefaultAsync();
        if (prev == null) return;
        // moving a section under another would orphan its children — block that.
        if (await _db.MenuItems.AnyAsync(m => m.ParentId == item.Id)) return;
        var next = (await _db.MenuItems.Where(m => m.ParentId == prev.Id).Select(m => (int?)m.DisplayOrder).MaxAsync() ?? -1) + 1;
        item.ParentId = prev.Id; item.DisplayOrder = next;
        await _db.SaveChangesAsync();
    }

    public async Task OutdentAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        var item = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == id);
        if (item is not { ParentId: not null }) return;   // only children can be outdented
        var next = (await _db.MenuItems.Where(m => m.ParentId == null).Select(m => (int?)m.DisplayOrder).MaxAsync() ?? -1) + 1;
        item.ParentId = null; item.DisplayOrder = next;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        _db.ChangeTracker.Clear();
        // Collect the item and EVERY descendant (arbitrary depth), then delete them.
        var edges = await _db.MenuItems.Select(m => new { m.Id, m.ParentId }).ToListAsync();
        var byParent = edges.ToLookup(e => e.ParentId);
        var toDelete = new List<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            toDelete.Add(cur);
            foreach (var child in byParent[cur]) stack.Push(child.Id);
        }
        var entities = await _db.MenuItems.Where(m => toDelete.Contains(m.Id)).ToListAsync();
        _db.MenuItems.RemoveRange(entities);   // EF orders self-referencing deletes children-first
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> SaveTreeOrderAsync(IReadOnlyList<MenuOrderItem> items)
    {
        _db.ChangeTracker.Clear();
        if (items.Count == 0) return (true, null);

        // ── Validate BEFORE touching the database ──
        var ids = items.Select(i => i.Id).ToList();
        if (ids.Distinct().Count() != ids.Count) return (false, "Duplicate menu items in the payload.");
        var idSet = ids.ToHashSet();

        var existing = (await _db.MenuItems.Select(m => m.Id).ToListAsync()).ToHashSet();
        if (!idSet.SetEquals(existing))
            return (false, "The payload must include every menu item exactly once.");

        var parentOf = items.ToDictionary(i => i.Id, i => i.ParentId);
        foreach (var it in items)
        {
            if (it.ParentId is not { } pid) continue;
            if (pid == it.Id) return (false, "A menu item cannot be its own parent.");
            if (!idSet.Contains(pid)) return (false, "A specified parent does not exist.");
        }

        // Cycle / self-descendant protection: walking up from any node must reach a null parent.
        foreach (var it in items)
        {
            var seen = new HashSet<Guid> { it.Id };
            var cur = it.ParentId;
            while (cur is { } p)
            {
                if (!seen.Add(p)) return (false, "Circular menu nesting is not allowed.");
                cur = parentOf.TryGetValue(p, out var next) ? next : null;
            }
        }

        // ── Apply transactionally ──
        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var all = await _db.MenuItems.ToListAsync();
            var map = all.ToDictionary(m => m.Id);
            foreach (var it in items)
            {
                if (!map.TryGetValue(it.Id, out var m)) continue;
                m.ParentId = it.ParentId;
                m.DisplayOrder = it.DisplayOrder;
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, ex.Message);
        }
    }
}
