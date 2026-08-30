using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using RioCommerce.Core.DTOs.Migration;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RioCommerce.Infrastructure.Services;

public class LegacyMigrationService : ILegacyMigrationService
{
    private readonly RioCommerceDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<LegacyMigrationService> _log;

    public LegacyMigrationService(RioCommerceDbContext db, IAuditService audit, ILogger<LegacyMigrationService> log)
    {
        _db = db; _audit = audit; _log = log;
    }

    private const int MaxMessages = 25;   // cap the sample messages we surface to the UI
    private const int SaveBatchSize = 200;   // bound the blast radius of a failed SaveChanges

    // SqlCommand defaults to 30 seconds, which is a read budget sized for a web request, not for a
    // full-table sweep of a multi-hundred-megabyte legacy database over a remote link. Five minutes
    // per read, and the admin still holds a CancellationToken if a run has to be abandoned.
    private const int ReadTimeoutSeconds = 300;

    // Matches the lookup in both registration paths — lower-case, singular. Roles."Name" is plain
    // text, not citext, so the spelling has to be exact.
    private const string StudentRoleName = "student";

    public async Task<ImportRunResult> RunAsync(ImportRequest req, Guid? actorId, string? actorName, CancellationToken ct = default)
    {
        var result = new ImportRunResult { DryRun = req.DryRun, StartedAt = DateTime.UtcNow };

        if (string.IsNullOrWhiteSpace(req.SourceConnectionString))
        {
            result.Ok = false; result.Error = "Source (SQL Server) connection string is required.";
            return result;
        }

        // Parse before connecting. ADO.NET's own rejection is "Format of the initialization string
        // does not conform to specification starting at index N" — an index into a value the admin
        // cannot see (the field is masked) and no indication of what to change. Nearly every real
        // cause is a password holding a character the parser treats as syntax, so say so.
        SqlConnectionStringBuilder csb;
        try
        {
            csb = new SqlConnectionStringBuilder(req.SourceConnectionString.Trim());
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Error =
                $"The connection string could not be parsed ({ex.Message}) " +
                "Expected: Server=host,1433;Database=riocommerce_com;User Id=sa;Password=...;TrustServerCertificate=True — " +
                "and if the password contains ; = or a quote it must be wrapped, e.g. Password='p@ss;word'. " +
                "Tick “Show” next to the field to check the pasted value for stray quotes or line breaks.";
            return result;
        }

        if (string.IsNullOrWhiteSpace(csb.InitialCatalog))
        {
            result.Ok = false;
            result.Error = "The connection string names no database. Add Database=riocommerce_com (or Initial Catalog=...).";
            return result;
        }

        try
        {
            await using var sql = new SqlConnection(csb.ConnectionString);
            await sql.OpenAsync(ct);

            if (req.ImportProducts)
                await ImportProductsAsync(sql, req.DryRun, result.Products, ct);

            if (req.ImportCustomers)
                await ImportCustomersAsync(sql, req.DryRun, result.Users, ct);

            if (req.ImportProductImages)
                await ImportProductImagesAsync(sql, req, result.Images, ct);

            result.Ok = true;
        }
        catch (Exception ex)
        {
            // Surface the innermost message — SqlException details are the useful part.
            var root = ex; while (root.InnerException != null) root = root.InnerException;
            result.Ok = false; result.Error = root.Message + ConnectionHint(root);
            _log.LogError(ex, "Legacy migration failed");
        }

        result.FinishedAt = DateTime.UtcNow;

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId, ActorName = actorName ?? "system",
            Module = "Migration", Action = req.DryRun ? "LegacyImportDryRun" : "LegacyImport",
            EntityType = "System", Status = result.Ok ? "Success" : "Error",
            Details = result.Ok
                ? $"Products: +{result.Products.Inserted}/~{result.Products.Updated}/skip {result.Products.Skipped}; " +
                  $"Users: +{result.Users.Inserted}/~{result.Users.Updated}/skip {result.Users.Skipped}; " +
                  $"Images: +{result.Images.Inserted}/skip {result.Images.Skipped}" + (req.DryRun ? " (dry run)" : "")
                : result.Error,
        });

        return result;
    }

    // ── Products ────────────────────────────────────────────────────────────────
    private async Task ImportProductsAsync(SqlConnection sql, bool dryRun, ImportEntityResult r, CancellationToken ct)
    {
        const string query = @"
SELECT Id, Name, Sku, ShortDescription, FullDescription, Price, OldPrice, ProductCost,
       Gtin, Published
FROM dbo.Product
WHERE Deleted = 0
  AND Published = 1;";

        var rows = new List<(int Id, string Name, string? Sku, string? ShortDesc, string? FullDesc,
            decimal Price, decimal OldPrice, decimal Cost, string? Gtin, bool Published)>();

        await using (var cmd = new SqlCommand(query, sql) { CommandTimeout = ReadTimeoutSeconds })
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                rows.Add((
                    rd.GetInt32(0),
                    rd.IsDBNull(1) ? "" : rd.GetString(1),
                    rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.IsDBNull(5) ? 0m : rd.GetDecimal(5),
                    rd.IsDBNull(6) ? 0m : rd.GetDecimal(6),
                    rd.IsDBNull(7) ? 0m : rd.GetDecimal(7),
                    rd.IsDBNull(8) ? null : rd.GetString(8),
                    !rd.IsDBNull(9) && rd.GetBoolean(9)));
            }
        }
        r.Read = rows.Count;

        // Pre-load maps + existing SKUs for matching (avoid per-row round-trips).
        var maps = await _db.LegacyProductMaps.ToDictionaryAsync(m => m.LegacyId, ct);
        var existingBySku = await _db.Products
            .Where(p => p.Sku != null && p.Sku != "")
            .ToDictionaryAsync(p => p.Sku!.ToLower(), p => p, ct);
        var usedSlugs = new HashSet<string>(await _db.Products.Select(p => p.Slug).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);

        foreach (var s in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(s.Name)) { r.Skipped++; AddMsg(r, $"#{s.Id}: blank name — skipped"); continue; }

            Product? existing = null;
            if (maps.TryGetValue(s.Id, out var map))
                existing = await _db.Products.FirstOrDefaultAsync(p => p.Id == map.NewId, ct);
            if (existing == null && !string.IsNullOrWhiteSpace(s.Sku))
                existingBySku.TryGetValue(s.Sku!.ToLower(), out existing);

            if (existing != null)
            {
                if (!dryRun)
                {
                    existing.Title = s.Name.Trim();
                    existing.ShortDesc = s.ShortDesc;
                    existing.FullDesc = s.FullDesc;
                    existing.SellingPrice = s.Price;
                    existing.Mrp = s.OldPrice > 0 ? s.OldPrice : s.Price;
                    existing.ProductCost = s.Cost;
                    existing.Gtin = s.Gtin;
                    existing.Status = s.Published ? ProductStatus.Active : ProductStatus.Draft;
                    if (string.IsNullOrWhiteSpace(existing.Sku)) existing.Sku = s.Sku;
                    UpsertProductMap(maps, s.Id, existing.Id, s.Sku);
                }
                r.Updated++;
                continue;
            }

            // New product
            if (!dryRun)
            {
                var slug = UniqueSlug(Slugify(s.Name), usedSlugs);
                var p = new Product
                {
                    // Assigned here, NOT left to the database. Every BaseEntity.Id is configured
                    // HasDefaultValueSql("gen_random_uuid()"), so EF treats it as store-generated and
                    // leaves the CLR property at Guid.Empty until SaveChanges. UpsertProductMap below
                    // reads p.Id, so without this the map records an all-zero NewId — which is exactly
                    // what happened to all 103 rows migration 0034 repairs.
                    Id = Guid.NewGuid(),
                    Title = s.Name.Trim(),
                    Slug = slug,
                    Sku = s.Sku,
                    ShortDesc = s.ShortDesc,
                    FullDesc = s.FullDesc,
                    SellingPrice = s.Price,
                    Mrp = s.OldPrice > 0 ? s.OldPrice : s.Price,
                    ProductCost = s.Cost,
                    Gtin = s.Gtin,
                    GstRate = 18.00m,
                    GstInclusive = true,
                    CourseType = CourseType.Regular,
                    Status = s.Published ? ProductStatus.Active : ProductStatus.Draft,
                    PublishedAt = s.Published ? DateTime.UtcNow : null,
                };
                _db.Products.Add(p);
                if (!string.IsNullOrWhiteSpace(s.Sku)) existingBySku[s.Sku!.ToLower()] = p;
                UpsertProductMap(maps, s.Id, p.Id, s.Sku);
            }
            r.Inserted++;
        }

        if (!dryRun) await _db.SaveChangesAsync(ct);
    }

    // ── Product images (point-in-place) ───────────────────────────────────────────
    // For every product we've already mapped (LegacyProductMap), read its old picture
    // mappings, verify each file exists in the source folder, and attach a ProductImage
    // whose ImageUrl points at the served path (e.g. /images/thumbs/{file}). The files
    // themselves are NOT copied here — they must already be served by the new app (you copy
    // the old wwwroot/images/thumbs into the new app's wwwroot/images/thumbs). Idempotent:
    // a product that already has images is skipped, so re-runs don't duplicate.
    private async Task ImportProductImagesAsync(SqlConnection sql, ImportRequest req, ImportEntityResult r, CancellationToken ct)
    {
        var urlPrefix = string.IsNullOrWhiteSpace(req.ImageUrlPrefix) ? "/images/thumbs/" : req.ImageUrlPrefix.Trim();
        if (!urlPrefix.StartsWith('/')) urlPrefix = "/" + urlPrefix;
        if (!urlPrefix.EndsWith('/')) urlPrefix += "/";

        // Optional on-disk verification. If a folder is given, only images whose file actually
        // exists there are attached (missing ones are logged + skipped). If left blank, we trust
        // the files are served at the URL prefix and attach every picture row.
        var folder = req.ImageSourceFolder?.Trim();
        var checkFiles = !string.IsNullOrWhiteSpace(folder);
        if (checkFiles && !Directory.Exists(folder))
        {
            r.Errors++;
            AddMsg(r, $"Image source folder not found: '{folder}'. " +
                      "Leave it blank to skip the on-disk check, or give a folder this server can read.");
            return;
        }

        // Match OLD products to NEW products by SKU (case-insensitive), skipping the legacy id map.
        // Build: SKU -> new Product Guid.
        var newBySku = await _db.Products
            .Where(p => p.Sku != null && p.Sku != "")
            .ToDictionaryAsync(p => p.Sku!.ToLower(), p => p.Id, ct);
        if (newBySku.Count == 0) { AddMsg(r, "No products with SKUs found in the new database."); return; }

        // Products that already have images (skip — idempotent).
        var alreadyHaveImages = (await _db.ProductImages
                .Select(pi => pi.ProductId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        // One pass over the old DB: every product picture joined to its product's SKU.
        const string q = @"
SELECT pr.Sku, p.Id AS PictureId, p.MimeType, p.SeoFilename, ppm.DisplayOrder
FROM dbo.Product_Picture_Mapping ppm
JOIN dbo.Picture p  ON p.Id = ppm.PictureId
JOIN dbo.Product pr ON pr.Id = ppm.ProductId
WHERE pr.Deleted = 0
  AND pr.Sku IS NOT NULL AND LTRIM(RTRIM(pr.Sku)) <> ''
ORDER BY pr.Sku, ppm.DisplayOrder, p.Id;";

        // Group the old picture rows by SKU.
        var bySku = new Dictionary<string, List<(int PictureId, string Mime, string? Seo, int DisplayOrder)>>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(q, sql) { CommandTimeout = ReadTimeoutSeconds })
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                var sku = rd.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(sku)) continue;
                if (!bySku.TryGetValue(sku, out var list)) { list = new(); bySku[sku] = list; }
                list.Add((
                    rd.GetInt32(1),
                    rd.IsDBNull(2) ? "image/jpeg" : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? 0 : rd.GetInt32(4)));
            }
        }

        foreach (var (sku, pics) in bySku)
        {
            ct.ThrowIfCancellationRequested();
            r.Read++;

            // Find the matching NEW product by SKU.
            if (!newBySku.TryGetValue(sku.ToLower(), out var newProductId))
            {
                r.Skipped++;
                AddMsg(r, $"No new product with SKU '{sku}' — {pics.Count} image(s) skipped.");
                continue;
            }

            if (alreadyHaveImages.Contains(newProductId)) { r.Skipped++; continue; }

            var order = 0;
            var addedForThisProduct = false;
            foreach (var pic in pics.OrderBy(p => p.DisplayOrder).ThenBy(p => p.PictureId))
            {
                var fileName = NopPictureFileName(pic.PictureId, pic.Seo, pic.Mime);

                if (checkFiles && !File.Exists(Path.Combine(folder!, fileName)))
                {
                    r.Skipped++;
                    AddMsg(r, $"Missing file for SKU '{sku}': {fileName}");
                    continue;
                }

                if (!req.DryRun)
                {
                    _db.ProductImages.Add(new ProductImage
                    {
                        ProductId    = newProductId,
                        ImageUrl     = urlPrefix + fileName,
                        AltText      = pic.Seo,
                        DisplayOrder = order,
                        IsPrimary    = order == 0,   // first (lowest DisplayOrder) becomes primary
                    });
                }
                order++;
                addedForThisProduct = true;
                r.Inserted++;
            }

            if (addedForThisProduct) alreadyHaveImages.Add(newProductId);
        }

        if (!req.DryRun) await _db.SaveChangesAsync(ct);
    }

    // nopCommerce stores picture files as {PictureId:0000000}_{SeoFilename}.{ext}
    // e.g. PictureId 4187, seo "ca-inter-group-1-test-series", image/jpeg
    //   -> 0004187_ca-inter-group-1-test-series.jpeg
    private static string NopPictureFileName(int pictureId, string? seo, string mime)
    {
        var ext = mime?.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpeg",
            "image/jpg"  => "jpeg",
            "image/png"  => "png",
            "image/gif"  => "gif",
            "image/webp" => "webp",
            "image/bmp"  => "bmp",
            _            => "jpeg",
        };
        var id = pictureId.ToString("0000000");   // 7-digit zero-padded, matches your example 0004187
        return string.IsNullOrWhiteSpace(seo) ? $"{id}.{ext}" : $"{id}_{seo}.{ext}";
    }

    private void UpsertProductMap(Dictionary<int, LegacyProductMap> maps, int legacyId, Guid newId, string? sku)
    {
        if (maps.TryGetValue(legacyId, out var m))
        {
            m.NewId = newId; m.LegacyKey = sku; m.ImportedAt = DateTime.UtcNow;
        }
        else
        {
            var nm = new LegacyProductMap { LegacyId = legacyId, NewId = newId, LegacyKey = sku };
            _db.LegacyProductMaps.Add(nm);
            maps[legacyId] = nm;
        }
    }

    // ── Customers → Users ────────────────────────────────────────────────────────
    private async Task ImportCustomersAsync(SqlConnection sql, bool dryRun, ImportEntityResult r, CancellationToken ct)
    {
        // Pull the customer with name from GenericAttribute (FirstName/LastName/Phone) and
        // city/state/phone from the billing Address (+ StateProvince name). nopCommerce 4.5 schema.
        //
        // The GenericAttribute lookups are scalar subqueries rather than LEFT JOINs on purpose.
        // GenericAttribute is keyed (EntityId, KeyGroup, Key, StoreId) with only a NON-unique index
        // on (EntityId, KeyGroup) — a customer whose FirstName was written under two StoreIds joins
        // out into two rows, and the whole customer then reads as a "duplicate email in source" and
        // gets skipped with a misleading message. TOP 1 + newest-Id collapses that to one value per
        // customer. Address/StateProvince join on their primary keys, so those cannot fan out.
        const string query = @"
SELECT  c.Id,
        c.Email,
        (SELECT TOP 1 ga.Value FROM dbo.GenericAttribute ga
          WHERE ga.EntityId = c.Id AND ga.KeyGroup = 'Customer' AND ga.[Key] = 'FirstName'
          ORDER BY ga.Id DESC)  AS FirstName,
        (SELECT TOP 1 ga.Value FROM dbo.GenericAttribute ga
          WHERE ga.EntityId = c.Id AND ga.KeyGroup = 'Customer' AND ga.[Key] = 'LastName'
          ORDER BY ga.Id DESC)  AS LastName,
        (SELECT TOP 1 ga.Value FROM dbo.GenericAttribute ga
          WHERE ga.EntityId = c.Id AND ga.KeyGroup = 'Customer' AND ga.[Key] = 'Phone'
          ORDER BY ga.Id DESC)  AS GaPhone,
        a.PhoneNumber   AS AddrPhone,
        a.City          AS City,
        sp.Name         AS StateName,
        c.Active,
        c.AdminComment
FROM dbo.Customer c
LEFT JOIN dbo.Address a
       ON a.Id = c.BillingAddress_Id
LEFT JOIN dbo.StateProvince sp
       ON sp.Id = a.StateProvinceId
WHERE c.Deleted = 0
  AND c.IsSystemAccount = 0
  AND c.Email IS NOT NULL
  AND LTRIM(RTRIM(c.Email)) <> '';";

        var rows = new List<(int Id, string Email, string? First, string? Last, string? GaPhone,
            string? AddrPhone, string? City, string? State, bool Active, string? AdminComment)>();

        await using (var cmd = new SqlCommand(query, sql) { CommandTimeout = ReadTimeoutSeconds })
        await using (var rd = await cmd.ExecuteReaderAsync(ct))
        {
            while (await rd.ReadAsync(ct))
            {
                rows.Add((
                    rd.GetInt32(0),
                    rd.GetString(1).Trim(),
                    rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? null : rd.GetString(4),
                    rd.IsDBNull(5) ? null : rd.GetString(5),
                    rd.IsDBNull(6) ? null : rd.GetString(6),
                    rd.IsDBNull(7) ? null : rd.GetString(7),
                    !rd.IsDBNull(8) && rd.GetBoolean(8),
                    rd.IsDBNull(9) ? null : rd.GetString(9)));
            }
        }
        r.Read = rows.Count;

        var maps = await _db.LegacyUserMaps.ToDictionaryAsync(m => m.LegacyId, ct);
        var existingByEmail = await _db.Users
            .Where(u => u.Email != null && u.Email != "")
            .ToDictionaryAsync(u => u.Email!.ToLower(), u => u, ct);

        // Users.Phone carries a FILTERED UNIQUE index (IX_users_Phone), so a number already held by
        // another account cannot be written again. Legacy data collides on this routinely — shared
        // family numbers within the source, and customers who re-registered on the new site under a
        // different email. Every phone already in use is loaded up front so a collision is detected
        // before EF is asked to insert it: one shared number must not cost the whole import.
        var takenPhones = new HashSet<string>(
            await _db.Users.Where(u => u.Phone != null && u.Phone != "")
                           .Select(u => u.Phone!)
                           .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);
        var phonesDropped = 0;

        // Every customer created through the site is given the "student" role — both the browser
        // register endpoint and AuthController.Register do it — so an imported customer without one
        // is inconsistent with the identical customer who signed up: the header greets them as
        // "Member" instead of "Student", and they drop out of any role-filtered admin view.
        var studentRole = await _db.Roles.FirstOrDefaultAsync(r2 => r2.Name == StudentRoleName, ct);

        // Any existing assignment counts, active or not. CustomerAdminService revokes a role by
        // flipping IsActive rather than deleting the row, so treating an inactive one as "missing"
        // would silently hand back a role an admin had deliberately taken away. This set is also the
        // only duplicate guard there is — UserRoles carries no unique index on (UserId, RoleId), so
        // nothing at the database level would stop a re-run stacking a second row per customer.
        var alreadyStudent = studentRole is null
            ? new HashSet<Guid>()
            : new HashSet<Guid>(await _db.UserRoles
                .Where(ur => ur.RoleId == studentRole.Id)
                .Select(ur => ur.UserId)
                .ToListAsync(ct));
        var rolesAssigned = 0;

        // Accounts already holding a staff role are not plain customers. Identity for this import is
        // the email address, so an old customer whose address happens to equal a staff login — or a
        // staff member who also shopped on the old site — resolves to that staff account, and a
        // blanket assignment would hand an administrator a customer role. Skipped and reported
        // rather than guessed at, because the match itself is the thing worth a human look.
        var staffRoleHolders = new HashSet<Guid>(
            await _db.UserRoles
                .Where(ur => ur.IsActive && ur.Role.Name != StudentRoleName)
                .Select(ur => ur.UserId)
                .ToListAsync(ct));
        var staffMatches = 0;

        // De-dupe within the source by email (keep first), so two old rows can't both insert.
        var seenEmail = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = 0;

        foreach (var s in rows)
        {
            ct.ThrowIfCancellationRequested();
            var email = s.Email.Trim();
            if (string.IsNullOrWhiteSpace(email)) { r.Skipped++; continue; }
            if (!seenEmail.Add(email.ToLower())) { r.Skipped++; AddMsg(r, $"#{s.Id}: duplicate email in source ({email}) — skipped"); continue; }

            var fullName = string.Join(" ", new[] { s.First, s.Last }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            if (string.IsNullOrWhiteSpace(fullName)) fullName = email.Split('@')[0];
            // users.FullName is varchar(200) but the source names come from GenericAttribute.Value,
            // which is nvarchar(max). One customer who pasted an essay into the name field would
            // otherwise abort the batch on a length violation — same guard as NormalizePhone's.
            if (fullName.Length > 200) fullName = fullName[..200];
            var phone = NormalizePhone(s.GaPhone) ?? NormalizePhone(s.AddrPhone);

            User? existing = null;
            if (maps.TryGetValue(s.Id, out var map))
                existing = await _db.Users.FirstOrDefaultAsync(u => u.Id == map.NewId, ct);
            if (existing == null)
                existingByEmail.TryGetValue(email.ToLower(), out existing);

            // Drop a colliding phone rather than failing the row. Email is the identity for this
            // import, and an account that arrives without a phone is recoverable — the customer can
            // add it themselves later. Losing the run to one duplicate number is not recoverable.
            //
            // Resolved AFTER the account lookup so a customer's OWN number never counts against
            // them: takenPhones is read from users at the start of the run, so on a re-run every
            // already-imported customer would otherwise match themselves and be reported as a
            // collision — inflating the count and burying the real clashes in false positives.
            string? phoneDropNote = null;
            if (phone != null
                && !string.Equals(existing?.Phone, phone, StringComparison.OrdinalIgnoreCase)
                && takenPhones.Contains(phone))
            {
                phonesDropped++;
                AddMsg(r, $"#{s.Id}: phone {phone} already in use — {email} imported without it");
                // The run's Notes are capped and thrown away when the page closes, which left the
                // dropped number recorded nowhere. Putting it on the account makes it triageable
                // later — it is the evidence needed to tell a shared family number apart from the
                // same person holding two accounts.
                phoneDropNote = $"[Migration] Phone {phone} was NOT imported — another account already holds it.";
                phone = null;
            }

            var adminComment = BuildAdminComment(s.AdminComment, s.Active, phoneDropNote);

            if (existing != null)
            {
                // Update only empty fields — never clobber data the user may have set on the new site,
                // and never touch PasswordHash.
                if (!dryRun)
                {
                    if (string.IsNullOrWhiteSpace(existing.FullName)) existing.FullName = fullName;
                    existing.FirstName ??= s.First;
                    existing.LastName ??= s.Last;
                    // Claim the number as we take it, so a later source row carrying the same phone
                    // sees it as taken instead of racing this one into the unique index.
                    if (existing.Phone is null && phone != null)
                    {
                        existing.Phone = phone;
                        takenPhones.Add(phone);
                    }
                    existing.City ??= s.City;
                    existing.State ??= s.State;
                    existing.AdminComment = AppendMissingNotes(existing.AdminComment, adminComment);
                    UpsertUserMap(maps, s.Id, existing.Id, email);
                }

                // Counted outside the dry-run guard so the preview reports what a real run would do.
                if (studentRole != null && !alreadyStudent.Contains(existing.Id))
                {
                    if (staffRoleHolders.Contains(existing.Id))
                    {
                        staffMatches++;
                        AddMsg(r, $"#{s.Id}: {email} matched an existing STAFF account — student role NOT added; "
                                + "verify this is the same person");
                    }
                    else
                    {
                        // Add() both tests and records, so a second source row for the same account
                        // cannot queue a duplicate — there is no unique index to fall back on.
                        alreadyStudent.Add(existing.Id);
                        rolesAssigned++;
                        if (!dryRun)
                            _db.UserRoles.Add(new UserRole { UserId = existing.Id, RoleId = studentRole.Id, IsActive = true });
                    }
                }

                r.Updated++;
                continue;
            }

            if (!dryRun)
            {
                var u = new User
                {
                    // Assigned here for the same reason as Product above: Id is store-generated, so it
                    // stays Guid.Empty until SaveChanges and UpsertUserMap would record an all-zero
                    // NewId — breaking the map that a later order migration has to join through.
                    Id = Guid.NewGuid(),
                    Email = email,
                    FullName = fullName,
                    FirstName = s.First,
                    LastName = s.Last,
                    Phone = phone,
                    City = s.City,
                    State = s.State,
                    AdminComment = adminComment,
                    PasswordHash = null,        // password not migrated — user sets it via email OTP
                    IsActive = s.Active,
                    IsVerified = false,
                };
                _db.Users.Add(u);
                existingByEmail[email.ToLower()] = u;
                if (phone != null) takenPhones.Add(phone);
                UpsertUserMap(maps, s.Id, u.Id, email);

                if (studentRole != null && alreadyStudent.Add(u.Id))
                    _db.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = studentRole.Id, IsActive = true });
            }
            // A brand-new account always takes the role, so this is countable in a dry run too —
            // there is no user Id to record yet, which is why it sits outside the block above.
            if (studentRole != null) rolesAssigned++;
            r.Inserted++;

            // Save in batches. A single SaveChanges for the whole run meant any unforeseen constraint
            // violation discarded every row; this bounds the loss to one batch, and the map + email
            // fallback make a re-run resume cleanly from where it stopped.
            if (!dryRun && ++pending >= SaveBatchSize)
            {
                await _db.SaveChangesAsync(ct);
                pending = 0;
            }
        }

        if (phonesDropped > 0)
            AddSummary(r, $"{phonesDropped} customer(s) imported without a phone because the number was already in use "
                        + $"(the lines below are a sample, capped at {MaxMessages}). Each one carries the dropped "
                        + "number in its admin comment.");

        // The signup endpoints skip the role in silence when it is missing. That is survivable for
        // one customer at a time; doing it quietly across a whole import is not, so it is reported.
        if (studentRole is null)
            AddSummary(r, $"WARNING: the \"{StudentRoleName}\" role does not exist, so no roles were assigned. "
                        + "Restart the application to seed it, then re-run this import to attach them.");
        else if (rolesAssigned > 0)
            AddSummary(r, $"{rolesAssigned} customer(s) given the \"{studentRole.DisplayName}\" role — the same one "
                        + "a customer gets when they register on the site.");

        if (staffMatches > 0)
            AddSummary(r, $"WARNING: {staffMatches} old customer record(s) matched an existing account that already "
                        + "holds a staff role (admin / franchise / faculty). They were left as they are. Because this "
                        + "import matches on EMAIL, confirm each is genuinely the same person — a wrong match also "
                        + "points that old customer's orders at a staff account once orders are migrated.");

        if (!dryRun && pending > 0) await _db.SaveChangesAsync(ct);
    }

    private void UpsertUserMap(Dictionary<int, LegacyUserMap> maps, int legacyId, Guid newId, string email)
    {
        if (maps.TryGetValue(legacyId, out var m))
        {
            m.NewId = newId; m.LegacyKey = email; m.ImportedAt = DateTime.UtcNow;
        }
        else
        {
            var nm = new LegacyUserMap { LegacyId = legacyId, NewId = newId, LegacyKey = email };
            _db.LegacyUserMaps.Add(nm);
            maps[legacyId] = nm;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a fix to the failures that are about reaching the server rather than about the data.
    /// The certificate one is near-certain on a first attempt: Microsoft.Data.SqlClient defaults to
    /// Encrypt=True, so any SQL Server with a self-signed certificate — which most on-prem installs
    /// have — fails during login with a chain-of-trust error that reads like a server fault.
    /// </summary>
    private static string ConnectionHint(Exception root)
    {
        var m = root.Message;
        if (m.Contains("certificate", StringComparison.OrdinalIgnoreCase))
            return "  →  Add TrustServerCertificate=True to the connection string (the driver encrypts by default).";
        if (m.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
            return "  →  Check User Id / Password, and that the login can read this database.";
        if (m.Contains("network-related", StringComparison.OrdinalIgnoreCase)
            || m.Contains("not accessible", StringComparison.OrdinalIgnoreCase))
            return "  →  Check the host and port (Server=host,1433), that TCP/IP is enabled on the SQL Server, "
                 + "and that this machine can reach it through the firewall.";
        if (m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            return "  →  The connection worked but the table is missing — check Database=... names the nopCommerce database.";
        return string.Empty;
    }

    /// <summary>
    /// Carries the old site's admin note across, and stamps the source <c>Customer.Active</c> state
    /// when the account arrives disabled. nopCommerce sets Active = 0 both for "registered but never
    /// confirmed their email" and for "an admin switched this one off", and the schema keeps no
    /// record of which — so the note is the only thing that tells a reviewer them apart later. It
    /// matters because the first-login setup flow re-activates a migrated account once its OTP is
    /// verified, and an admin needs to be able to find and re-disable the deliberate ones.
    /// </summary>
    private static string? BuildAdminComment(string? sourceComment, bool active, string? phoneDropNote)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceComment)) parts.Add(sourceComment.Trim());
        if (!active)
            parts.Add("[Migration] This account was INACTIVE on the old website when it was imported. "
                    + "It will be re-activated if the customer completes email-OTP password setup.");
        if (!string.IsNullOrWhiteSpace(phoneDropNote)) parts.Add(phoneDropNote);
        return parts.Count == 0 ? null : string.Join("\n\n", parts);
    }

    /// <summary>
    /// Adds any note that isn't already on the account. Appending rather than <c>??=</c> means a
    /// re-run backfills notes onto customers who already carry the old site's own admin comment,
    /// and the containment check keeps a third and fourth run from stacking duplicates. An admin's
    /// hand-written comment is always kept as the first paragraph.
    /// </summary>
    private static string? AppendMissingNotes(string? current, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming)) return current;
        if (string.IsNullOrWhiteSpace(current)) return incoming;

        var parts = new List<string> { current.Trim() };
        foreach (var note in incoming.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = note.Trim();
            if (trimmed.Length > 0 && !current.Contains(trimmed, StringComparison.Ordinal))
                parts.Add(trimmed);
        }
        return string.Join("\n\n", parts);
    }

    private static void AddMsg(ImportEntityResult r, string msg)
    {
        if (r.Messages.Count < MaxMessages) r.Messages.Add(msg);
    }

    /// <summary>
    /// Adds a run total, bypassing the sample cap and going to the top of the list. Totals are
    /// written after the loop — precisely when the cap is most likely already full — so routing them
    /// through <see cref="AddMsg"/> silently dropped the one line that says how big the problem is,
    /// leaving the admin to read 25 samples as if they were the whole set.
    /// </summary>
    private static void AddSummary(ImportEntityResult r, string msg) => r.Messages.Insert(0, msg);

    private static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = Regex.Replace(raw, @"[^\d]", "");
        if (digits.Length > 10 && digits.StartsWith("91")) digits = digits[^10..];   // strip a 91 country prefix
        // Users.Phone is varchar(15). Legacy free-text phone fields hold things like two numbers run
        // together; writing one would abort the batch on a length violation, so treat it as absent.
        if (digits.Length > 15) return null;
        return digits.Length == 0 ? null : digits;
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return Guid.NewGuid().ToString("n")[..8];
        var s = input.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? Guid.NewGuid().ToString("n")[..8] : s;
    }

    private static string UniqueSlug(string baseSlug, HashSet<string> used)
    {
        var slug = baseSlug; var n = 1;
        while (used.Contains(slug)) slug = $"{baseSlug}-{++n}";
        used.Add(slug);
        return slug;
    }
}
