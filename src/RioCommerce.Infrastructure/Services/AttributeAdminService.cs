using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

public class AttributeAdminService : IAttributeAdminService
{
    private readonly RioCommerceDbContext _db;
    public AttributeAdminService(RioCommerceDbContext db) => _db = db;

    // ─────────────── Product Attributes ───────────────

    public async Task<List<ProductAttributeAdminItem>> ListProductAttributesAsync()
    {
        var usage = await _db.ProductAttributeMappings
            .GroupBy(m => m.ProductAttributeId)
            .Select(g => new { g.Key, Count = g.Select(x => x.ProductId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var attrs = await _db.ProductAttributes
            .Select(a => new { a.Id, a.Name, a.Description, ValueCount = a.PredefinedValues.Count })
            .OrderBy(a => a.Name).ToListAsync();

        return attrs.Select(a => new ProductAttributeAdminItem(
            a.Id, a.Name, a.Description, a.ValueCount,
            usage.TryGetValue(a.Id, out var n) ? n : 0)).ToList();
    }

    public async Task<ProductAttributeEditModel?> GetProductAttributeAsync(Guid id)
    {
        var a = await _db.ProductAttributes
            .Include(x => x.PredefinedValues)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return null;
        return new ProductAttributeEditModel
        {
            Id = a.Id, Name = a.Name, Description = a.Description,
            PredefinedValues = a.PredefinedValues.OrderBy(v => v.DisplayOrder).Select(v => new PredefinedValueEditModel
            {
                Id = v.Id, Name = v.Name, PriceAdjustment = v.PriceAdjustment,
                PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage,
                IsPreSelected = v.IsPreSelected, DisplayOrder = v.DisplayOrder
            }).ToList()
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveProductAttributeAsync(ProductAttributeEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);
        if (await _db.ProductAttributes.AnyAsync(a => a.Name.ToLower() == m.Name.Trim().ToLower() && a.Id != (m.Id ?? Guid.Empty)))
            return (false, "Another product attribute already uses that name.", Guid.Empty);

        ProductAttribute entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.ProductAttributes.Include(a => a.PredefinedValues).FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new InvalidOperationException("Product attribute not found.");
        }
        else
        {
            entity = new ProductAttribute();
            _db.ProductAttributes.Add(entity);
        }

        entity.Name = m.Name.Trim();
        entity.Description = Clean(m.Description);

        // Sync predefined values: update matched, add new, remove dropped.
        var keepIds = m.PredefinedValues.Where(v => v.Id.HasValue).Select(v => v.Id!.Value).ToHashSet();
        foreach (var existing in entity.PredefinedValues.Where(v => !keepIds.Contains(v.Id)).ToList())
            _db.PredefinedProductAttributeValues.Remove(existing);

        foreach (var v in m.PredefinedValues)
        {
            if (string.IsNullOrWhiteSpace(v.Name)) continue;
            var target = v.Id.HasValue ? entity.PredefinedValues.FirstOrDefault(x => x.Id == v.Id) : null;
            if (target == null)
            {
                target = new PredefinedProductAttributeValue();
                entity.PredefinedValues.Add(target);
            }
            target.Name = v.Name.Trim();
            target.PriceAdjustment = v.PriceAdjustment;
            target.PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage;
            target.IsPreSelected = v.IsPreSelected;
            target.DisplayOrder = v.DisplayOrder;
        }

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task<(bool ok, string? error)> DeleteProductAttributeAsync(Guid id)
    {
        if (await _db.ProductAttributeMappings.AnyAsync(m => m.ProductAttributeId == id))
            return (false, "Can't delete — this attribute is mapped to one or more products. Remove those mappings first.");
        var a = await _db.ProductAttributes.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return (false, "Product attribute not found.");
        _db.ProductAttributes.Remove(a);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // ─────────────── Specification Attributes ───────────────

    public async Task<List<SpecificationAttributeAdminItem>> ListSpecificationAttributesAsync()
    {
        var rows = await _db.SpecificationAttributes
            .Select(a => new
            {
                a.Id, a.Name, a.DisplayOrder,
                GroupName = a.Group != null ? a.Group.Name : null,
                OptionCount = a.Options.Count
            })
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name).ToListAsync();

        return rows.Select(a => new SpecificationAttributeAdminItem(
            a.Id, a.Name, a.GroupName, a.OptionCount, a.DisplayOrder)).ToList();
    }

    public async Task<SpecificationAttributeEditModel?> GetSpecificationAttributeAsync(Guid id)
    {
        var a = await _db.SpecificationAttributes.Include(x => x.Options).FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return null;
        return new SpecificationAttributeEditModel
        {
            Id = a.Id, Name = a.Name, DisplayOrder = a.DisplayOrder,
            SpecificationAttributeGroupId = a.SpecificationAttributeGroupId,
            Options = a.Options.OrderBy(o => o.DisplayOrder).Select(o => new SpecOptionEditModel
            {
                Id = o.Id, Name = o.Name, ColorSquaresRgb = o.ColorSquaresRgb, DisplayOrder = o.DisplayOrder
            }).ToList()
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveSpecificationAttributeAsync(SpecificationAttributeEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);

        SpecificationAttribute entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.SpecificationAttributes.Include(a => a.Options).FirstOrDefaultAsync(a => a.Id == id)
                ?? throw new InvalidOperationException("Specification attribute not found.");
        }
        else
        {
            entity = new SpecificationAttribute();
            _db.SpecificationAttributes.Add(entity);
        }

        entity.Name = m.Name.Trim();
        entity.DisplayOrder = m.DisplayOrder;
        entity.SpecificationAttributeGroupId = m.SpecificationAttributeGroupId;

        var keepIds = m.Options.Where(o => o.Id.HasValue).Select(o => o.Id!.Value).ToHashSet();
        foreach (var existing in entity.Options.Where(o => !keepIds.Contains(o.Id)).ToList())
            _db.SpecificationAttributeOptions.Remove(existing);

        foreach (var o in m.Options)
        {
            if (string.IsNullOrWhiteSpace(o.Name)) continue;
            var target = o.Id.HasValue ? entity.Options.FirstOrDefault(x => x.Id == o.Id) : null;
            if (target == null)
            {
                target = new SpecificationAttributeOption();
                entity.Options.Add(target);
            }
            target.Name = o.Name.Trim();
            target.ColorSquaresRgb = Clean(o.ColorSquaresRgb);
            target.DisplayOrder = o.DisplayOrder;
        }

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task<(bool ok, string? error)> DeleteSpecificationAttributeAsync(Guid id)
    {
        var optionIds = await _db.SpecificationAttributeOptions.Where(o => o.SpecificationAttributeId == id).Select(o => o.Id).ToListAsync();
        if (optionIds.Count > 0 && await _db.ProductSpecificationAttributes.AnyAsync(p => optionIds.Contains(p.SpecificationAttributeOptionId)))
            return (false, "Can't delete — options of this attribute are assigned to products.");

        var a = await _db.SpecificationAttributes.FirstOrDefaultAsync(x => x.Id == id);
        if (a == null) return (false, "Specification attribute not found.");
        _db.SpecificationAttributes.Remove(a);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<List<SpecAttributeGroupItem>> ListSpecGroupsAsync()
    {
        var rows = await _db.SpecificationAttributeGroups
            .Select(g => new { g.Id, g.Name, g.DisplayOrder, Count = g.SpecificationAttributes.Count })
            .OrderBy(g => g.DisplayOrder).ThenBy(g => g.Name).ToListAsync();
        return rows.Select(g => new SpecAttributeGroupItem(g.Id, g.Name, g.DisplayOrder, g.Count)).ToList();
    }

    public async Task<(bool ok, string? error, Guid id)> SaveSpecGroupAsync(SpecAttributeGroupEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);

        SpecificationAttributeGroup entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.SpecificationAttributeGroups.FirstOrDefaultAsync(g => g.Id == id)
                ?? throw new InvalidOperationException("Group not found.");
        }
        else
        {
            entity = new SpecificationAttributeGroup();
            _db.SpecificationAttributeGroups.Add(entity);
        }

        entity.Name = m.Name.Trim();
        entity.DisplayOrder = m.DisplayOrder;
        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task<(bool ok, string? error)> DeleteSpecGroupAsync(Guid id)
    {
        var g = await _db.SpecificationAttributeGroups.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return (false, "Group not found.");
        // FK is ON DELETE SET NULL, so member attributes simply become ungrouped.
        _db.SpecificationAttributeGroups.Remove(g);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // ─────────────── Checkout Attributes ───────────────

    public async Task<List<CheckoutAttributeAdminItem>> ListCheckoutAttributesAsync()
    {
        var rows = await _db.CheckoutAttributes
            .Select(c => new
            {
                c.Id, c.Name, c.ControlType, c.IsRequired, c.IsActive, c.DisplayOrder,
                ValueCount = c.Values.Count
            })
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync();

        return rows.Select(c => new CheckoutAttributeAdminItem(
            c.Id, c.Name, c.ControlType, c.IsRequired, c.IsActive, c.ValueCount, c.DisplayOrder)).ToList();
    }

    public async Task<CheckoutAttributeEditModel?> GetCheckoutAttributeAsync(Guid id)
    {
        var c = await _db.CheckoutAttributes.Include(x => x.Values).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return null;
        return new CheckoutAttributeEditModel
        {
            Id = c.Id, Name = c.Name, TextPrompt = c.TextPrompt, IsRequired = c.IsRequired,
            ControlType = c.ControlType, DisplayOrder = c.DisplayOrder, IsActive = c.IsActive,
            Values = c.Values.OrderBy(v => v.DisplayOrder).Select(v => new CheckoutValueEditModel
            {
                Id = v.Id, Name = v.Name, PriceAdjustment = v.PriceAdjustment,
                PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage,
                IsPreSelected = v.IsPreSelected, DisplayOrder = v.DisplayOrder
            }).ToList()
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveCheckoutAttributeAsync(CheckoutAttributeEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) return (false, "Name is required.", Guid.Empty);

        CheckoutAttribute entity;
        if (m.Id is { } id && id != Guid.Empty)
        {
            entity = await _db.CheckoutAttributes.Include(c => c.Values).FirstOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("Checkout attribute not found.");
        }
        else
        {
            entity = new CheckoutAttribute();
            _db.CheckoutAttributes.Add(entity);
        }

        entity.Name = m.Name.Trim();
        entity.TextPrompt = Clean(m.TextPrompt);
        entity.IsRequired = m.IsRequired;
        entity.ControlType = m.ControlType;
        entity.DisplayOrder = m.DisplayOrder;
        entity.IsActive = m.IsActive;

        var keepIds = m.Values.Where(v => v.Id.HasValue).Select(v => v.Id!.Value).ToHashSet();
        foreach (var existing in entity.Values.Where(v => !keepIds.Contains(v.Id)).ToList())
            _db.CheckoutAttributeValues.Remove(existing);

        foreach (var v in m.Values)
        {
            if (string.IsNullOrWhiteSpace(v.Name)) continue;
            var target = v.Id.HasValue ? entity.Values.FirstOrDefault(x => x.Id == v.Id) : null;
            if (target == null)
            {
                target = new CheckoutAttributeValue();
                entity.Values.Add(target);
            }
            target.Name = v.Name.Trim();
            target.PriceAdjustment = v.PriceAdjustment;
            target.PriceAdjustmentUsePercentage = v.PriceAdjustmentUsePercentage;
            target.IsPreSelected = v.IsPreSelected;
            target.DisplayOrder = v.DisplayOrder;
        }

        await _db.SaveChangesAsync();
        return (true, null, entity.Id);
    }

    public async Task ToggleCheckoutAttributeAsync(Guid id)
    {
        var c = await _db.CheckoutAttributes.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return;
        c.IsActive = !c.IsActive;
        await _db.SaveChangesAsync();
    }

    public async Task<(bool ok, string? error)> DeleteCheckoutAttributeAsync(Guid id)
    {
        var c = await _db.CheckoutAttributes.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return (false, "Checkout attribute not found.");
        _db.CheckoutAttributes.Remove(c);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
