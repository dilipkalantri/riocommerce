using RioCommerce.Core.DTOs.Admin;
using RioCommerce.Core.DTOs.Faculty;
using RioCommerce.Core.DTOs.Meta;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services;

/// <summary>
/// Product faculty share administration: rule CRUD, the per-product multi-faculty editor, the earned
/// ledger and payout aggregation.
///
/// <para>All share arithmetic goes through <see cref="IFacultyShareCalculator"/>. This class
/// previously computed shares itself as <c>SellingPrice × pct / 100</c> in two places, which applied
/// the share to the GST-INCLUSIVE gross, ignored the special price, and never asked whether the
/// faculty was GST-registered. Those three defects are exactly what the share specification exists to
/// fix — see <c>docs/faculty-share-calculation.md</c>.</para>
/// </summary>
public class FacultySharingService : IFacultySharingService
{
    private static readonly OrderStatus[] NonRevenue = { OrderStatus.Draft, OrderStatus.Cancelled, OrderStatus.Refunded };

    private readonly RioCommerceDbContext _db;
    private readonly IFacultyShareCalculator _calc;
    private readonly IAuditService _audit;

    public FacultySharingService(RioCommerceDbContext db, IFacultyShareCalculator calc, IAuditService audit)
    {
        _db = db; _calc = calc; _audit = audit;
    }

    // ── Rule listing ────────────────────────────────────────────────────────────────────────
    public async Task<List<SharingRuleRow>> ListRulesAsync()
    {
        var settings = await _calc.GetSettingsAsync();
        var rules = await _db.FacultySharingRules.AsNoTracking()
            .Include(r => r.Product).Include(r => r.Faculty)
            .OrderBy(r => r.Product.Title).ThenBy(r => r.Faculty.DisplayName)
            .ToListAsync();
        if (rules.Count == 0) return new();

        // Group by product so each row can report the product's COMBINED allocation — the number that
        // actually matters when several faculty share one product, and the one a per-rule view hides.
        var byProduct = rules.GroupBy(r => r.ProductId);
        var now = DateTime.UtcNow;
        var rows = new List<SharingRuleRow>(rules.Count);

        // Attachments are needed even though every row here has a rule: the product's combined
        // allocation includes attached faculty earning via the default, and the default must reach
        // exactly those and no one else.
        var attachedByProduct = await LoadAttachmentsAsync(rules.Select(r => r.ProductId).Distinct().ToList());
        var attachedInfo = await LoadFacultyInfoAsync(
            attachedByProduct.Values.SelectMany(s => s).Distinct().ToList());

        foreach (var group in byProduct)
        {
            var product = group.First().Product;
            var attached = attachedByProduct.TryGetValue(product.Id, out var a) ? a : new HashSet<Guid>();

            var faculty = group.ToDictionary(
                r => r.FacultyId,
                r => FacultyShareCalculator.Info(r.Faculty));
            foreach (var fid in attached)
                if (!faculty.ContainsKey(fid) && attachedInfo.TryGetValue(fid, out var info))
                    faculty[fid] = info;

            var result = _calc.Calculate(product, group.ToList(), faculty, attached, settings, now);
            var lineByFaculty = result.Lines.ToDictionary(l => l.FacultyId);

            foreach (var rule in group)
            {
                lineByFaculty.TryGetValue(rule.FacultyId, out var line);
                var inEffect = rule.AppliesAt(now);

                rows.Add(new SharingRuleRow
                {
                    Id = rule.Id,
                    ProductId = rule.ProductId,
                    FacultyId = rule.FacultyId,
                    ProductTitle = product.Title,
                    Sku = product.Sku,
                    Level = product.Level,
                    FacultyName = rule.Faculty.DisplayName,
                    FacultyShortCode = rule.Faculty.ShortCode,
                    ShareType = rule.ShareType,
                    ShareValue = rule.ShareValue,
                    ProductPrice = result.ProductPrice,
                    EffectivePrice = result.EffectivePrice,
                    GstRate = result.GstRate,
                    TaxableBase = result.TaxableBase,
                    // Zero rather than a stale number when the rule isn't in effect — a dated-future
                    // rate must not display as though it were already earning.
                    EffectiveAmount = line?.ShareAmount ?? 0m,
                    GstOnShare = line?.GstOnShare ?? 0m,
                    TotalPayout = line?.TotalPayout ?? 0m,
                    FacultyIsGstRegistered = rule.Faculty.IsGstRegisteredForShare,
                    SharePctOfBase = line?.SharePctOfBase ?? 0m,
                    ProductTotalSharePct = result.TotalSharePctOfBase,
                    ProductExceedsCap = result.ExceedsConfiguredCap,
                    WasCapped = line?.WasCapped ?? false,
                    IsActive = rule.IsActive,
                    IsInEffect = inEffect,
                    EffectiveFrom = rule.EffectiveFrom,
                    EffectiveTo = rule.EffectiveTo,
                    Notes = rule.Notes
                });
            }
        }

        return rows.OrderBy(r => r.ProductTitle).ThenByDescending(r => r.TotalPayout).ToList();
    }

    public async Task<SharingOptions> OptionsAsync()
    {
        var products = await _db.Products.Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.Title).Select(p => new IdName(p.Id, p.Title)).ToListAsync();
        var faculty = await _db.Faculty.Where(f => f.IsActive)
            .OrderBy(f => f.DisplayOrder).Select(f => new IdName(f.Id, f.DisplayName)).ToListAsync();
        return new SharingOptions(products, faculty);
    }

    // ── Single-rule upsert ──────────────────────────────────────────────────────────────────
    public async Task<(bool ok, string? error)> SaveRuleAsync(SharingRuleEdit r)
    {
        if (r.ProductId == Guid.Empty) return (false, "Select a product.");
        if (r.FacultyId == Guid.Empty) return (false, "Select a faculty member.");
        if (r.ShareValue <= 0m) return (false, "Share value must be greater than zero.");
        if (r.ShareType == SharingType.Percentage && r.ShareValue > 100m)
            return (false, "A percentage share cannot exceed 100%.");
        if (r.EffectiveFrom is { } from && r.EffectiveTo is { } to && to <= from)
            return (false, "Effective To must be after Effective From.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == r.ProductId);
        if (product == null) return (false, "Product not found.");

        // One rule per (product, faculty). Without this the payout would credit the faculty once per
        // duplicate row — the Excel importer already dedupes on this pair, so the UI must too.
        var existing = r.Id is Guid id
            ? await _db.FacultySharingRules.FirstOrDefaultAsync(x => x.Id == id)
            : await _db.FacultySharingRules.FirstOrDefaultAsync(
                x => x.ProductId == r.ProductId && x.FacultyId == r.FacultyId);

        if (r.Id is null && existing != null)
            return (false, "This faculty already has a share on this product — edit the existing rule instead.");

        // Editing a rule can also repoint it at a different product/faculty, which would collide with
        // that pair's own rule. Checked explicitly so the user gets this message rather than a raw
        // unique-constraint violation.
        if (existing != null && await _db.FacultySharingRules.AnyAsync(
                x => x.Id != existing.Id && x.ProductId == r.ProductId && x.FacultyId == r.FacultyId))
            return (false, "This faculty already has a share on that product — edit that rule instead.");

        // A NEW share requires the faculty to actually be on the product. Existing rules are exempt:
        // one whose attachment was later removed stays editable and keeps paying, so a course roster
        // change never silently cancels or freezes a live agreement.
        if (existing == null && !await IsAttachedAsync(r.ProductId, r.FacultyId))
            return (false, "This faculty is not assigned to that product. Add them to the product's " +
                           "Faculty list first, then set their share.");

        var rule = existing ?? new FacultySharingRule();
        if (existing == null) _db.FacultySharingRules.Add(rule);

        rule.ProductId = r.ProductId;
        rule.FacultyId = r.FacultyId;
        rule.ShareType = r.ShareType;
        rule.ShareValue = r.ShareValue;
        rule.IsActive = r.IsActive;
        rule.EffectiveFrom = Utc(r.EffectiveFrom);
        rule.EffectiveTo = Utc(r.EffectiveTo);
        rule.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes;
        rule.UpdatedAt = DateTime.UtcNow;

        var (capOk, capError) = await ValidateProductCapAsync(product, rule);
        if (!capOk)
        {
            // Detach so a rejected save leaves no partial state behind in the change tracker.
            if (existing == null) _db.Entry(rule).State = EntityState.Detached;
            else await ReloadAsync(rule);
            return (false, capError);
        }

        rule.EffectiveAmount = await ResolveEffectiveAmountAsync(product, rule);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task DeleteRuleAsync(Guid id)
    {
        var rule = await _db.FacultySharingRules.FirstOrDefaultAsync(x => x.Id == id);
        if (rule != null) { _db.FacultySharingRules.Remove(rule); await _db.SaveChangesAsync(); }
    }

    // ── Product editor: load ────────────────────────────────────────────────────────────────
    public async Task<ProductFacultyShareEditModel?> GetProductShareEditModelAsync(
        Guid productId, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return null;

        var settings = await _calc.GetSettingsAsync(ct);
        var rules = await _db.FacultySharingRules.AsNoTracking()
            .Where(r => r.ProductId == productId).ToListAsync(ct);

        // Rows are driven by the product's attached faculty UNION anyone holding a rule. A rule for a
        // detached faculty still has to be visible and editable — it is a live agreement, and hiding
        // it would leave money being paid out with no UI that admits it exists.
        var attachedIds = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => pf.ProductId == productId).Select(pf => pf.FacultyId).ToListAsync(ct);
        var facultyIds = attachedIds.Concat(rules.Select(r => r.FacultyId)).Distinct().ToList();

        var faculty = facultyIds.Count == 0
            ? new List<Core.Entities.Faculty>()
            : await _db.Faculty.AsNoTracking().Where(f => facultyIds.Contains(f.Id))
                .OrderBy(f => f.DisplayOrder).ThenBy(f => f.DisplayName).ToListAsync(ct);

        var infoMap = faculty.ToDictionary(f => f.Id, FacultyShareCalculator.Info);
        var result = _calc.Calculate(product, rules, infoMap, attachedIds.ToHashSet(), settings);
        var lineByFaculty = result.Lines.ToDictionary(l => l.FacultyId);
        var ruleByFaculty = rules.ToDictionary(r => r.FacultyId);

        var model = new ProductFacultyShareEditModel
        {
            EnableDefaultFacultyShare = product.EnableDefaultFacultyShare,
            DefaultFacultyShareType = product.DefaultFacultyShareType,
            DefaultFacultyShareValue = product.DefaultFacultyShareValue,
            TaxableBase = result.TaxableBase,
            EffectivePrice = result.EffectivePrice,
            GstRate = result.GstRate,
            TotalSharePctOfBase = result.TotalSharePctOfBase,
            TotalPayout = result.TotalPayout,
            MaxTotalSharePct = settings.MaxTotalSharePct,
            EnforceMaxTotalShare = settings.EnforceMaxTotalShare,
            ExceedsConfiguredCap = result.ExceedsConfiguredCap
        };

        foreach (var f in faculty)
        {
            ruleByFaculty.TryGetValue(f.Id, out var rule);
            lineByFaculty.TryGetValue(f.Id, out var line);

            model.Rows.Add(new ProductFacultyShareEditRow
            {
                RuleId = rule?.Id,
                FacultyId = f.Id,
                FacultyName = f.DisplayName,
                FacultyShortCode = f.ShortCode,
                FacultyIsGstRegistered = f.IsGstRegisteredForShare,
                HasExplicitRule = rule != null,
                // An unruled faculty pre-fills with the product default, so ticking "own rate" starts
                // from what they are currently earning rather than from zero.
                ShareType = rule?.ShareType ?? product.DefaultFacultyShareType,
                ShareValue = rule?.ShareValue ?? product.DefaultFacultyShareValue,
                IsActive = rule?.IsActive ?? true,
                EffectiveFrom = rule?.EffectiveFrom,
                EffectiveTo = rule?.EffectiveTo,
                Notes = rule?.Notes,
                ShareAmount = line?.ShareAmount ?? 0m,
                GstOnShare = line?.GstOnShare ?? 0m,
                TotalPayout = line?.TotalPayout ?? 0m,
                SharePctOfBase = line?.SharePctOfBase ?? 0m,
                Source = line?.Source ?? FacultyShareSource.None,
                WasCapped = line?.WasCapped ?? false
            });
        }

        return model;
    }

    // ── Product editor: save ────────────────────────────────────────────────────────────────
    public async Task<(bool ok, string? error)> SaveProductSharesAsync(
        Guid productId, ProductFacultyShareEditModel model, Guid? actorUserId, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return (false, "Product not found.");

        var settings = await _calc.GetSettingsAsync(ct);

        foreach (var row in model.Rows.Where(r => r.HasExplicitRule))
        {
            if (row.ShareValue <= 0m)
                return (false, $"{row.FacultyName}: share value must be greater than zero (or untick their own rate).");
            if (row.ShareType == SharingType.Percentage && row.ShareValue > 100m)
                return (false, $"{row.FacultyName}: a percentage share cannot exceed 100%.");
            if (row.EffectiveFrom is { } f && row.EffectiveTo is { } t && t <= f)
                return (false, $"{row.FacultyName}: Effective To must be after Effective From.");
        }

        // Project the SUBMITTED rules over the PERSISTED product and check the combined allocation
        // before writing anything — per-row validation cannot catch three individually-valid shares
        // that only break the ceiling together, which is the whole risk of multi-faculty configuration.
        //
        // The product-level default is deliberately NOT read from the model here, nor written below:
        // it is owned by the product save (ProductEditModel / ProductAdminService), exactly like
        // EnableDefaultFranchiseShare. One writer per field means this grid cannot clobber a default
        // the admin changed in the Prices card during the same visit.
        var candidateRules = ProjectSubmittedRules(productId, model);
        var infoMap = await LoadFacultyInfoAsync(model.Rows.Select(r => r.FacultyId).ToList(), ct);
        // Loaded fresh rather than inferred from the submitted rows: the grid shows rule-holders who
        // are no longer attached, and treating those as attached would let them pick up the default.
        var attached = (await LoadAttachmentsAsync(new List<Guid> { productId }, ct))
            .TryGetValue(productId, out var att) ? att : new HashSet<Guid>();

        // A NEW share requires attachment. The editor's own grid can only offer attached faculty and
        // existing rule-holders, so this is unreachable from the UI — but this method is also an API
        // endpoint, and a crafted payload could name any faculty. Existing rules are exempt so a
        // detached legacy agreement stays editable.
        var alreadyRuled = (await _db.FacultySharingRules.AsNoTracking()
                .Where(r => r.ProductId == productId).Select(r => r.FacultyId).ToListAsync(ct))
            .ToHashSet();
        foreach (var row in model.Rows.Where(r => r.HasExplicitRule))
        {
            if (attached.Contains(row.FacultyId) || alreadyRuled.Contains(row.FacultyId)) continue;
            return (false, $"{row.FacultyName} is not assigned to this product. " +
                           "Add them to the product's Faculty list first, then set their share.");
        }

        var preview = _calc.Calculate(product, candidateRules, infoMap, attached, settings);
        if (settings.EnforceMaxTotalShare && preview.ExceedsConfiguredCap)
            return (false, $"Total faculty share is {preview.TotalSharePctOfBase:0.##}% of the taxable base, " +
                           $"which exceeds the {settings.MaxTotalSharePct:0.##}% limit. " +
                           "Reduce a share, or raise the limit in Faculty Share Settings.");

        // ── Persist ──
        var existing = await _db.FacultySharingRules.Where(r => r.ProductId == productId).ToListAsync(ct);
        var existingByFaculty = existing.ToDictionary(r => r.FacultyId);
        var now = DateTime.UtcNow;

        foreach (var row in model.Rows)
        {
            existingByFaculty.TryGetValue(row.FacultyId, out var rule);

            if (!row.HasExplicitRule)
            {
                // Un-ticked: drop the explicit rule so the faculty falls back to the product default.
                if (rule != null) _db.FacultySharingRules.Remove(rule);
                continue;
            }

            if (rule == null)
            {
                rule = new FacultySharingRule { ProductId = productId, FacultyId = row.FacultyId };
                _db.FacultySharingRules.Add(rule);
            }

            rule.ShareType = row.ShareType;
            rule.ShareValue = row.ShareValue;
            rule.IsActive = row.IsActive;
            rule.EffectiveFrom = Utc(row.EffectiveFrom);
            rule.EffectiveTo = Utc(row.EffectiveTo);
            rule.Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes;
            rule.UpdatedAt = now;
            // Denormalised convenience column, refreshed from the calculator on every save. It is a
            // cache of a derived value, so nothing reads it for money decisions.
            rule.EffectiveAmount = preview.Lines.FirstOrDefault(l => l.FacultyId == row.FacultyId)?.ShareAmount ?? 0m;
        }

        // A faculty removed from the product entirely (no row submitted) keeps their rule — see the
        // load path. Only an explicit untick deletes.
        await _db.SaveChangesAsync(ct);

        if (actorUserId is Guid actor)
        {
            await _audit.LogAsync(actor, "Admin", "ProductFacultySharesSaved", "Product", productId.ToString(),
                $"Rules={model.Rows.Count(r => r.HasExplicitRule)}; " +
                $"Total={preview.TotalSharePctOfBase:0.##}% of base; Payout=₹{preview.TotalPayout:0.##}/unit");
        }

        return (true, null);
    }

    public async Task<ProductFacultyShareEditModel?> PreviewProductSharesAsync(
        Guid productId, ProductFacultyShareEditModel model, CancellationToken ct = default)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product == null) return null;

        var settings = await _calc.GetSettingsAsync(ct);
        var infoMap = await LoadFacultyInfoAsync(model.Rows.Select(r => r.FacultyId).ToList(), ct);
        var attached = (await LoadAttachmentsAsync(new List<Guid> { productId }, ct))
            .TryGetValue(productId, out var att) ? att : new HashSet<Guid>();

        // The product default is read from the PERSISTED product, not the model: the grid does not own
        // that field, so previewing must not pretend an unsaved change to it has taken effect.
        var result = _calc.Calculate(
            product, ProjectSubmittedRules(productId, model), infoMap, attached, settings);
        var lineByFaculty = result.Lines.ToDictionary(l => l.FacultyId);

        model.TaxableBase = result.TaxableBase;
        model.EffectivePrice = result.EffectivePrice;
        model.GstRate = result.GstRate;
        model.TotalSharePctOfBase = result.TotalSharePctOfBase;
        model.TotalPayout = result.TotalPayout;
        model.MaxTotalSharePct = settings.MaxTotalSharePct;
        model.EnforceMaxTotalShare = settings.EnforceMaxTotalShare;
        model.ExceedsConfiguredCap = result.ExceedsConfiguredCap;
        model.EnableDefaultFacultyShare = product.EnableDefaultFacultyShare;
        model.DefaultFacultyShareType = product.DefaultFacultyShareType;
        model.DefaultFacultyShareValue = product.DefaultFacultyShareValue;

        foreach (var row in model.Rows)
        {
            lineByFaculty.TryGetValue(row.FacultyId, out var line);
            row.ShareAmount = line?.ShareAmount ?? 0m;
            row.GstOnShare = line?.GstOnShare ?? 0m;
            row.TotalPayout = line?.TotalPayout ?? 0m;
            row.SharePctOfBase = line?.SharePctOfBase ?? 0m;
            row.Source = line?.Source ?? FacultyShareSource.None;
            row.WasCapped = line?.WasCapped ?? false;
            row.FacultyIsGstRegistered = infoMap.TryGetValue(row.FacultyId, out var info) && info.IsGstRegistered;
        }

        return model;
    }

    public Task<List<ProductFacultyShareSummary>> ListProductSharesAsync(
        bool onlyWithShare = false, CancellationToken ct = default)
        => _calc.GetProductSharesAsync(onlyWithShare, ct);

    // ── Bulk assign ─────────────────────────────────────────────────────────────────────────
    public async Task<FacultyBulkAssignResult> BulkAssignSharesAsync(
        BulkAssignFacultySharesRequest req, Guid actorUserId, bool dryRun = false, CancellationToken ct = default)
    {
        // ── Validate the rate itself ──
        if (req.ProductIds.Count == 0)
            return Fail("Select at least one product.");
        if (req.Value <= 0m)
            return Fail("Enter a share value greater than zero.");
        if (req.Type == SharingType.Percentage && req.Value > 100m)
            return Fail("A percentage share cannot exceed 100%.");
        if (req.EffectiveFrom is { } ef && req.EffectiveTo is { } et && et <= ef)
            return Fail("Effective To must be after Effective From.");

        // ── Resolve target faculty ──
        List<Guid> facultyIds;
        if (req.AllFaculty)
        {
            facultyIds = await _db.Faculty.Where(f => f.IsActive).Select(f => f.Id).ToListAsync(ct);
        }
        else
        {
            facultyIds = req.FacultyIds.Distinct().ToList();
            if (facultyIds.Count == 0) return Fail("Select at least one faculty member (or choose all).");
            var found = await _db.Faculty.CountAsync(f => facultyIds.Contains(f.Id), ct);
            if (found != facultyIds.Count) return Fail("One or more selected faculty were not found.");
        }
        if (facultyIds.Count == 0) return Fail("No faculty to assign.");

        var productIds = req.ProductIds.Distinct().ToList();
        var settings = await _calc.GetSettingsAsync(ct);

        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count == 0) return Fail("None of the selected products were found.");

        var facultyEntities = await _db.Faculty.AsNoTracking()
            .Where(f => facultyIds.Contains(f.Id)).ToListAsync(ct);
        var infoMap = facultyEntities.ToDictionary(f => f.Id, FacultyShareCalculator.Info);

        var attachments = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId }).ToListAsync(ct);
        var attachedByProduct = attachments.GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToHashSet());

        // Existing rules for the affected products — ALL of them, not just the targeted pairs. A
        // product's combined allocation includes rules this operation isn't touching, so a file adding
        // one 60% rate to a product that already has 60% elsewhere has to fail.
        var existingRules = await _db.FacultySharingRules
            .Where(r => productIds.Contains(r.ProductId)).ToListAsync(ct);
        var rulesByProduct = existingRules.GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Faculty referenced by an untouched rule still needs GST info for the projection.
        var extraFacultyIds = existingRules.Select(r => r.FacultyId)
            .Concat(attachments.Select(a => a.FacultyId))
            .Distinct().Where(id => !infoMap.ContainsKey(id)).ToList();
        if (extraFacultyIds.Count > 0)
        {
            foreach (var f in await _db.Faculty.AsNoTracking()
                         .Where(f => extraFacultyIds.Contains(f.Id)).ToListAsync(ct))
                infoMap[f.Id] = FacultyShareCalculator.Info(f);
        }

        var now = DateTime.UtcNow;
        var from = Utc(req.EffectiveFrom);
        var to = Utc(req.EffectiveTo);

        // ── Project the outcome per product ──
        var rows = new List<FacultyBulkAssignPreviewRow>(productIds.Count);
        // productId → the pairs that would actually be written
        var plan = new Dictionary<Guid, List<(Guid FacultyId, FacultySharingRule? Existing)>>();

        foreach (var pid in productIds)
        {
            if (!products.TryGetValue(pid, out var product)) continue;

            rulesByProduct.TryGetValue(pid, out var current);
            current ??= new List<FacultySharingRule>();
            attachedByProduct.TryGetValue(pid, out var attached);
            attached ??= new HashSet<Guid>();

            var targets = new List<(Guid FacultyId, FacultySharingRule? Existing)>();
            var skippedNotAttached = 0;

            foreach (var fid in facultyIds)
            {
                var existingRule = current.FirstOrDefault(r => r.FacultyId == fid);
                // Create only where attached; update wherever a rule already exists. A detached
                // rule-holder is still updated so a bulk rate change doesn't quietly skip a legacy
                // agreement — but no NEW unattached share is ever created.
                if (existingRule == null && !attached.Contains(fid)) { skippedNotAttached++; continue; }
                targets.Add((fid, existingRule));
            }

            var createdCount = targets.Count(t => t.Existing == null);
            var updatedCount = targets.Count(t => t.Existing != null);

            // Build the map the calculator needs for this product, and the BEFORE / AFTER rule sets.
            var map = new Dictionary<Guid, FacultyGstInfo>();
            foreach (var fid in attached.Concat(current.Select(r => r.FacultyId))
                         .Concat(targets.Select(t => t.FacultyId)).Distinct())
                if (infoMap.TryGetValue(fid, out var info)) map[fid] = info;

            var before = _calc.Calculate(product, current, map, attached, settings, now);

            // Detached projections — nothing here is tracked, so a rejected preview leaves no residue.
            var projected = current
                .Where(r => targets.All(t => t.FacultyId != r.FacultyId))
                .Select(r => Clone(r))
                .Concat(targets.Select(t => new FacultySharingRule
                {
                    Id = t.Existing?.Id ?? Guid.NewGuid(),
                    ProductId = pid,
                    FacultyId = t.FacultyId,
                    ShareType = req.Type,
                    ShareValue = req.Value,
                    IsActive = true,
                    EffectiveFrom = from,
                    EffectiveTo = to
                }))
                .ToList();

            var after = _calc.Calculate(product, projected, map, attached, settings, now);
            var exceeds = settings.EnforceMaxTotalShare && after.ExceedsConfiguredCap;

            string? skipReason = null;
            if (targets.Count == 0)
                skipReason = skippedNotAttached > 0
                    ? "None of the selected faculty are attached to this product."
                    : "No faculty to assign.";
            else if (exceeds)
                skipReason = $"Would total {after.TotalSharePctOfBase:0.##}% of the taxable base, " +
                             $"over the {settings.MaxTotalSharePct:0.##}% limit.";

            rows.Add(new FacultyBulkAssignPreviewRow
            {
                ProductId = pid,
                ProductTitle = product.Title,
                Sku = product.Sku,
                RulesCreated = createdCount,
                RulesUpdated = updatedCount,
                SkippedNotAttached = skippedNotAttached,
                CurrentSharePct = before.TotalSharePctOfBase,
                ResultingSharePct = after.TotalSharePctOfBase,
                MaxTotalSharePct = settings.MaxTotalSharePct,
                ExceedsCap = after.ExceedsConfiguredCap,
                ResultingPayoutPerUnit = after.TotalPayout,
                SkipReason = skipReason
            });

            if (skipReason == null) plan[pid] = targets;
        }

        var breaches = rows.Where(r => r.ExceedsCap && settings.EnforceMaxTotalShare).ToList();

        // ── All-or-nothing unless the caller opted into skipping ──
        if (breaches.Count > 0 && !req.SkipProductsOverCap)
        {
            return new FacultyBulkAssignResult
            {
                Ok = false,
                DryRun = dryRun,
                Error = $"{breaches.Count} product(s) would exceed the {settings.MaxTotalSharePct:0.##}% " +
                        "faculty share limit. Reduce the value, deselect those products, or tick " +
                        "\"Skip products over the limit\" to apply to the rest.",
                Rows = rows,
                ProductsSkipped = rows.Count(r => !r.WillApply)
            };
        }

        var pairs = plan.Sum(p => p.Value.Count);

        if (dryRun)
            return new FacultyBulkAssignResult
            {
                Ok = true,
                DryRun = true,
                Rows = rows,
                PairsApplied = 0,
                ProductsApplied = plan.Count,
                ProductsSkipped = rows.Count(r => !r.WillApply)
            };

        if (pairs == 0)
            return new FacultyBulkAssignResult
            {
                Ok = false,
                Error = "Nothing to apply — every selected pair was skipped. See the rows below.",
                Rows = rows,
                ProductsSkipped = rows.Count(r => !r.WillApply)
            };

        // ── Write ──
        foreach (var (pid, targets) in plan)
        foreach (var (fid, existing) in targets)
        {
            var rule = existing;
            if (rule == null)
            {
                rule = new FacultySharingRule { ProductId = pid, FacultyId = fid };
                _db.FacultySharingRules.Add(rule);
            }
            rule.ShareType = req.Type;
            rule.ShareValue = req.Value;
            rule.IsActive = true;
            rule.EffectiveFrom = from;
            rule.EffectiveTo = to;
            rule.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        // EffectiveAmount is a display cache of a derived value; recompute it from the now-saved state
        // in one pass rather than guessing at it inside the write loop above.
        await RefreshEffectiveAmountsAsync(plan.Keys.ToList(), settings, ct);

        await _audit.LogAsync(actorUserId, "Admin", "FacultyShareBulkAssign", "Faculty",
            req.AllFaculty ? "ALL" : string.Join(",", facultyIds),
            $"Faculty={facultyIds.Count}; Products={plan.Count}; Pairs={pairs}; " +
            $"{req.Type}={req.Value}; Skipped={rows.Count(r => !r.WillApply)}");

        return new FacultyBulkAssignResult
        {
            Ok = true,
            PairsApplied = pairs,
            ProductsApplied = plan.Count,
            ProductsSkipped = rows.Count(r => !r.WillApply),
            Rows = rows
        };

        static FacultyBulkAssignResult Fail(string error) => new() { Ok = false, Error = error };
    }

    /// <summary>
    /// Recomputes the denormalised <c>FacultySharingRule.EffectiveAmount</c> for the given products from
    /// their saved state. Nothing reads this column for money decisions — it exists so list screens can
    /// show a per-unit figure without running the calculator per row.
    /// </summary>
    private async Task RefreshEffectiveAmountsAsync(
        List<Guid> productIds, FacultySettings settings, CancellationToken ct)
    {
        if (productIds.Count == 0) return;

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var rules = await _db.FacultySharingRules
            .Where(r => productIds.Contains(r.ProductId)).ToListAsync(ct);
        if (rules.Count == 0) return;

        var attachedByProduct = await LoadAttachmentsAsync(productIds, ct);
        var facultyIds = rules.Select(r => r.FacultyId)
            .Concat(attachedByProduct.Values.SelectMany(s => s)).Distinct().ToList();
        var infoMap = await LoadFacultyInfoAsync(facultyIds, ct);

        var changed = false;
        foreach (var group in rules.GroupBy(r => r.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product)) continue;
            var attached = attachedByProduct.TryGetValue(group.Key, out var att) ? att : new HashSet<Guid>();
            var result = _calc.Calculate(product, group.ToList(), infoMap, attached, settings);
            var byFaculty = result.Lines.ToDictionary(l => l.FacultyId, l => l.ShareAmount);

            foreach (var rule in group)
            {
                var amount = byFaculty.TryGetValue(rule.FacultyId, out var a) ? a : 0m;
                if (rule.EffectiveAmount == amount) continue;
                rule.EffectiveAmount = amount;
                changed = true;
            }
        }

        if (changed) await _db.SaveChangesAsync(ct);
    }

    // ── Assignment management grid ──────────────────────────────────────────────────────────
    public async Task<List<FacultyAssignmentRow>> ListAssignmentsAsync(CancellationToken ct = default)
    {
        var settings = await _calc.GetSettingsAsync(ct);

        var rules = await _db.FacultySharingRules.AsNoTracking()
            .Include(r => r.Product).Include(r => r.Faculty)
            .ToListAsync(ct);
        if (rules.Count == 0) return new();

        var productIds = rules.Select(r => r.ProductId).Distinct().ToList();
        var attachments = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId }).ToListAsync(ct);
        var attachedByProduct = attachments.GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToHashSet());

        var now = DateTime.UtcNow;
        var result = new List<FacultyAssignmentRow>(rules.Count);

        foreach (var group in rules.GroupBy(r => r.ProductId))
        {
            var product = group.First().Product;
            attachedByProduct.TryGetValue(product.Id, out var attached);
            attached ??= new HashSet<Guid>();

            // Attached faculty are included in the map even without a rule, so the product's combined
            // allocation correctly counts anyone earning via the product default.
            var map = new Dictionary<Guid, FacultyGstInfo>();
            foreach (var r in group) map[r.FacultyId] = FacultyShareCalculator.Info(r.Faculty);
            var missing = attached.Where(id => !map.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                foreach (var f in await _db.Faculty.AsNoTracking()
                             .Where(f => missing.Contains(f.Id)).ToListAsync(ct))
                    map[f.Id] = FacultyShareCalculator.Info(f);

            var calc = _calc.Calculate(product, group.ToList(), map, attached, settings, now);
            var lineByFaculty = calc.Lines.ToDictionary(l => l.FacultyId);

            foreach (var rule in group)
            {
                lineByFaculty.TryGetValue(rule.FacultyId, out var line);

                result.Add(new FacultyAssignmentRow
                {
                    RuleId = rule.Id,
                    ProductId = rule.ProductId,
                    FacultyId = rule.FacultyId,
                    ProductTitle = product.Title,
                    Sku = product.Sku,
                    Level = product.Level,
                    FacultyName = rule.Faculty.DisplayName,
                    FacultyShortCode = rule.Faculty.ShortCode,
                    Type = rule.ShareType,
                    AssignedValue = rule.ShareValue,
                    ProductDefaultEnabled = product.EnableDefaultFacultyShare,
                    ProductDefaultType = product.DefaultFacultyShareType,
                    ProductDefaultValue = product.DefaultFacultyShareValue,
                    // "Custom" when the rate differs from the product default, or there is no default.
                    IsCustom = !(product.EnableDefaultFacultyShare
                                 && rule.ShareType == product.DefaultFacultyShareType
                                 && rule.ShareValue == product.DefaultFacultyShareValue),
                    IsActive = rule.IsActive,
                    IsInEffect = rule.AppliesAt(now),
                    EffectiveFrom = rule.EffectiveFrom,
                    EffectiveTo = rule.EffectiveTo,
                    AssignedAt = rule.EffectiveFrom ?? rule.CreatedAt,
                    IsAttachedToProduct = attached.Contains(rule.FacultyId),
                    FacultyIsGstRegistered = rule.Faculty.IsGstRegisteredForShare,
                    ShareAmount = line?.ShareAmount ?? 0m,
                    GstOnShare = line?.GstOnShare ?? 0m,
                    TotalPayout = line?.TotalPayout ?? 0m,
                    SharePctOfBase = line?.SharePctOfBase ?? 0m,
                    ProductTotalSharePct = calc.TotalSharePctOfBase,
                    ProductExceedsCap = calc.ExceedsConfiguredCap,
                    WasCapped = line?.WasCapped ?? false
                });
            }
        }

        return result
            .OrderByDescending(r => r.AssignedAt)
            .ThenBy(r => r.ProductTitle)
            .ThenBy(r => r.FacultyName)
            .ToList();
    }

    public async Task<(bool ok, string? error)> UpdateAssignmentAsync(
        UpdateFacultyAssignmentRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (req.Value <= 0m) return (false, "Share value must be greater than zero.");
        if (req.Type == SharingType.Percentage && req.Value > 100m)
            return (false, "A percentage share cannot exceed 100%.");
        if (req.EffectiveFrom is { } ef && req.EffectiveTo is { } et && et <= ef)
            return (false, "Effective To must be after Effective From.");

        var rule = await _db.FacultySharingRules
            .FirstOrDefaultAsync(r => r.ProductId == req.ProductId && r.FacultyId == req.FacultyId, ct);
        if (rule == null) return (false, "Assignment not found.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == req.ProductId, ct);
        if (product == null) return (false, "Product not found.");

        rule.ShareType = req.Type;
        rule.ShareValue = req.Value;
        rule.EffectiveFrom = Utc(req.EffectiveFrom) ?? rule.EffectiveFrom;
        rule.EffectiveTo = Utc(req.EffectiveTo);
        rule.UpdatedAt = DateTime.UtcNow;

        var (capOk, capError) = await ValidateProductCapAsync(product, rule);
        if (!capOk) { await ReloadAsync(rule); return (false, capError); }

        rule.EffectiveAmount = await ResolveEffectiveAmountAsync(product, rule);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "Admin", "FacultyAssignmentUpdated", "Faculty",
            req.FacultyId.ToString(), $"Product={req.ProductId}; {req.Type}={req.Value}");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> ToggleAssignmentAsync(
        Guid productId, Guid facultyId, bool active, Guid actorUserId, CancellationToken ct = default)
    {
        var rule = await _db.FacultySharingRules
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.FacultyId == facultyId, ct);
        if (rule == null) return (false, "Assignment not found.");
        if (rule.IsActive == active) return (true, null);

        // Re-enabling can breach the cap; disabling never can, so only the former is validated.
        if (active)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);
            if (product == null) return (false, "Product not found.");
            rule.IsActive = true;
            var (capOk, capError) = await ValidateProductCapAsync(product, rule);
            if (!capOk) { await ReloadAsync(rule); return (false, capError); }
        }
        else
        {
            rule.IsActive = false;
        }

        rule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "Admin",
            active ? "FacultyAssignmentEnabled" : "FacultyAssignmentDisabled",
            "Faculty", facultyId.ToString(), $"Product={productId}");
        return (true, null);
    }

    public async Task<(bool ok, string? error)> RemoveAssignmentAsync(
        Guid productId, Guid facultyId, Guid actorUserId, CancellationToken ct = default)
    {
        var rule = await _db.FacultySharingRules
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.FacultyId == facultyId, ct);
        if (rule == null) return (true, null);   // already absent → no-op

        _db.FacultySharingRules.Remove(rule);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "Admin", "FacultyAssignmentRemoved", "Faculty",
            facultyId.ToString(), $"Product={productId}");
        return (true, null);
    }

    /// <summary>Detached copy of a rule, for projecting an outcome without touching the change tracker.</summary>
    private static FacultySharingRule Clone(FacultySharingRule r) => new()
    {
        Id = r.Id,
        ProductId = r.ProductId,
        FacultyId = r.FacultyId,
        ShareType = r.ShareType,
        ShareValue = r.ShareValue,
        IsActive = r.IsActive,
        EffectiveFrom = r.EffectiveFrom,
        EffectiveTo = r.EffectiveTo
    };

    // ── Earned-share ledger ─────────────────────────────────────────────────────────────────
    public async Task RecordOrderFacultyShareAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null || order.Items.Count == 0) return;
        if (await _db.FacultyShareEntries.AnyAsync(e => e.OrderId == orderId, ct)) return;   // idempotent

        var settings = await _calc.GetSettingsAsync(ct);
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var rulesByProduct = (await _db.FacultySharingRules.AsNoTracking()
                .Where(r => productIds.Contains(r.ProductId)).ToListAsync(ct))
            .GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => (IReadOnlyList<FacultySharingRule>)g.ToList());

        var attachments = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId }).ToListAsync(ct);

        var facultyIds = attachments.Select(a => a.FacultyId)
            .Concat(rulesByProduct.Values.SelectMany(r => r).Select(r => r.FacultyId))
            .Distinct().ToList();
        if (facultyIds.Count == 0) return;

        var facultyInfo = (await _db.Faculty.AsNoTracking().Where(f => facultyIds.Contains(f.Id)).ToListAsync(ct))
            .ToDictionary(f => f.Id, FacultyShareCalculator.Info);

        var attachedByProduct = attachments.GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToList());

        var earnedAt = DateTime.UtcNow;

        foreach (var item in order.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product)) continue;

            rulesByProduct.TryGetValue(item.ProductId, out var rules);
            rules ??= Array.Empty<FacultySharingRule>();

            attachedByProduct.TryGetValue(item.ProductId, out var attachedIds);
            var attached = (attachedIds ?? new List<Guid>()).ToHashSet();
            var map = new Dictionary<Guid, FacultyGstInfo>();
            foreach (var fid in attached.Concat(rules.Select(r => r.FacultyId)).Distinct())
                if (facultyInfo.TryGetValue(fid, out var info)) map[fid] = info;

            var calc = _calc.Calculate(product, rules, map, attached, settings, earnedAt);
            if (!calc.HasShare) continue;

            // The base the share was actually computed against, multiplied out to the line quantity.
            var baseAmount = Math.Round(calc.TaxableBase * item.Quantity, 2);

            foreach (var line in calc.Lines)
            {
                // Scale the per-unit split out to the quantity. The calculator already applied Steps
                // 1–3 and the cap, so this must not re-derive the share from the base.
                var split = new FacultyShareMath.ShareSplit(line.ShareAmount, line.GstOnShare, line.TotalPayout)
                    .Times(item.Quantity);
                if (split.TotalPayout <= 0m) continue;

                _db.FacultyShareEntries.Add(new FacultyShareEntry
                {
                    FacultyId = line.FacultyId,
                    OrderId = orderId,
                    OrderNumber = order.OrderNumber,
                    OrderItemId = item.Id,
                    ProductId = item.ProductId,
                    ProductTitle = item.ProductTitle,
                    BaseAmount = baseAmount,
                    ShareType = line.ShareType,
                    ShareValue = line.ShareValue,
                    ShareAmount = split.Share,
                    GstOnShare = split.GstOnShare,
                    TotalPayout = split.TotalPayout,
                    WasGstRegistered = line.FacultyIsGstRegistered,
                    WasCapped = line.WasCapped,
                    EarnedAt = earnedAt
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<FacultyEarningRow>> GetEarningsAsync(
        Guid facultyId, DateTime? fromUtc, DateTime? toUtc, int take = 200, CancellationToken ct = default)
    {
        var q = _db.FacultyShareEntries.AsNoTracking().Where(e => e.FacultyId == facultyId);
        if (fromUtc is { } f) q = q.Where(e => e.EarnedAt >= DateTime.SpecifyKind(f, DateTimeKind.Utc));
        if (toUtc is { } t) q = q.Where(e => e.EarnedAt < DateTime.SpecifyKind(t, DateTimeKind.Utc));

        return await q.OrderByDescending(e => e.EarnedAt).Take(Math.Clamp(take, 1, 5000))
            .Select(e => new FacultyEarningRow
            {
                EarnedAt = e.EarnedAt,
                OrderNumber = e.OrderNumber,
                ProductTitle = e.ProductTitle,
                BaseAmount = e.BaseAmount,
                ShareType = e.ShareType,
                ShareValue = e.ShareValue,
                ShareAmount = e.ShareAmount,
                GstOnShare = e.GstOnShare,
                TotalPayout = e.TotalPayout,
                WasGstRegistered = e.WasGstRegistered,
                WasCapped = e.WasCapped
            })
            .ToListAsync(ct);
    }

    // ── Payout aggregation ──────────────────────────────────────────────────────────────────
    /// <summary>
    /// Per-faculty totals for revenue orders created in [from, to).
    ///
    /// <para>Ledger-first: orders that have <c>FacultyShareEntries</c> are summed from the snapshot, so
    /// editing a rule cannot restate settled history. Orders predating the ledger fall back to
    /// computing from today's rules — without that fallback every historical month would report ₹0,
    /// which is worse than reporting a recomputed figure. Rows sourced from the fallback are the same
    /// shape, so callers need no special case.</para>
    /// </summary>
    public async Task<List<FacultyPayout>> PayoutAsync(DateTime fromUtc, DateTime toUtc)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var agg = new Dictionary<Guid, (string Name, HashSet<Guid> Orders, decimal Share, decimal Gst)>();

        void Add(Guid facultyId, string name, Guid orderId, decimal share, decimal gst)
        {
            if (!agg.TryGetValue(facultyId, out var a)) a = (name, new HashSet<Guid>(), 0m, 0m);
            a.Orders.Add(orderId);
            a.Share += share;
            a.Gst += gst;
            agg[facultyId] = a;
        }

        // ── 1. Ledger rows for orders in range ──
        // Joined to Orders rather than filtered by a subquery so the status test translates to SQL,
        // and so the Order soft-delete query filter applies — a deleted order must stop earning.
        var ledger = await (
                from e in _db.FacultyShareEntries.AsNoTracking()
                join o in _db.Orders.AsNoTracking() on e.OrderId equals o.Id
                where e.EarnedAt >= fromUtc && e.EarnedAt < toUtc && !NonRevenue.Contains(o.Status)
                select new { e.FacultyId, Name = e.Faculty.DisplayName, e.OrderId, e.ShareAmount, e.GstOnShare })
            .ToListAsync();

        var ledgerOrderIds = ledger.Select(l => l.OrderId).ToHashSet();
        foreach (var l in ledger) Add(l.FacultyId, l.Name, l.OrderId, l.ShareAmount, l.GstOnShare);

        // ── 2. Fallback for pre-ledger orders ──
        var legacy = await ComputeFromRulesForPayoutAsync(fromUtc, toUtc, ledgerOrderIds);
        foreach (var l in legacy) Add(l.FacultyId, l.Name, l.OrderId, l.Share, l.Gst);

        return agg.Select(kv => new FacultyPayout(
                kv.Key, kv.Value.Name, kv.Value.Orders.Count,
                Math.Round(kv.Value.Share, 2), Math.Round(kv.Value.Gst, 2),
                Math.Round(kv.Value.Share + kv.Value.Gst, 2)))
            .OrderByDescending(p => p.TotalPayout).ToList();
    }

    /// <summary>
    /// Computes faculty shares for revenue order items whose order has no ledger row, using the
    /// CURRENT rules and the GST-correct specification. Shared by <see cref="PayoutAsync"/> and
    /// <c>PayoutCalculationService</c> so the two never disagree about a historical month.
    /// </summary>
    public async Task<List<FacultyShareRecomputedLine>> ComputeFromRulesForPayoutAsync(
        DateTime fromUtc, DateTime toUtc, HashSet<Guid> skipOrderIds, CancellationToken ct = default)
    {
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        var result = new List<FacultyShareRecomputedLine>();

        var items = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order.CreatedAt >= fromUtc && i.Order.CreatedAt < toUtc
                        && !NonRevenue.Contains(i.Order.Status))
            .Select(i => new { i.Id, i.ProductId, i.Quantity, i.OrderId, OrderNumber = i.Order.OrderNumber })
            .ToListAsync(ct);

        items = items.Where(i => !skipOrderIds.Contains(i.OrderId)).ToList();
        if (items.Count == 0) return result;

        var settings = await _calc.GetSettingsAsync(ct);
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var rulesByProduct = (await _db.FacultySharingRules.AsNoTracking()
                .Where(r => productIds.Contains(r.ProductId)).ToListAsync())
            .GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => (IReadOnlyList<FacultySharingRule>)g.ToList());
        if (rulesByProduct.Count == 0) return result;

        var attachments = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId }).ToListAsync();

        var facultyIds = attachments.Select(a => a.FacultyId)
            .Concat(rulesByProduct.Values.SelectMany(r => r).Select(r => r.FacultyId)).Distinct().ToList();
        var facultyInfo = (await _db.Faculty.AsNoTracking().Where(f => facultyIds.Contains(f.Id)).ToListAsync())
            .ToDictionary(f => f.Id, FacultyShareCalculator.Info);
        var attachedByProduct = attachments.GroupBy(a => a.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToList());

        // Cache per product — the same product recurs across many order items and the calculation is
        // quantity-independent, so recomputing it per line would be pure waste.
        var cache = new Dictionary<Guid, ProductFacultyShareResult>();

        foreach (var item in items)
        {
            if (!cache.TryGetValue(item.ProductId, out var calc))
            {
                if (!products.TryGetValue(item.ProductId, out var product)) continue;
                rulesByProduct.TryGetValue(item.ProductId, out var rules);
                rules ??= Array.Empty<FacultySharingRule>();
                attachedByProduct.TryGetValue(item.ProductId, out var attachedList);
                var attached = (attachedList ?? new List<Guid>()).ToHashSet();

                var map = new Dictionary<Guid, FacultyGstInfo>();
                foreach (var fid in attached.Concat(rules.Select(r => r.FacultyId)).Distinct())
                    if (facultyInfo.TryGetValue(fid, out var info)) map[fid] = info;

                calc = _calc.Calculate(product, rules, map, attached, settings);
                cache[item.ProductId] = calc;
            }

            foreach (var line in calc.Lines)
            {
                var split = new FacultyShareMath.ShareSplit(line.ShareAmount, line.GstOnShare, line.TotalPayout)
                    .Times(item.Quantity);
                if (split.TotalPayout <= 0m) continue;
                result.Add(new FacultyShareRecomputedLine(
                    line.FacultyId, line.FacultyName, item.OrderId, item.OrderNumber, item.Id,
                    split.Share, split.GstOnShare));
            }
        }

        return result;
    }

    // ── Settings ────────────────────────────────────────────────────────────────────────────
    public Task<FacultySettings> GetSettingsAsync(CancellationToken ct = default) => _calc.GetSettingsAsync(ct);

    public async Task SaveSettingsAsync(FacultySettings settings, Guid actorUserId, CancellationToken ct = default)
    {
        var s = await _calc.GetSettingsAsync(ct);
        s.ApplyShareOnSpecialPrice = settings.ApplyShareOnSpecialPrice;
        s.AllowFacultyRuleOverride = settings.AllowFacultyRuleOverride;
        s.MaxTotalSharePct = Math.Clamp(settings.MaxTotalSharePct, 0m, 100m);
        s.EnforceMaxTotalShare = settings.EnforceMaxTotalShare;
        s.AutoCreateRuleOnAssign = settings.AutoCreateRuleOnAssign;
        s.TdsOnShareExcludingGst = settings.TdsOnShareExcludingGst;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(actorUserId, "Admin", "FacultySettingsSaved", "FacultySettings", s.Id.ToString(),
            $"SpecialPrice={s.ApplyShareOnSpecialPrice}; RuleOverride={s.AllowFacultyRuleOverride}; " +
            $"MaxTotal={s.MaxTotalSharePct}; Enforce={s.EnforceMaxTotalShare}; " +
            $"AutoRule={s.AutoCreateRuleOnAssign}; TdsExGst={s.TdsOnShareExcludingGst}");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static DateTime? Utc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>
    /// Whether this faculty is on this product's faculty list. Gates the CREATION of a share — see
    /// <see cref="SaveRuleAsync"/> for why existing rules are exempt.
    /// </summary>
    private Task<bool> IsAttachedAsync(Guid productId, Guid facultyId, CancellationToken ct = default) =>
        _db.ProductFaculty.AsNoTracking()
            .AnyAsync(pf => pf.ProductId == productId && pf.FacultyId == facultyId, ct);

    /// <summary>
    /// productId → the faculty attached to it via <c>ProductFaculty</c>. This set is what the product
    /// default is allowed to reach; a rule-holder who is no longer attached must not be in it, or
    /// disabling their rule would start paying them the default instead of nothing.
    /// </summary>
    private async Task<Dictionary<Guid, HashSet<Guid>>> LoadAttachmentsAsync(
        List<Guid> productIds, CancellationToken ct = default)
    {
        if (productIds.Count == 0) return new();
        var rows = await _db.ProductFaculty.AsNoTracking()
            .Where(pf => productIds.Contains(pf.ProductId))
            .Select(pf => new { pf.ProductId, pf.FacultyId })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.FacultyId).ToHashSet());
    }

    private async Task<Dictionary<Guid, FacultyGstInfo>> LoadFacultyInfoAsync(
        List<Guid> facultyIds, CancellationToken ct = default)
    {
        if (facultyIds.Count == 0) return new();
        return (await _db.Faculty.AsNoTracking().Where(f => facultyIds.Contains(f.Id)).ToListAsync(ct))
            .ToDictionary(f => f.Id, FacultyShareCalculator.Info);
    }

    /// <summary>
    /// Checks the product's combined allocation with <paramref name="pending"/> applied. Loads the
    /// product's other rules fresh so a single-rule save is validated against reality, not against
    /// whatever the caller happened to send.
    /// </summary>
    private async Task<(bool ok, string? error)> ValidateProductCapAsync(Product product, FacultySharingRule pending)
    {
        var settings = await _calc.GetSettingsAsync();
        if (!settings.EnforceMaxTotalShare) return (true, null);

        // Excluded by BOTH id and faculty: by id because `pending` is the tracked copy of that row and
        // must not be counted twice, by faculty because `pending` replaces whatever that faculty had.
        var others = await _db.FacultySharingRules.AsNoTracking()
            .Where(r => r.ProductId == product.Id && r.Id != pending.Id && r.FacultyId != pending.FacultyId)
            .ToListAsync();

        var all = others.Append(pending).ToList();
        var attached = (await LoadAttachmentsAsync(new List<Guid> { product.Id }))
            .TryGetValue(product.Id, out var att) ? att : new HashSet<Guid>();
        var facultyIds = all.Select(r => r.FacultyId).Concat(attached).Distinct().ToList();
        var infoMap = await LoadFacultyInfoAsync(facultyIds);

        var preview = _calc.Calculate(product, all, infoMap, attached, settings);
        if (!preview.ExceedsConfiguredCap) return (true, null);

        return (false, $"This would take the product's total faculty share to " +
                       $"{preview.TotalSharePctOfBase:0.##}% of the taxable base, over the " +
                       $"{settings.MaxTotalSharePct:0.##}% limit.");
    }

    private async Task<decimal> ResolveEffectiveAmountAsync(Product product, FacultySharingRule rule)
    {
        var settings = await _calc.GetSettingsAsync();
        var faculty = await _db.Faculty.AsNoTracking().FirstOrDefaultAsync(f => f.Id == rule.FacultyId);
        if (faculty == null) return 0m;

        var map = new Dictionary<Guid, FacultyGstInfo>
        {
            [faculty.Id] = FacultyShareCalculator.Info(faculty)
        };
        // Only this faculty's own rule is in scope, so the attached set is narrowed to them too: it
        // decides whether they'd fall back to the product default if their rule isn't in force.
        var attached = (await LoadAttachmentsAsync(new List<Guid> { product.Id }))
            .TryGetValue(product.Id, out var att) ? att : new HashSet<Guid>();
        var scoped = attached.Contains(rule.FacultyId)
            ? new HashSet<Guid> { rule.FacultyId }
            : new HashSet<Guid>();

        var result = _calc.Calculate(product, new[] { rule }, map, scoped, settings);
        return result.Lines.FirstOrDefault(l => l.FacultyId == rule.FacultyId)?.ShareAmount ?? 0m;
    }

    private async Task ReloadAsync(FacultySharingRule rule)
    {
        await _db.Entry(rule).ReloadAsync();
    }

    /// <summary>
    /// Builds detached <see cref="FacultySharingRule"/> instances reflecting the SUBMITTED form, so
    /// the combined-allocation cap can be checked before anything is written. Nothing here is tracked.
    /// </summary>
    private static List<FacultySharingRule> ProjectSubmittedRules(
        Guid productId, ProductFacultyShareEditModel model) =>
        model.Rows.Where(r => r.HasExplicitRule).Select(r => new FacultySharingRule
        {
            Id = r.RuleId ?? Guid.NewGuid(),
            ProductId = productId,
            FacultyId = r.FacultyId,
            ShareType = r.ShareType,
            ShareValue = r.ShareValue,
            IsActive = r.IsActive,
            EffectiveFrom = Utc(r.EffectiveFrom),
            EffectiveTo = Utc(r.EffectiveTo)
        }).ToList();
}
