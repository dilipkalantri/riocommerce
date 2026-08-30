using System.Text.Json;
using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Enums;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace RioCommerce.Infrastructure.Services.SerialKeys;

/// <summary>
/// The orchestrator. Everything outside <c>SerialKeys/</c> talks only to this — controllers,
/// the scheduled task, the order-paid hook, the admin UI. It does:
///   • Idempotent enqueue from <c>CheckoutService.CompleteOrderAsync</c>
///   • Batched dispatch driven by <c>SerialKeyRetryTask</c>
///   • Manual regenerate / activate / revoke from the admin UI
///   • Validation lookup (local DB) and status (local DB + provider when supported)
/// </summary>
public sealed class SerialKeyService : ISerialKeyService
{
    // Exponential backoff in minutes. Index = attempt number; values picked to give support a
    // chance to react to an upstream outage without flooding logs. After this list we dead-letter.
    private static readonly int[] BackoffMinutes = { 1, 5, 15, 60, 360 };

    private readonly RioCommerceDbContext _db;
    private readonly ISerialKeyProviderFactory _factory;
    private readonly INotificationService _notify;
    private readonly INotificationCenterService _center;
    private readonly IAuditService _audit;
    private readonly IAppLogService _appLog;
    private readonly IValencePackService _valencePacks;
    private readonly ISuperclassSettingsService _superclass;
    private readonly ILogger<SerialKeyService> _log;

    public SerialKeyService(
        RioCommerceDbContext db,
        ISerialKeyProviderFactory factory,
        INotificationService notify,
        INotificationCenterService center,
        IAuditService audit,
        IAppLogService appLog,
        IValencePackService valencePacks,
        ISuperclassSettingsService superclass,
        ILogger<SerialKeyService> log)
    {
        _db = db;
        _factory = factory;
        _notify = notify;
        _center = center;
        _audit = audit;
        _appLog = appLog;
        _valencePacks = valencePacks;
        _superclass = superclass;
        _log = log;
    }

    // ── Enqueue ─────────────────────────────────────────────────────────────

    public async Task<int> EnqueueForOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.ProductMode)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return 0;

        // Serial keys are issued only for PAID orders — same policy as invoices. The callers
        // (CheckoutService on payment success, OrderAdminService on confirm/paid transitions)
        // already gate on this, but we re-check here so a direct/manual call can't generate
        // keys for an unpaid order.
        if (order.PaymentStatus != PaymentStatus.Success)
        {
            _log.LogInformation("SerialKey enqueue skipped — order {OrderNumber} not paid (status={Status})",
                order.OrderNumber, order.PaymentStatus);
            return 0;
        }

        // Already-enqueued items are skipped — this whole method is safe to call multiple times.
        var alreadyEnqueued = await _db.Set<SerialKeyRecord>()
            .Where(r => r.OrderId == orderId)
            .Select(r => r.OrderItemId)
            .ToListAsync(ct);
        var skip = alreadyEnqueued.ToHashSet();

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var configs = await _db.Set<ProductSerialKeyConfig>()
            .Where(c => productIds.Contains(c.ProductId) && c.IsActive)
            .ToListAsync(ct);

        // Per-mode resolution with product-level fallback:
        //   * modeConfig[(productId, modeId)] → the override for that specific purchased mode.
        //   * productConfig[productId]        → the product-level row (ProductModeId == null), the fallback.
        // At generation time an item resolves to its mode override if one exists, else the product-level
        // config, else nothing. If duplicates somehow exist, take the earliest (deterministic).
        var modeConfig = configs
            .Where(c => c.ProductModeId != null)
            .GroupBy(c => (c.ProductId, ModeId: c.ProductModeId!.Value))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt).First());
        var productConfig = configs
            .Where(c => c.ProductModeId == null)
            .GroupBy(c => c.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt).First());

        // Rio's "Owner" is an Int32 (their NopCommerce OrderId). The confirmed-working call used a
        // small value (6038). Our OrderId is a Guid, so derive a SMALL positive int: prefer the
        // numeric part of the human OrderNumber (e.g. "RIO-1049" → 1049); otherwise fall back to a
        // hash reduced into a small range. Large hash values appear to be rejected by Rio.
        var serialOrderId = SmallOwnerFrom(order.OrderNumber, order.Id);

        var inserted = 0;
        foreach (var item in order.Items)
        {
            if (skip.Contains(item.Id)) continue;

            // Resolve config: the purchased mode's own override wins; otherwise the product-level
            // config; otherwise this item gets no key.
            ProductSerialKeyConfig? cfg = null;
            if (item.ProductModeId is { } midResolve
                && modeConfig.TryGetValue((item.ProductId, midResolve), out var modeCfg))
            {
                cfg = modeCfg;
            }
            else if (productConfig.TryGetValue(item.ProductId, out var prodCfg))
            {
                cfg = prodCfg;
            }
            if (cfg == null) continue;

            if (!_factory.TryGet(cfg.ProviderKey, out _))
            {
                _log.LogError("SerialKey enqueue skip — provider '{Key}' not registered orderItemId={Item}",
                    cfg.ProviderKey, item.Id);
                continue;
            }

            // ── Split comma-separated Rio product codes into one record per code. ──
            // The old ERP allowed a product to map to multiple Rio product ids ("7094,7093") to
            // generate multiple keys per purchase. We honour that by emitting ONE SerialKeyRecord
            // per code — each with a single-int ProviderProductCode in its own RequestPayload.
            // The retry loop then processes each independently (partial success is natural: one
            // failed code doesn't block the others, and retry targets just the failed record).
            // Only splits for RIOPLAY — Valence keys by our product id and never accepts a list.
            var codes = SplitProviderProductCodes(cfg.ProviderProductCode, cfg.ProviderKey);
            if (codes.Count == 0) codes = new List<string?> { cfg.ProviderProductCode }; // single-code default

            // Build the list of (providerProductCode, per-record ConfigJson) work items. For Rio
            // this is one entry per comma-separated code, each with the same ConfigJson. For
            // Valence with a combo PackIdsCsv (2+ packs), this is one entry per pack, each with
            // its own PackId in ConfigJson so the provider sees exactly one pack per record. This
            // makes multi-pack Valence behave exactly like multi-code Rio in every downstream
            // system (retry, activation, status, email delivery) without provider changes.
            var work = new List<(string? Code, string ConfigJson)>();
            if (string.Equals(cfg.ProviderKey, "valence", StringComparison.OrdinalIgnoreCase))
            {
                var packs = ExtractValencePackIds(cfg.ConfigJson);
                if (packs.Count >= 2)
                {
                    // Each pack registers under its OWN course id. Sending the same course twice is
                    // what made a combo yield one key: Valence resolves the pack from the course, so
                    // the second registration was the same (student, course) pair and simply returned
                    // the first key again.
                    foreach (var pack in packs)
                        work.Add((await ResolveValenceCourseAsync(item.ProductId, pack, isCombo: true, ct),
                                  RewriteValencePack(cfg.ConfigJson, pack)));
                }
            }
            else if (string.Equals(cfg.ProviderKey, "superclass", StringComparison.OrdinalIgnoreCase))
            {
                // Superclass /api/register carries exactly one course_id and one combo_id, so a
                // product covering several of either needs several registrations. Each fanned-out
                // record is pinned to one id and looks like an ordinary single-course product from
                // here on — no provider change, and retry/status/notification all behave as usual.
                // Registration-style provider (no serial key returned), so the duplicate-key guard
                // that limits Valence combos does not apply here.
                var (onCourse, ids) = ExtractSuperclassFanOut(cfg.ConfigJson);
                if (ids.Count >= 2)
                {
                    foreach (var id in ids)
                        // ProviderProductCode mirrors the course id, so it moves with the fan-out
                        // only when the courses are what vary.
                        work.Add((onCourse ? id.ToString() : codes[0], RewriteSuperclassId(cfg.ConfigJson, onCourse, id)));
                }
            }
            if (work.Count == 0)
            {
                // Non-combo path (Rio or single-pack Valence): one work item per code with the
                // config as saved. Preserves today's behaviour byte-for-byte.
                foreach (var code in codes) work.Add((code, cfg.ConfigJson ?? "{}"));
            }

            foreach (var w in work)
            {
                var req = new GenerateKeyRequest
                {
                    ProviderKey = cfg.ProviderKey,
                    UserId = order.UserId,
                    CustomerName = order.StudentName,
                    CustomerEmail = order.StudentEmail,
                    CustomerPhone = order.StudentPhone,
                    // Registration-style providers (Superclass) need country code + pincode separately.
                    // Harmless for RioPlay/Valence, which ignore them. Country code defaults to India (91)
                    // since phones are stored as local numbers; pincode comes from the billing address.
                    CustomerCountryCode = "91",
                    CustomerPincode = order.BillingPincode ?? order.ShippingPincode,
                    OrderId = order.Id,
                    OrderItemId = item.Id,
                    OrderNumber = order.OrderNumber,
                    SerialOrderId = serialOrderId,
                    ProductId = item.ProductId,
                    ProductTitle = item.ProductTitle,
                    ProviderProductCode = w.Code,
                    Quantity = Math.Max(1, item.Quantity),
                    TenantRef = cfg.TenantId?.ToString(),
                    AutoActivate = cfg.AutoActivate,
                    ConfigJson = w.ConfigJson,
                };

                var record = new SerialKeyRecord
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    OrderItemId = item.Id,
                    ProductId = item.ProductId,
                    UserId = order.UserId,
                    ProviderKey = cfg.ProviderKey,
                    TenantRef = cfg.TenantId?.ToString(),
                    Status = SerialKeyStatus.Pending,
                    RequestPayload = JsonSerializer.Serialize(req),
                    NextRetryAt = DateTime.UtcNow,
                };
                _db.Set<SerialKeyRecord>().Add(record);
                inserted++;
            }
        }

        if (inserted > 0)
        {
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("SerialKey enqueued count={Count} orderId={OrderId} orderNumber={OrderNumber}",
                inserted, orderId, order.OrderNumber);
            await _appLog.InfoAsync("SerialKey",
                $"Enqueued {inserted} serial-key record(s) for order {order.OrderNumber}.",
                eventCode: "serial_key.enqueued",
                properties: new { OrderNumber = order.OrderNumber, Inserted = inserted },
                orderId: orderId, ct: ct);
        }
        return inserted;
    }

    /// <summary>Split a comma-separated ProviderProductCode into individual codes. Old-ERP
    /// migrated products may have "7094,7093" to request multiple keys per purchase. Only splits
    /// for the RIOPLAY provider — Valence's ProviderProductCode is a single value never a list.
    /// Returns the trimmed distinct codes; whitespace-only entries are dropped. Returns an empty
    /// list if the input has no comma (caller falls back to single-code path).</summary>
    private static List<string?> SplitProviderProductCodes(string? raw, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string?>();
        if (!string.Equals(providerKey, "rioplay", StringComparison.OrdinalIgnoreCase)) return new();
        if (!raw.Contains(',')) return new();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .Select(s => (string?)s)
                  .ToList();
    }

    /// <summary>Extract Valence pack ids from a stored Valence ConfigJson. Reads
    /// <c>ValenceProductConfig.PackIdsCsv</c> — the comma-separated list set by the admin for a
    /// combo product — and returns the distinct positive int pack ids in order. Returns an empty
    /// list if the config has no CSV (single-pack products fall through to today's PackId
    /// behaviour). Never throws; malformed JSON returns an empty list.</summary>
    private static List<int> ExtractValencePackIds(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return new();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("PackIdsCsv", out var csvProp)) return new();
            var csv = csvProp.ValueKind == System.Text.Json.JsonValueKind.String ? csvProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(csv)) return new();
            return csv!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(s => int.TryParse(s, out var n) ? n : 0)
                       .Where(n => n > 0)
                       .Distinct()
                       .ToList();
        }
        catch (System.Text.Json.JsonException) { return new(); }
    }

    /// <summary>Return a copy of the Valence ConfigJson with <c>PackId</c> set to the given pack
    /// and <c>PackIdsCsv</c> cleared. Used to derive a per-record ConfigJson from a combo config
    /// so each fanned-out SerialKeyRecord looks like a single-pack product to the provider.</summary>
    private static string RewriteValencePack(string? configJson, int packId)
    {
        Providers.ValenceProductConfig cfg;
        try
        {
            cfg = string.IsNullOrWhiteSpace(configJson)
                ? new Providers.ValenceProductConfig()
                : (System.Text.Json.JsonSerializer.Deserialize<Providers.ValenceProductConfig>(configJson!) ?? new Providers.ValenceProductConfig());
        }
        catch (System.Text.Json.JsonException) { cfg = new Providers.ValenceProductConfig(); }
        cfg.PackId = packId;
        cfg.PackIdsCsv = null;   // per-record config is single-pack
        return System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    /// <summary>The scalar <c>PackId</c> in a Valence ConfigJson, or null. Never throws.</summary>
    private static int? ExtractSinglePackId(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("PackId", out var p)) return null;
            return p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) && n > 0 ? n : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The raw <c>PackIdsCsv</c> in a Valence ConfigJson, or null. Never throws.</summary>
    private static string? ExtractValencePackCsv(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            return doc.RootElement.TryGetProperty("PackIdsCsv", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// The <c>course</c> to send Valence for one pack of a product.
    ///
    /// <para>Valence resolves the pack from the course — <c>register_student_with_course</c> carries
    /// no pack of its own. A COMBO therefore used to yield ONE key: both of its packs hung off the
    /// same course, so the second registration was the same (student, course) pair as the first and
    /// Valence returned the first key again.</para>
    ///
    /// <para>On Valence a pack belongs to ONE course. Packs in a combo are normally already claimed
    /// by the standalone products that sell them, and re-mapping such a pack to the combo's own
    /// course does nothing — <c>save_product_pack</c> still answers "Data saved successfully", but
    /// registration then answers "No pack found for this course". (Confirmed against the live API on
    /// 21 Aug 2026, including with a synthetic per-pack course id, which failed the same way.)</para>
    ///
    /// <para>So a combo registers each pack under the course of the product that OWNS that pack. Those
    /// courses already resolve, so the student gets one real key per subject. When a pack has no
    /// owner — a pack sold only inside the combo — the combo's own course can hold it, and does.</para>
    /// </summary>
    private async Task<string> ResolveValenceCourseAsync(
        Guid productId, int packId, bool isCombo, CancellationToken ct)
    {
        if (!isCombo || packId <= 0) return productId.ToString();
        var owner = await FindValencePackOwnerAsync(productId, packId, ct);
        return (owner ?? productId).ToString();
    }

    /// <summary>
    /// The single-pack product that already owns this pack on Valence, or null when nothing does.
    ///
    /// <para>Other combos are skipped deliberately: a combo never owns a pack on Valence's side, so
    /// pointing one combo at another would just move the problem. Ordered by creation so the answer
    /// is stable if two products were ever configured with the same pack.</para>
    ///
    /// <para>"Single-pack" is measured the same way <see cref="ValencePackIds"/> measures it
    /// everywhere else — the EFFECTIVE list, scalar plus CSV, de-duplicated. It used to require the
    /// CSV to be blank instead, which quietly disqualified the very products this method exists to
    /// find: a standalone whose CSV simply restates its one pack ("449") is single-pack in every
    /// sense that matters, yet was rejected, leaving the owner null and the combo registering both
    /// packs under its OWN course — the "No pack found for this course" failure on RIO-1077. A real
    /// combo is still excluded, now because its effective list holds two ids rather than because a
    /// field happened to be filled in.</para>
    /// </summary>
    private async Task<Guid?> FindValencePackOwnerAsync(Guid excludeProductId, int packId, CancellationToken ct)
    {
        var candidates = await _db.Set<ProductSerialKeyConfig>().AsNoTracking()
            .Where(c => c.IsActive && c.ProviderKey == "valence" && c.ProductId != excludeProductId)
            .Select(c => new { c.ProductId, c.ConfigJson, c.CreatedAt })
            .ToListAsync(ct);

        return candidates
            .Where(c => ValencePackIds(ExtractSinglePackId(c.ConfigJson),
                                       ExtractValencePackCsv(c.ConfigJson))
                        is { Count: 1 } single
                        && single[0] == packId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => (Guid?)c.ProductId)
            .FirstOrDefault();
    }

    /// <summary>
    /// Parses a comma-separated list of positive ids typed by an admin, rejecting anything that is
    /// not one — a silently-dropped id means a customer never gets that course, so a typo has to be
    /// a save-time error rather than a surprise at dispatch. Duplicates are collapsed; blank input is
    /// valid and yields an empty list.
    /// </summary>
    private static (List<int> Ids, string? Error) ParseIdCsv(string? raw, string fieldLabel)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(raw)) return (ids, null);

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var n) || n <= 0)
                return (new List<int>(), $"“{part}” is not a valid {fieldLabel}. Enter positive numbers separated by commas (e.g. 2722, 2723).");
            if (!ids.Contains(n)) ids.Add(n);
        }
        return (ids, null);
    }

    /// <summary>
    /// Works out which mapping mode a stored Superclass config represents. Nothing records the mode
    /// on disk — it is implied by which ids are present, so every pre-existing row keeps its exact
    /// shape and no migration or backfill is required.
    ///
    /// <para>A combo id is the discriminator. Course id carries no signal on its own: it used to be
    /// mandatory, so EVERY row has one, including combo-only products where the admin was forced to
    /// repeat the combo id in it. A combo id, by contrast, is optional and was only ever set
    /// deliberately — so its presence means the product is granted as a combo.</para>
    /// </summary>
    internal static SuperclassMappingMode DeriveSuperclassMode(Providers.SuperclassProductConfig cfg)
    {
        // Explicit precedence, combo first: a combo id is only ever set deliberately.
        if (!string.IsNullOrWhiteSpace(cfg.ComboIdsCsv) || cfg.ComboId is > 0)
            return SuperclassMappingMode.ComboPackages;

        // ANY course list means the admin chose the list mode — one id included. The scalar CourseId
        // is no evidence either way, because the list mode stores its first id there too; testing it
        // first is what made a saved multi-course product reload as a single course.
        if (ParseIdCsv(cfg.CourseIdsCsv, "Course ID").Ids.Count >= 1)
            return SuperclassMappingMode.MultipleCourses;

        return SuperclassMappingMode.SingleCourse;
    }

    /// <summary>
    /// Which axis a Superclass product fans out on, and the ids to fan out over. Returns an empty
    /// list for an ordinary single-course product, which then follows the untouched single-record path.
    /// </summary>
    private static (bool OnCourse, List<int> Ids) ExtractSuperclassFanOut(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return (true, new List<int>());
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var courses = ReadIdCsv(doc.RootElement, "CourseIdsCsv");
            if (courses.Count >= 2) return (true, courses);
            var combos = ReadIdCsv(doc.RootElement, "ComboIdsCsv");
            if (combos.Count >= 2) return (false, combos);
        }
        catch (JsonException) { /* malformed config falls back to the single-record path */ }
        return (true, new List<int>());

        static List<int> ReadIdCsv(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String) return new();
            var csv = prop.GetString();
            if (string.IsNullOrWhiteSpace(csv)) return new();
            return csv!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(s => int.TryParse(s, out var n) ? n : 0)
                       .Where(n => n > 0).Distinct().ToList();
        }
    }

    /// <summary>Returns a copy of a Superclass ConfigJson pinned to ONE id on the fan-out axis, with
    /// both CSVs cleared — so each fanned-out record looks like an ordinary single-course product to
    /// the provider, which knows nothing about combos.</summary>
    private static string RewriteSuperclassId(string? configJson, bool onCourse, int id)
    {
        Providers.SuperclassProductConfig cfg;
        try
        {
            cfg = string.IsNullOrWhiteSpace(configJson)
                ? new Providers.SuperclassProductConfig()
                : (JsonSerializer.Deserialize<Providers.SuperclassProductConfig>(configJson!) ?? new Providers.SuperclassProductConfig());
        }
        catch (JsonException) { cfg = new Providers.SuperclassProductConfig(); }

        // Pin the fan-out axis and CLEAR the other one. /api/register grants through a course id or
        // a combo id, never both, so leaving the opposite field populated would ride an unrelated id
        // along on every registration — and on a course fan-out it would send the same combo id N
        // times, which is N duplicate grants rather than the N distinct courses that were intended.
        if (onCourse) { cfg.CourseId = id; cfg.ComboId = null; }
        else { cfg.ComboId = id; cfg.CourseId = null; }
        cfg.CourseIdsCsv = null;
        cfg.ComboIdsCsv = null;
        return JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    // ── Batched dispatch (called by SerialKeyRetryTask) ─────────────────────

    public async Task<(int processed, int generated, int failed, int deadLettered)> ProcessDueAsync(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Superclass dead-letter cap comes from its global settings (resolved lazily, once per batch).
        int? superclassMax = null;
        var due = await _db.Set<SerialKeyRecord>()
            .Where(r => (r.Status == SerialKeyStatus.Pending || r.Status == SerialKeyStatus.Failed)
                        && r.NextRetryAt <= now)
            .OrderBy(r => r.NextRetryAt)
            .Take(batchSize)
            .ToListAsync(ct);

        int processed = 0, generated = 0, failed = 0, deadLettered = 0;

        var first = true;
        foreach (var rec in due)
        {
            if (ct.IsCancellationRequested) break;

            // Space out records — Rio's edge soft-404s rapid back-to-back requests from the same
            // client (a transient rate-limit). Each record makes ~3 Rio calls, so without spacing
            // a batch of N records fires 3N requests in milliseconds and Rio rejects them all with
            // 404 (Server: Kestrel, empty body). A short gap between records avoids this.
            if (!first) await Task.Delay(1200, ct);
            first = false;

            processed++;

            if (!_factory.TryGet(rec.ProviderKey, out var provider))
            {
                rec.Status = SerialKeyStatus.DeadLettered;
                rec.ErrorCode = "no_provider";
                rec.ErrorMessage = $"Provider '{rec.ProviderKey}' is no longer registered.";
                deadLettered++;
                _log.LogError("SerialKey dead-letter no-provider id={Id} provider={Provider}", rec.Id, rec.ProviderKey);
                continue;
            }

            GenerateKeyRequest? req;
            try { req = JsonSerializer.Deserialize<GenerateKeyRequest>(rec.RequestPayload); }
            catch (JsonException ex)
            {
                rec.Status = SerialKeyStatus.DeadLettered;
                rec.ErrorCode = "bad_request_payload";
                rec.ErrorMessage = ex.Message;
                deadLettered++;
                _log.LogError(ex, "SerialKey dead-letter bad payload id={Id}", rec.Id);
                continue;
            }
            if (req == null)
            {
                rec.Status = SerialKeyStatus.DeadLettered;
                rec.ErrorCode = "null_request";
                rec.ErrorMessage = "Stored RequestPayload deserialised to null.";
                deadLettered++;
                continue;
            }

            // Refresh the serial-key config from the DB so admin edits to the config (e.g. fixing
            // EWatchTime, product code, validity) take effect on the next attempt without re-placing
            // the order. The RequestPayload snapshots config at enqueue time; here we overlay the
            // latest live values — resolving the SAME way enqueue does: the purchased mode's own
            // override wins, else the product-level (ProductModeId == null) config.
            try
            {
                var recModeId = await _db.OrderItems
                    .Where(i => i.Id == rec.OrderItemId)
                    .Select(i => i.ProductModeId)
                    .FirstOrDefaultAsync(ct);

                ProductSerialKeyConfig? liveCfg = null;
                if (recModeId is { } rmid)
                {
                    liveCfg = await _db.Set<ProductSerialKeyConfig>()
                        .Where(c => c.ProductId == rec.ProductId && c.ProductModeId == rmid && c.IsActive)
                        .OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt)
                        .FirstOrDefaultAsync(ct);
                }
                liveCfg ??= await _db.Set<ProductSerialKeyConfig>()
                    .Where(c => c.ProductId == rec.ProductId && c.ProductModeId == null && c.IsActive)
                    .OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (liveCfg != null)
                {
                    // ── The PROVIDER follows the config too ─────────────────────────────────────
                    // The vendor was fixed on the record at enqueue time, so re-issuing a key for a
                    // product that has since been moved from one vendor to another went straight
                    // back to the old vendor — the product page said Superclass and Regenerate kept
                    // producing Valence keys. Retrying is "issue this again using whatever this
                    // product is mapped to NOW", which is the only reading that matches the button.
                    // Only switched when the new vendor is actually registered, so an unknown key
                    // cannot strand the record.
                    if (!string.IsNullOrWhiteSpace(liveCfg.ProviderKey)
                        && !string.Equals(liveCfg.ProviderKey, rec.ProviderKey, StringComparison.OrdinalIgnoreCase)
                        && _factory.TryGet(liveCfg.ProviderKey, out var liveProvider))
                    {
                        _log.LogInformation("SerialKey provider re-resolved id={Id} {Old} → {New} (product config changed)",
                            rec.Id, rec.ProviderKey, liveCfg.ProviderKey);
                        rec.ProviderKey = liveCfg.ProviderKey;
                        req.ProviderKey = liveCfg.ProviderKey;
                        provider = liveProvider;
                        // A key issued by the previous vendor says nothing about this attempt.
                        rec.SerialKey = null;
                        rec.ExternalReference = null;
                    }

                    // ── Keep THIS record's own pack ─────────────────────────────────────────────
                    // A combo fans out one record per pack, but the live config is the PRODUCT's and
                    // lists every pack. Overlaying it wholesale collapsed both records onto the same
                    // pack on the first retry — quietly undoing the fan-out. Re-pin the record's own
                    // pack after the overlay, and re-derive its course id from the CURRENT config so
                    // a product that gained or lost its combo packs is followed correctly.
                    var recordPack = ExtractSinglePackId(req.ConfigJson);

                    if (!string.IsNullOrWhiteSpace(liveCfg.ConfigJson)) req.ConfigJson = liveCfg.ConfigJson;
                    if (!string.IsNullOrWhiteSpace(liveCfg.ProviderProductCode)) req.ProviderProductCode = liveCfg.ProviderProductCode;
                    if (liveCfg.TenantId is { } t) req.TenantRef = t.ToString();
                    req.AutoActivate = liveCfg.AutoActivate;

                    if (string.Equals(rec.ProviderKey, "valence", StringComparison.OrdinalIgnoreCase))
                    {
                        var livePacks = ValencePackIds(
                            ExtractSinglePackId(liveCfg.ConfigJson), ExtractValencePackCsv(liveCfg.ConfigJson));
                        var isCombo = livePacks.Count >= 2;

                        // Prefer the pack this record was created for; fall back to the config's own
                        // when the record predates the fan-out.
                        var pack = recordPack is > 0 && (!isCombo || livePacks.Contains(recordPack.Value))
                            ? recordPack.Value
                            : livePacks.FirstOrDefault();

                        if (pack > 0)
                        {
                            req.ConfigJson = RewriteValencePack(req.ConfigJson, pack);
                            req.ProviderProductCode = await ResolveValenceCourseAsync(rec.ProductId, pack, isCombo, ct);
                        }
                    }
                }

                // NOTE: do not write to _appLog from inside this try. It shares the scoped DbContext
                // that is mid-flight here, and an exception from it is swallowed by the catch below —
                // which silently skips the customer-field refresh that follows.

                // ── Refresh the CUSTOMER fields too ─────────────────────────────────────────────
                // The config was already re-read here; the customer was not, so the enqueue-time
                // snapshot was used for ever. A vendor rejecting the order for a missing pincode,
                // email or phone could therefore never be fixed: correcting the order changed
                // nothing, and Retry re-sent the same incomplete payload. Superclass returns exactly
                // that — 403 "The Pincode field is required" — on an order whose pincode was filled
                // in after the key was queued.
                var liveOrder = await _db.Orders.AsNoTracking()
                    .Where(o => o.Id == rec.OrderId)
                    .Select(o => new
                    {
                        o.StudentName, o.StudentEmail, o.StudentPhone,
                        Pincode = o.BillingPincode ?? o.ShippingPincode,
                    })
                    .FirstOrDefaultAsync(ct);

                if (liveOrder != null)
                {
                    // Only overwrite with something real — a field cleared on the order must not wipe
                    // a value that was captured correctly at checkout.
                    if (!string.IsNullOrWhiteSpace(liveOrder.StudentName)) req.CustomerName = liveOrder.StudentName;
                    if (!string.IsNullOrWhiteSpace(liveOrder.StudentEmail)) req.CustomerEmail = liveOrder.StudentEmail;
                    if (!string.IsNullOrWhiteSpace(liveOrder.StudentPhone)) req.CustomerPhone = liveOrder.StudentPhone;
                    if (!string.IsNullOrWhiteSpace(liveOrder.Pincode)) req.CustomerPincode = liveOrder.Pincode;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SerialKey config refresh failed id={Id} — using snapshot", rec.Id);
            }

            rec.AttemptCount++;
            rec.LastAttemptAt = DateTime.UtcNow;
            rec.Status = SerialKeyStatus.Generating;

            GenerateKeyResult result;

            // ── Valence: guarantee the course↔pack mapping EXISTS before the student is registered ──
            // register_student_with_course sends only `course` (our product id) — never a pack. Valence
            // resolves which pack that course means from its OWN mapping table. When that mapping is
            // absent, Valence still registers the student and still returns a key, just against a
            // different pack — so the customer receives a working key for the WRONG subject, with no
            // error anywhere. That is exactly how RIO-1048/1052/1056/1058 shipped wrong: save_product_pack
            // had been 404ing (doubled /index.php) since the legacy import, and nothing ever re-sent it,
            // while registration carried on working.
            //
            // Asserting the mapping HERE — immediately before the call that depends on it — is the only
            // place that cannot drift out of date. It no longer matters whether anyone re-saved the
            // product config. Valence treats a repeat mapping as success ("This combination already
            // exists"), so the cost is one idempotent request per attempt.
            var mappingFailure = await EnsureValenceMappingAsync(rec, req, ct);
            if (mappingFailure != null)
            {
                // Deliberately do NOT register: a retryable failure is recoverable, a wrong key in a
                // customer's inbox is not.
                result = mappingFailure;
            }
            else
            {
                try
                {
                    result = await provider.GenerateAsync(req, ct);
                }
                catch (Exception ex)
                {
                    // Providers shouldn't throw, but defend regardless — one bad provider mustn't kill the batch.
                    _log.LogError(ex, "SerialKey provider THREW id={Id} provider={Provider}", rec.Id, rec.ProviderKey);
                    result = GenerateKeyResult.Fail("provider_exception", ex.Message);
                }
            }

            // Capture the ACTUAL parameters sent to the provider API (Rio's wrapped Entity body, or
            // Valence's form fields with the secret masked) for later parameter inspection. Set on
            // every attempt — success or failure — so the admin can always see what was sent last.
            // ToJsonbOrNull keeps the jsonb column valid even if a provider left it blank/non-JSON.
            if (!string.IsNullOrWhiteSpace(result.RawRequest))
                rec.ApiRequestPayload = ToJsonbOrNull(result.RawRequest);

            var providerIssuesKey = provider.IssuesSerialKey;
            // Success gate: key-issuing providers (RioPlay, Valence) MUST return a non-empty SerialKey;
            // registration-style providers (Superclass) succeed WITHOUT a key — they return a
            // student/subscription reference instead. Both cases are a valid success here.
            if (result.Success && (!providerIssuesKey || !string.IsNullOrWhiteSpace(result.SerialKey)))
            {
                // Duplicate-key guard. The unique index IX_serial_key_records_SerialKey forbids two
                // records sharing a key. Some providers (notably Valence, whose success shape isn't
                // fully confirmed) can echo the same confirmation string for different registrations,
                // which would collide on save (Postgres 23505) and abort the whole batch. Detect it
                // here and route THIS record to the failure path instead of inserting a colliding key.
                // Only relevant when a key is present — keyless registration success skips it (a null
                // key never collides on the filtered unique index).
                var dupExists = !string.IsNullOrWhiteSpace(result.SerialKey)
                    && await _db.Set<SerialKeyRecord>()
                        .AnyAsync(r => r.Id != rec.Id && r.SerialKey == result.SerialKey, ct);
                if (dupExists)
                {
                    var rawKept = result.RawResponse;
                    _log.LogError("SerialKey duplicate key returned id={Id} provider={Provider} key was already stored on another record — treating as failure.",
                        rec.Id, rec.ProviderKey);
                    result = GenerateKeyResult.Fail("duplicate_key",
                        "Provider returned a serial key that is already assigned to another record. " +
                        "This usually means the response was not a real per-order key (raw response captured).",
                        rawKept);
                    // fall through to the failure path below (do NOT assign rec.SerialKey).
                }
                else
                {
                rec.SerialKey = result.SerialKey;
                rec.ExternalReference = result.ExternalReference;
                // Registration-style outcome (Superclass) — null for key-issuing providers.
                rec.SubscriptionStatus = result.SubscriptionStatus;
                rec.ExpiresAt = result.ExpiresAt;
                rec.ProviderCourseRef = result.ProviderCourseRef;
                rec.ResponsePayload = ToJsonbOrNull(result.RawResponse);
                rec.GeneratedAt = DateTime.UtcNow;
                rec.Status = result.Activated ? SerialKeyStatus.Activated : SerialKeyStatus.Generated;
                if (result.Activated) rec.ActivatedAt = DateTime.UtcNow;
                rec.ErrorCode = null;
                rec.ErrorMessage = null;
                generated++;

                await _audit.WriteAsync(new AuditEntry
                {
                    ActorName = "system",
                    Module = "Integrations",
                    Action = "SerialKey.Generated",
                    EntityType = "SerialKeyRecord",
                    EntityId = rec.Id.ToString(),
                    EntityName = rec.SerialKey ?? rec.ExternalReference,
                    Status = "Success",
                    Details = $"Provider={rec.ProviderKey} Order={rec.OrderId} Activated={result.Activated}",
                });

                // Persist the success state IMMEDIATELY (per-record) so a later failure in this
                // batch can't roll it back and cause reprocessing → duplicate keys/emails.
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException dux) when (IsUniqueSerialKeyViolation(dux))
                {
                    // Lost a race (or a provider echoed a stored key between our check and save).
                    // Reset the tracked entity's key and route to the failure path so the batch survives.
                    _db.Entry(rec).State = EntityState.Detached;
                    var fresh = await _db.Set<SerialKeyRecord>().FirstAsync(r => r.Id == rec.Id, ct);
                    _log.LogError(dux, "SerialKey unique-violation on save id={Id} — routing to retry.", rec.Id);
                    fresh.SerialKey = null;
                    fresh.AttemptCount = rec.AttemptCount;
                    fresh.LastAttemptAt = rec.LastAttemptAt;
                    fresh.ApiRequestPayload = rec.ApiRequestPayload;
                    fresh.Status = SerialKeyStatus.Failed;
                    fresh.ErrorCode = "duplicate_key";
                    fresh.ErrorMessage = "Provider returned a serial key already assigned to another record.";
                    fresh.NextRetryAt = DateTime.UtcNow.AddMinutes(BackoffMinutes[Math.Min(rec.AttemptCount, BackoffMinutes.Length - 1)]);
                    generated--; failed++;
                    await _db.SaveChangesAsync(ct);
                    continue;
                }

                // Notify customer exactly once — ONLY for key-issuing providers. Registration-style
                // providers (Superclass) send their own welcome email + WhatsApp, so we must not also
                // send our "your serial key is ready" message (there is no key, and it would duplicate).
                if (providerIssuesKey && rec.NotifiedAt == null)
                {
                    rec.NotifiedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);   // commit the guard before dispatching
                    await TryNotifyAsync(rec, req);
                }
                await _appLog.InfoAsync("SerialKey",
                    providerIssuesKey
                        ? $"Generated{(result.Activated ? " + activated" : "")} key for order {req.OrderNumber}."
                        : $"Provisioned access via {rec.ProviderKey} for order {req.OrderNumber} (ref {rec.ExternalReference}).",
                    eventCode: providerIssuesKey ? "serial_key.generated" : "serial_key.provisioned",
                    properties: new { Provider = rec.ProviderKey, Activated = result.Activated, rec.ExternalReference, KeyPreview = rec.SerialKey?.Substring(0, Math.Min(8, rec.SerialKey?.Length ?? 0)) },
                    orderId: rec.OrderId, ct: ct);
                continue;
                }
            }

            // Failure path — schedule retry or dead-letter.
            // ResponsePayload is a jsonb column: an empty string or non-JSON text (Rio's 404 returns
            // an empty body) is INVALID jsonb and makes SaveChanges throw. Store valid JSON or null.
            rec.ResponsePayload = ToJsonbOrNull(result.RawResponse);
            rec.ErrorCode = result.ErrorCode;
            rec.ErrorMessage = result.ErrorMessage;

            // Dead-letter cap: key-issuing providers use the fixed backoff-ladder length; Superclass uses
            // its configurable MaxRetryAttempts (global settings), resolved once per batch.
            var deadLetterCap = BackoffMinutes.Length;
            if (string.Equals(rec.ProviderKey, "superclass", StringComparison.OrdinalIgnoreCase))
            {
                superclassMax ??= (await _superclass.ResolveAsync(ct))?.MaxRetryAttempts ?? BackoffMinutes.Length;
                deadLetterCap = Math.Max(1, superclassMax.Value);
            }

            var nextSlot = rec.AttemptCount - 1;
            if (nextSlot >= BackoffMinutes.Length || rec.AttemptCount >= deadLetterCap)
            {
                rec.Status = SerialKeyStatus.DeadLettered;
                deadLettered++;
                _log.LogError("SerialKey DEAD-LETTERED id={Id} order={Order} attempts={Attempts} code={Code} msg={Msg}",
                    rec.Id, rec.OrderId, rec.AttemptCount, rec.ErrorCode, rec.ErrorMessage);

                await _center.NotifyAsync(AdminNotificationType.SystemAlert, NotificationSeverity.Warning,
                    "Serial key generation failed",
                    $"Order {req.OrderNumber}: {rec.ErrorMessage}",
                    $"/admin/integrations/serial-keys?orderId={rec.OrderId}",
                    rec.Id.ToString());

                await _appLog.CriticalAsync("SerialKey",
                    $"Dead-lettered after {rec.AttemptCount} attempts: {rec.ErrorMessage}",
                    eventCode: "serial_key.dead_lettered",
                    properties: new { rec.ProviderKey, rec.ErrorCode, rec.AttemptCount, RawResponse = result.RawResponse },
                    orderId: rec.OrderId, ct: ct);
            }
            else
            {
                rec.Status = SerialKeyStatus.Failed;
                rec.NextRetryAt = DateTime.UtcNow.AddMinutes(BackoffMinutes[nextSlot]);
                failed++;
                _log.LogWarning("SerialKey failed (will retry) id={Id} attempts={Attempts} nextRetry={Next} code={Code}",
                    rec.Id, rec.AttemptCount, rec.NextRetryAt, rec.ErrorCode);
                await _appLog.WarnAsync("SerialKey",
                    $"Attempt {rec.AttemptCount} failed ({rec.ErrorCode}): {rec.ErrorMessage}. Next retry at {rec.NextRetryAt:HH:mm:ss}.",
                    eventCode: "serial_key.retry_scheduled",
                    properties: new { rec.ProviderKey, rec.ErrorCode, rec.AttemptCount, NextRetryAt = rec.NextRetryAt, RawResponse = result.RawResponse },
                    orderId: rec.OrderId, ct: ct);
            }
        }

        if (processed > 0) await _db.SaveChangesAsync(ct);
        return (processed, generated, failed, deadLettered);
    }

    /// <summary>Coerces a raw provider response into something safe for a Postgres <c>jsonb</c>
    /// column. Rio returns an empty body on 404, and an empty/non-JSON string is INVALID jsonb —
    /// writing it makes SaveChanges throw "22P02: invalid input syntax for type json". We return
    /// null for empty/blank, pass through valid JSON as-is, and wrap any other text as a JSON
    /// string so the raw response is still captured for debugging.</summary>
    private static string? ToJsonbOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.TrimStart();
        // Looks like JSON already (object/array/string/number/bool/null) → store verbatim.
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[") || trimmed.StartsWith("\""))
            return raw;
        // Plain text (e.g. an HTML error page) → wrap as a JSON string literal so it's valid jsonb.
        return JsonSerializer.Serialize(raw);
    }

    /// <summary>True when the exception chain is a unique-constraint violation on the SerialKey index
    /// (Postgres SQLSTATE 23505 on IX_serial_key_records_SerialKey). Matched on message text so we don't
    /// take a hard dependency on the Npgsql exception type here.</summary>
    private static bool IsUniqueSerialKeyViolation(Exception ex)
    {
        var cur = ex;
        var guard = 0;
        while (cur != null && guard++ < 5)
        {
            var m = cur.Message ?? "";
            if (m.Contains("23505") || m.Contains("IX_serial_key_records_SerialKey"))
                return true;
            cur = cur.InnerException;
        }
        return false;
    }

    private async Task TryNotifyAsync(SerialKeyRecord rec, GenerateKeyRequest req)
    {
        try
        {
            var tokens = new Dictionary<string, string>
            {
                ["name"] = req.CustomerName,
                ["order_number"] = req.OrderNumber,
                ["product_title"] = req.ProductTitle,
                ["serial_key"] = rec.SerialKey ?? "",
            };
            await _notify.SendAsync("serial_key_generated",
                new NotificationRecipient(Email: req.CustomerEmail, Phone: req.CustomerPhone),
                tokens);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SerialKey notification dispatch failed id={Id}", rec.Id);
        }
    }

    // ── Manual ops ──────────────────────────────────────────────────────────

    public async Task<(bool ok, string? error, int sent, string? sentTo)> ResendKeyEmailAsync(
        Guid orderId, Guid? recordId, Guid? actorId, CancellationToken ct = default)
    {
        var order = await _db.Orders.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return (false, "Order not found.", 0, null);
        if (order.IsDeleted) return (false, "This order is in the recycle bin.", 0, null);

        // The mail hands over paid-for course access. Keys are only dispatched after payment in the
        // first place, so a resend before payment would be a way around that.
        if (order.PaymentStatus != PaymentStatus.Success)
            return (false, "Only paid orders can have the serial key email resent.", 0, null);

        // The order's CURRENT address wins over whatever the key was originally mailed to — the
        // usual reason for a resend is that the address was wrong and has just been corrected.
        var email = order.StudentEmail;
        if (string.IsNullOrWhiteSpace(email))
            return (false, "This order has no email address. Add one on the order first.", 0, null);

        var records = await _db.Set<SerialKeyRecord>()
            .Where(r => r.OrderId == orderId && (recordId == null || r.Id == recordId))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        if (records.Count == 0) return (false, "No serial key records on this order.", 0, null);

        // Only records that actually hold a key the customer can use. A revoked key must never be
        // re-sent — it has been deliberately invalidated — and a pending or failed record has no key
        // to quote, so the mail would arrive with an empty {{serial_key}}.
        var sendable = records
            .Where(r => !string.IsNullOrWhiteSpace(r.SerialKey)
                     && r.Status is SerialKeyStatus.Generated or SerialKeyStatus.Activated)
            .ToList();

        if (sendable.Count == 0)
        {
            var revoked = records.Any(r => r.Status == SerialKeyStatus.Revoked);
            return (false, revoked
                ? "This order's key has been revoked — it cannot be resent."
                : "No key has been issued on this order yet.", 0, null);
        }

        var sent = 0;
        foreach (var rec in sendable)
        {
            // Registration-style providers (Superclass) send their own welcome mail and issue no key
            // of ours, so "your serial key is ready" would be both a duplicate and empty. Same rule
            // the generation path applies — kept in sync via IssuesSerialKey, not a hardcoded name.
            if (_factory.TryGet(rec.ProviderKey, out var provider) && !provider.IssuesSerialKey) continue;

            // The stored request carries the product title and customer name as they were at
            // generation time; the address is overridden with the order's current one above.
            GenerateKeyRequest? req = null;
            try { req = JsonSerializer.Deserialize<GenerateKeyRequest>(rec.RequestPayload); }
            catch (JsonException) { /* fall back to the order below */ }

            var tokens = new Dictionary<string, string>
            {
                ["name"] = req?.CustomerName ?? order.StudentName,
                ["order_number"] = req?.OrderNumber ?? order.OrderNumber,
                ["product_title"] = req?.ProductTitle ?? "",
                ["serial_key"] = rec.SerialKey!,
            };

            try
            {
                var (ok, _, channels) = await _notify.SendAsync("serial_key_generated",
                    new NotificationRecipient(Email: email, Phone: order.StudentPhone),
                    tokens,
                    triggeredBy: "Serial Key Resent");
                if (ok && channels > 0)
                {
                    sent++;
                    // Stamp only on a real send, so the field keeps meaning "the customer has been
                    // told about this key" rather than "someone pressed the button".
                    rec.NotifiedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SerialKey resend failed recordId={Id}", rec.Id);
            }
        }

        if (sent == 0)
        {
            // Every eligible record was a registration-style provider, or every send failed.
            return (false, sendable.All(r => _factory.TryGet(r.ProviderKey, out var p) && !p.IssuesSerialKey)
                ? "This product's provider emails the customer directly — there is no key email to resend."
                : "Could not send the email. Please try again in a moment.", 0, null);
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = "admin",
            Module = "Integrations",
            Action = "SerialKey.ResendEmail",
            EntityType = "Order",
            EntityId = orderId.ToString(),
            Status = "Success",
            Details = $"Order={order.OrderNumber} Keys={sent} SentTo={email}",
        });

        return (true, null, sent, email);
    }

    public async Task<bool> RegenerateAsync(Guid recordId, Guid? actorId, CancellationToken ct = default)
    {
        var rec = await _db.Set<SerialKeyRecord>().FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (rec == null) return false;
        rec.Status = SerialKeyStatus.Pending;
        rec.AttemptCount = 0;
        rec.NextRetryAt = DateTime.UtcNow;
        rec.ErrorCode = null;
        rec.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = "admin",
            Module = "Integrations",
            Action = "SerialKey.Regenerate",
            EntityType = "SerialKeyRecord",
            EntityId = rec.Id.ToString(),
            Status = "Success",
            Details = $"Order={rec.OrderId}",
        });
        return true;
    }

    public async Task<SerialKeyDetail?> ValidateAsync(string serialKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serialKey)) return null;
        var rec = await _db.Set<SerialKeyRecord>()
            .Include(r => r.Order).Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.SerialKey == serialKey, ct);
        return rec == null ? null : ToDetail(rec);
    }

    public async Task<ActivateKeyResult> ActivateAsync(string serialKey, CancellationToken ct = default)
    {
        var rec = await _db.Set<SerialKeyRecord>().FirstOrDefaultAsync(r => r.SerialKey == serialKey, ct);
        if (rec == null) return ActivateKeyResult.Fail("not_found", "Serial key not found.");
        if (!_factory.TryGet(rec.ProviderKey, out var provider))
            return ActivateKeyResult.Fail("no_provider", $"Provider '{rec.ProviderKey}' not registered.");
        if (!provider.SupportsActivate)
            return ActivateKeyResult.Fail("not_supported", "Provider does not support activate.");

        var result = await provider.ActivateAsync(serialKey, rec.TenantRef, ct);
        if (result.Success)
        {
            rec.Status = SerialKeyStatus.Activated;
            rec.ActivatedAt = DateTime.UtcNow;
            rec.ExternalReference = result.ExternalReference ?? rec.ExternalReference;
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync(new AuditEntry
            {
                ActorName = "system",
                Module = "Integrations",
                Action = "SerialKey.Activated",
                EntityType = "SerialKeyRecord",
                EntityId = rec.Id.ToString(),
                EntityName = serialKey,
                Status = "Success",
            });
        }
        return result;
    }

    public async Task<KeyStatusResult> GetStatusAsync(string serialKey, CancellationToken ct = default)
    {
        var rec = await _db.Set<SerialKeyRecord>().FirstOrDefaultAsync(r => r.SerialKey == serialKey, ct);
        if (rec == null) return KeyStatusResult.Fail("not_found", "Serial key not found.");
        if (_factory.TryGet(rec.ProviderKey, out var provider) && provider.SupportsStatusLookup)
            return await provider.GetStatusAsync(serialKey, rec.TenantRef, ct);
        return KeyStatusResult.Ok(rec.Status.ToString());
    }

    public async Task<bool> RevokeAsync(Guid recordId, string reason, Guid? actorId, CancellationToken ct = default)
    {
        var rec = await _db.Set<SerialKeyRecord>().FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (rec == null) return false;
        rec.Status = SerialKeyStatus.Revoked;
        rec.RevokedAt = DateTime.UtcNow;
        rec.RevokedReason = reason;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = "admin",
            Module = "Integrations",
            Action = "SerialKey.Revoked",
            EntityType = "SerialKeyRecord",
            EntityId = rec.Id.ToString(),
            EntityName = rec.SerialKey,
            Status = "Success",
            Details = reason,
        });
        return true;
    }

    // ── Admin listing ───────────────────────────────────────────────────────

    public async Task<List<SerialKeyListItem>> ListAsync(SerialKeyListFilter filter, CancellationToken ct = default)
    {
        var q = _db.Set<SerialKeyRecord>()
            .Include(r => r.Order).Include(r => r.Product).Include(r => r.OrderItem)
            .AsNoTracking().AsQueryable();
        if (filter.Status.HasValue) q = q.Where(r => r.Status == filter.Status.Value);
        if (!string.IsNullOrWhiteSpace(filter.ProviderKey)) q = q.Where(r => r.ProviderKey == filter.ProviderKey);
        if (filter.OrderId.HasValue) q = q.Where(r => r.OrderId == filter.OrderId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var s = filter.Query.Trim().ToLower();
            q = q.Where(r =>
                (r.SerialKey != null && r.SerialKey.ToLower().Contains(s)) ||
                (r.Order != null && r.Order.OrderNumber.ToLower().Contains(s)) ||
                (r.Order != null && r.Order.StudentName.ToLower().Contains(s)));
        }
        var page = Math.Max(1, filter.Page);
        var size = Math.Clamp(filter.PageSize, 10, 200);
        return await q.OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new SerialKeyListItem
            {
                Id = r.Id,
                OrderId = r.OrderId,
                OrderNumber = r.Order != null ? r.Order.OrderNumber : "",
                ProductTitle = r.Product != null ? r.Product.Title : "",
                ModeName = r.OrderItem != null ? r.OrderItem.ModeName : null,
                CustomerName = r.Order != null ? r.Order.StudentName : "",
                ProviderKey = r.ProviderKey,
                SerialKey = r.SerialKey,
                Status = r.Status,
                AttemptCount = r.AttemptCount,
                CreatedAt = r.CreatedAt,
                GeneratedAt = r.GeneratedAt,
                ErrorMessage = r.ErrorMessage,
                ExternalReference = r.ExternalReference,
                ActivatedAt = r.ActivatedAt,
                ExpiresAt = r.ExpiresAt,
                SubscriptionStatus = r.SubscriptionStatus,
                ProviderCourseRef = r.ProviderCourseRef,
            })
            .ToListAsync(ct);
    }

    public async Task<SerialKeyDetail?> GetAsync(Guid recordId, CancellationToken ct = default)
    {
        var rec = await _db.Set<SerialKeyRecord>()
            .Include(r => r.Order).Include(r => r.Product).Include(r => r.OrderItem)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (rec == null) return null;

        var detail = ToDetail(rec);

        // Work out which config scope produced this key, for admin clarity. If the purchased item's mode
        // has its own active override, it was used; otherwise the product-level default was the fallback.
        var modeId = rec.OrderItem?.ProductModeId;
        if (modeId is { } mid)
        {
            var hasModeCfg = await _db.Set<ProductSerialKeyConfig>()
                .AnyAsync(c => c.ProductId == rec.ProductId && c.ProductModeId == mid && c.IsActive, ct);
            detail.ConfigSource = hasModeCfg
                ? $"Mode override ({rec.OrderItem?.ModeName ?? "mode"})"
                : "Product default (mode has no override)";
        }
        else
        {
            detail.ConfigSource = "Product default";
        }
        return detail;
    }

    private static SerialKeyDetail ToDetail(SerialKeyRecord r) => new()
    {
        Id = r.Id,
        OrderId = r.OrderId,
        OrderNumber = r.Order?.OrderNumber ?? "",
        ProductTitle = r.Product?.Title ?? "",
        ModeName = r.OrderItem?.ModeName,
        CustomerName = r.Order?.StudentName ?? "",
        ProviderKey = r.ProviderKey,
        SerialKey = r.SerialKey,
        Status = r.Status,
        AttemptCount = r.AttemptCount,
        CreatedAt = r.CreatedAt,
        GeneratedAt = r.GeneratedAt,
        ErrorMessage = r.ErrorMessage,
        RequestPayload = r.RequestPayload,
        ApiRequestPayload = r.ApiRequestPayload,
        ResponsePayload = r.ResponsePayload,
        ExternalReference = r.ExternalReference,
        TenantRef = r.TenantRef,
        ErrorCode = r.ErrorCode,
        LastAttemptAt = r.LastAttemptAt,
        NextRetryAt = r.NextRetryAt,
        ActivatedAt = r.ActivatedAt,
        ExpiresAt = r.ExpiresAt,
        SubscriptionStatus = r.SubscriptionStatus,
        ProviderCourseRef = r.ProviderCourseRef,
        RevokedAt = r.RevokedAt,
        RevokedReason = r.RevokedReason,
    };

    /// <summary>Deterministic positive Int32 derived from a Guid. Used as the "Owner" int that
    /// RioPlay expects in place of our Guid OrderId. Collisions are improbable enough to be
    /// safe — Rio uses it as a free-text reference per the PDF.</summary>
    private static int StableInt32From(Guid g)
    {
        Span<byte> bytes = stackalloc byte[16];
        g.TryWriteBytes(bytes);
        // XOR-fold to 4 bytes, then mask to positive int.
        int x = 0;
        for (int i = 0; i < 16; i++) x = (x * 31) ^ bytes[i];
        return x & 0x7FFFFFFF;
    }

    /// <summary>Produces a SMALL, stable, positive Int32 to use as Rio's "Owner". Prefers the digits
    /// in the human order number (e.g. "RIO-1049" → 1049). Falls back to a guid-derived hash reduced
    /// into a 1..9,999,999 range so it stays comfortably small (Rio rejected very large Owner values).</summary>
    private static int SmallOwnerFrom(string? orderNumber, Guid orderId)
    {
        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            var digits = new string(orderNumber.Where(char.IsDigit).ToArray());
            if (digits.Length > 0 && int.TryParse(digits.Length > 9 ? digits[^9..] : digits, out var n) && n > 0)
                return n;
        }
        // Fallback: reduce the guid hash into a small positive range.
        return (StableInt32From(orderId) % 9_999_999) + 1;
    }

    // ── Provider catalogue + product config (admin UI surface) ──────────────

    public Task<List<ProviderInfo>> ListProvidersAsync(CancellationToken ct = default)
        => Task.FromResult(_factory.All()
            .Select(p => new ProviderInfo { Key = p.Key, DisplayName = p.DisplayName, SupportsActivate = p.SupportsActivate })
            .ToList());

    public async Task<ProductSerialKeyConfigItem> GetProductConfigAsync(Guid productId, Guid? productModeId = null, CancellationToken ct = default)
    {
        // Explicit null handling for the scope filter: product-level rows have ProductModeId IS NULL,
        // per-mode rows match the given id. Written as two branches so the SQL is unambiguous regardless
        // of EF null-comparison settings.
        var query = _db.Set<ProductSerialKeyConfig>()
            .Where(c => c.ProductId == productId && c.IsActive);
        query = productModeId.HasValue
            ? query.Where(c => c.ProductModeId == productModeId.Value)
            : query.Where(c => c.ProductModeId == null);
        var row = await query.OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

        // No existing row → return an unsaved blank model for this scope. UI binds to it directly;
        // nothing hits the DB until the admin clicks Save.
        if (row == null)
            return new ProductSerialKeyConfigItem { ProductId = productId, ProductModeId = productModeId, IsActive = true, AutoActivate = true };

        var item = new ProductSerialKeyConfigItem
        {
            Id = row.Id,
            ProductId = row.ProductId,
            ProductModeId = row.ProductModeId,
            ProviderKey = row.ProviderKey,
            TenantId = row.TenantId,
            ProviderProductCode = row.ProviderProductCode,
            AutoActivate = row.AutoActivate,
            IsActive = row.IsActive,
        };
        SplatConfigJsonOnto(item, row.ProviderKey, row.ConfigJson, productId);
        return item;
    }

    /// <summary>Lists editable scopes: the product-level default plus one per enabled mode, each flagged
    /// with whether it has its own saved override (and which provider, for a badge).</summary>
    public async Task<List<SerialKeyConfigScope>> ListConfigScopesAsync(Guid productId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ProductSerialKeyConfig>()
            .Where(c => c.ProductId == productId && c.IsActive)
            .Select(c => new { c.ProductModeId, c.ProviderKey })
            .ToListAsync(ct);

        // The product-level config has ProductModeId == null, which cannot be a Dictionary key. Pull it
        // out separately and key the dictionary only by the (non-null) mode ids.
        var productLevel = rows.FirstOrDefault(r => r.ProductModeId == null);
        var byMode = rows
            .Where(r => r.ProductModeId != null)
            .GroupBy(r => r.ProductModeId!.Value)
            .ToDictionary(g => g.Key, g => g.First().ProviderKey);

        var modes = await _db.Set<ProductMode>()
            .Where(m => m.ProductId == productId && m.IsEnabled)
            .OrderBy(m => m.DisplayOrder)
            .Select(m => new { m.Id, m.ModeName })
            .ToListAsync(ct);

        var scopes = new List<SerialKeyConfigScope>
        {
            new()
            {
                ProductModeId = null,
                Label = "Product default",
                HasOwnConfig = productLevel != null,
                ProviderKey = productLevel?.ProviderKey,
            }
        };
        foreach (var m in modes)
        {
            scopes.Add(new SerialKeyConfigScope
            {
                ProductModeId = m.Id,
                Label = m.ModeName,
                HasOwnConfig = byMode.ContainsKey(m.Id),
                ProviderKey = byMode.TryGetValue(m.Id, out var mpk) ? mpk : null,
            });
        }
        return scopes;
    }

    /// <summary>Deserialises a stored ConfigJson blob into the flat DTO fields, for the given provider.</summary>
    private void SplatConfigJsonOnto(ProductSerialKeyConfigItem item, string providerKey, string configJson, Guid productId)
    {
        try
        {
            if (string.Equals(providerKey, "valence", StringComparison.OrdinalIgnoreCase))
            {
                var vcfg = JsonSerializer.Deserialize<Providers.ValenceProductConfig>(configJson)
                           ?? new Providers.ValenceProductConfig();
                item.ValenceBaseUrl = vcfg.BaseUrl;
                item.ValencePathSegment = vcfg.PathSegment;
                item.ValenceClassId = vcfg.ClassId;
                item.ValenceKeyViews = vcfg.KeyViews;
                // Pack id now lives in config; ProviderProductCode holds our product id (the course value).
                item.ValencePackId = vcfg.PackId;
                item.ValencePackIdsCsv = vcfg.PackIdsCsv;   // combo multi-pack CSV (empty on single-pack products)
                return;
            }

            if (string.Equals(providerKey, "superclass", StringComparison.OrdinalIgnoreCase))
            {
                var scfg = JsonSerializer.Deserialize<Providers.SuperclassProductConfig>(configJson)
                           ?? new Providers.SuperclassProductConfig();
                // Mode is implied by the ids, not stored — so an old row loads with no rewrite.
                item.SuperclassMappingMode = DeriveSuperclassMode(scfg);

                // A row saved before the modes existed can hold BOTH ids (Course ID was mandatory,
                // so a combo product had to repeat its id there). Nothing is deleted or rewritten
                // here — both values load exactly as stored — but the admin is told which one will
                // actually be sent, and the duplicate only disappears when they choose to re-save.
                if (scfg.CourseId is > 0 && (scfg.ComboId is > 0 || !string.IsNullOrWhiteSpace(scfg.ComboIdsCsv)))
                {
                    item.SuperclassMappingWarning =
                        $"This product has both a Course ID ({scfg.CourseId}) and a Combo ID — an older shape that is no "
                      + "longer produced. Superclass is sent the COMBO id and no course id. Saving in Combo mode clears "
                      + "the unused Course ID; nothing changes until you save.";
                }

                item.SuperclassCourseId = scfg.CourseId;
                item.SuperclassComboId = scfg.ComboId;
                // Round-trip the multi-id lists so the editor shows what was actually saved rather
                // than just the first id of a combo.
                item.SuperclassCourseIdsCsv = scfg.CourseIdsCsv;
                item.SuperclassComboIdsCsv = scfg.ComboIdsCsv;
                item.SuperclassViewType = scfg.ViewType ?? 1;
                item.SuperclassValue = scfg.Value;
                item.SuperclassValidityType = scfg.ValidityType ?? 1;
                item.SuperclassValidityDays = scfg.ValidityDays;
                item.SuperclassExpiryDate = scfg.ExpiryDate;
                item.SuperclassClassIdOverride = scfg.ClassIdOverride;
                item.SuperclassPurchaseOption = scfg.PurchaseOption;
                return;
            }

            var cfg = JsonSerializer.Deserialize<Providers.RioPlayProductConfig>(configJson)
                      ?? new Providers.RioPlayProductConfig();
            item.EOpens = cfg.EOpens;
            item.EWatchTime = cfg.EWatchTime;
            item.EWatchTimeType = cfg.EWatchTimeType;
            item.EValidity = cfg.EValidity;
            item.EValidityType = cfg.EValidityType;
            item.TenantIdInt = cfg.TenantId;
            item.AccessStatus = cfg.AccessStatus;
            item.TotalAllowedDuration = cfg.TotalAllowedDuration;
            item.AllowedDevices = cfg.AllowedDevices;
            item.WithinDeviceCount = cfg.WithinDeviceCount;
            item.EDeviceLockingType = cfg.EDeviceLockingType;
            item.AllowSecondScreen = cfg.AllowSecondScreen ?? false;
            item.EScreenResolution = cfg.EScreenResolution;
            item.SwitichingDevice = cfg.SwitichingDevice;
            item.SwitchCount = cfg.SwitchCount;
            item.AnalyticsRequired = cfg.AnalyticsRequired ?? false;
            item.IsLiveClassIncluded = cfg.IsLiveClassIncluded ?? false;
            item.IsTrial = cfg.IsTrial ?? false;
            item.WaterMarkRequired = cfg.WaterMarkRequired ?? false;
            item.WaterMarkText = cfg.WaterMarkText;
            item.UpdateFrequencyInDays = cfg.UpdateFrequencyInDays;
            item.ValidTill = cfg.ValidTill;
            item.TemplateSerialKeyId = cfg.TemplateSerialKeyId;
            item.IsRioActive = cfg.IsActive;
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Existing serial-key config has invalid JSON productId={ProductId}", productId);
        }
    }

    public async Task<(bool ok, string? error)> SaveProductConfigAsync(ProductSerialKeyConfigItem model, Guid? actorId, CancellationToken ct = default)
    {
        if (model.ProductId == Guid.Empty) return (false, "ProductId is required.");
        if (string.IsNullOrWhiteSpace(model.ProviderKey)) return (false, "Provider is required.");
        if (!_factory.TryGet(model.ProviderKey, out _))
            return (false, $"Provider '{model.ProviderKey}' is not registered.");

        // A per-mode override must target a mode that belongs to this product.
        if (model.ProductModeId is { } scopeModeId)
        {
            var modeOk = await _db.Set<ProductMode>()
                .AnyAsync(m => m.Id == scopeModeId && m.ProductId == model.ProductId, ct);
            if (!modeOk)
                return (false, "The selected lecture mode does not belong to this product.");
        }

        string configJson;
        string? providerProductCode = model.ProviderProductCode?.Trim();

        // RioPlay generates against Rio's numeric product id, which the admin supplies as the Provider
        // Product Code. It must be present and numeric (Rio's Int32 product id, or a comma-separated list
        // for multi-code products). We must NOT fall back to our product GUID for Rio — Rio would 404 /
        // reject it. Valence is different (it keys by our product id) and is handled in its own branch.
        if (string.Equals(model.ProviderKey, "rioplay", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(providerProductCode))
                return (false, "Provider Product Code is required for RioPlay — enter Rio's numeric product id (e.g. 5813).");
            // Accept one or more comma-separated positive integers; reject anything else (incl. a GUID).
            var codeParts = providerProductCode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (codeParts.Length == 0 || !codeParts.All(p => int.TryParse(p, out var n) && n > 0))
                return (false, "Provider Product Code for RioPlay must be Rio's numeric product id (e.g. 5813), " +
                               "or a comma-separated list of ids (e.g. 5813,5814). Do not enter the product GUID.");
        }

        if (string.Equals(model.ProviderKey, "valence", StringComparison.OrdinalIgnoreCase))
        {
            // Valence keys a course by OUR product id (sent to save_product_pack as course_id, and to
            // register_student_with_course as course). So the course value is the product id, not the
            // pack name. The pack id is held separately in ConfigJson and used only for the mapping call.
            providerProductCode = model.ProductId.ToString();

            // Normalize a multi-pack combo CSV: keep the raw list AND set scalar PackId to the
            // first pack (kept for backward compat with the save_product_pack registration flow,
            // which only maps one pack per product on the Valence side).
            var packIdsCsv = string.IsNullOrWhiteSpace(model.ValencePackIdsCsv) ? null : model.ValencePackIdsCsv!.Trim();
            int? scalarPackId = model.ValencePackId;
            if (!string.IsNullOrWhiteSpace(packIdsCsv))
            {
                var first = packIdsCsv!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var n) ? (int?)n : null)
                    .FirstOrDefault(n => n is > 0);
                if (first is > 0) scalarPackId = first;
            }

            // ── The pack is MANDATORY ────────────────────────────────────────────────────────────
            // A Valence config without a pack cannot tie a key to a subject, and key generation now
            // refuses to run for it (see EnsureValenceMappingAsync). Rejecting it at save time means
            // that state can no longer be created at all, instead of being discovered later by a
            // customer holding a blocked order. Checked AFTER the CSV normalisation so a COMBO that
            // lists its packs only in "Combo: Pack IDs" still satisfies it.
            if (scalarPackId is not > 0)
                return (false, "Valence Pack is required. Configure the correct Valence Pack before saving this product.");

            // Every pack this product will register must be one we actually know, not just the scalar —
            // otherwise a combo's later packs fail at generation time with a vague vendor error.
            foreach (var pid in ValencePackIds(scalarPackId, packIdsCsv))
            {
                if (!await _db.ValencePacks.AnyAsync(p => p.ExternalId == pid, ct))
                    return (false, $"Selected Valence pack (#{pid}) was not found locally. Re-sync packs and try again.");
            }

            var vcfg = new Providers.ValenceProductConfig
            {
                BaseUrl = string.IsNullOrWhiteSpace(model.ValenceBaseUrl) ? null : model.ValenceBaseUrl!.Trim(),
                PathSegment = string.IsNullOrWhiteSpace(model.ValencePathSegment) ? null : model.ValencePathSegment!.Trim(),
                ClassId = string.IsNullOrWhiteSpace(model.ValenceClassId) ? null : model.ValenceClassId!.Trim(),
                KeyViews = model.ValenceKeyViews,
                PackId = scalarPackId,
                PackIdsCsv = packIdsCsv,      // ← preserved verbatim; enqueue fans out on this
            };
            configJson = JsonSerializer.Serialize(vcfg, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        }
        else if (string.Equals(model.ProviderKey, "superclass", StringComparison.OrdinalIgnoreCase))
        {
            // Superclass grants a product through its own numeric course id OR its combo package id —
            // never both. Which one is required, and which one is discarded, follows the mapping mode
            // below; a course id is additionally mirrored into the provider product code. Validity must
            // be internally consistent so the provider doesn't fail every call at dispatch time.

            // ── Multi-course / multi-combo products ─────────────────────────────────────────────
            // /api/register accepts exactly ONE course_id and ONE combo_id per call, so a product
            // covering several Superclass courses needs several registrations. The admin lists the
            // ids here and the enqueue fans them out — the same shape Rio and Valence combos already
            // use. Before this, only a single id could be entered and the rest of a combo silently
            // went un-granted.
            var (courseIds, courseErr) = ParseIdCsv(model.SuperclassCourseIdsCsv, "Course ID");
            if (courseErr != null) return (false, courseErr);
            var (comboIds, comboErr) = ParseIdCsv(model.SuperclassComboIdsCsv, "Combo ID");
            if (comboErr != null) return (false, comboErr);

            // Fanning out on both axes at once has no single meaning — 2 courses × 2 combos could be
            // 2 or 4 registrations, and guessing would silently grant the wrong thing.
            if (courseIds.Count > 1 && comboIds.Count > 1)
                return (false, "List multiple ids in either Course ID or Combo ID, not both — "
                             + "otherwise it is ambiguous how many registrations to send.");

            // ── Mapping mode ────────────────────────────────────────────────────────────────────
            // /api/register grants through a course id OR a combo id, never both. The mode decides
            // which axis is stored, and therefore which one is sent. Callers that predate the mode
            // (and any API client) send null, so it is inferred from the ids they supplied — that
            // keeps every existing caller and every existing saved product working unchanged.
            var mode = model.SuperclassMappingMode ?? DeriveSuperclassMode(new Providers.SuperclassProductConfig
            {
                CourseId = model.SuperclassCourseId,
                ComboId = model.SuperclassComboId,
                // Inferred from the SAME shape that will be stored below — a one-id list included.
                // Deriving from a different shape than the one persisted is what let a saved mode
                // come back as a different mode on reload.
                CourseIdsCsv = courseIds.Count > 0 ? string.Join(",", courseIds) : null,
                ComboIdsCsv = comboIds.Count > 0 ? string.Join(",", comboIds) : null,
            });

            // The scalar stays the first of the list so every existing single-id reader keeps working.
            var courseId = courseIds.Count > 0 ? courseIds[0] : model.SuperclassCourseId;
            var comboId = comboIds.Count > 0 ? comboIds[0] : (model.SuperclassComboId is > 0 ? model.SuperclassComboId : null);

            // Only the chosen axis survives. The admin is never asked to enter the same id twice,
            // and a combo can no longer drag a redundant course_id along with it.
            if (mode == SuperclassMappingMode.ComboPackages)
            {
                if (comboId is not > 0)
                    return (false, "Combo ID is required for a Superclass combo — enter the Superclass numeric combo id, "
                                 + "or list several in Multiple Combo IDs.");
                courseId = null;
                courseIds.Clear();
            }
            else if (mode == SuperclassMappingMode.MultipleCourses)
            {
                // The LIST is what this mode means, so the list is what is checked. Falling back to
                // the scalar here would accept an empty list whenever a course id happened to be
                // left in the form, save a single-course row, and reload as Single Course.
                if (courseIds.Count == 0)
                    return (false, "Course IDs are required — list the Superclass numeric course ids separated by commas (e.g. 2722, 2723).");
                comboId = null;
                comboIds.Clear();
            }
            else
            {
                if (courseId is not > 0)
                    return (false, "Course ID is required for Superclass — enter the Superclass numeric course id (e.g. 2403).");
                comboId = null;
                comboIds.Clear();
                courseIds.Clear();   // a list left over from another mode is not this mode's data
            }

            var viewType = model.SuperclassViewType is 1 or 2 ? model.SuperclassViewType!.Value : 1;
            var validityType = model.SuperclassValidityType is 1 or 2 ? model.SuperclassValidityType!.Value : 1;
            if (model.SuperclassValue is not > 0)
                return (false, "Value (views or minutes) is required for Superclass and must be positive.");
            if (validityType == 1 && model.SuperclassValidityDays is not > 0)
                return (false, "Validity Days is required when Validity Type is 'Days'.");
            if (validityType == 2 && model.SuperclassExpiryDate is null)
                return (false, "Expiry Date is required when Validity Type is 'Fixed Expiry Date'.");

            var scfg = new Providers.SuperclassProductConfig
            {
                CourseId = courseId,
                ComboId = comboId,
                // Written whenever the admin used a list field — a ONE-id list included. Dropping a
                // single-id list is what made "Multiple Courses" come back as "Single Course": the
                // list was the only record that the mode had ever been chosen. Fan-out still needs
                // two ids to split, so a one-id list is one registration exactly as before.
                CourseIdsCsv = courseIds.Count > 0 ? string.Join(",", courseIds) : null,
                ComboIdsCsv = comboIds.Count > 0 ? string.Join(",", comboIds) : null,
                ViewType = viewType,
                Value = model.SuperclassValue,
                ValidityType = validityType,
                ValidityDays = validityType == 1 ? model.SuperclassValidityDays : null,
                ExpiryDate = validityType == 2 ? model.SuperclassExpiryDate : null,
                ClassIdOverride = model.SuperclassClassIdOverride is > 0 ? model.SuperclassClassIdOverride : null,
                PurchaseOption = string.IsNullOrWhiteSpace(model.SuperclassPurchaseOption) ? null : model.SuperclassPurchaseOption!.Trim(),
            };
            // Mirrors the course id, which is also the provider's fallback when ConfigJson carries
            // none. A combo has no course to mirror, so it stays null rather than being given the
            // combo id — that would come back as a course_id through exactly that fallback.
            providerProductCode = courseId?.ToString();
            configJson = JsonSerializer.Serialize(scfg, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        }
        else
        {
        // Compress the flat form fields back into the typed Rio config blob.
        var cfg = new Providers.RioPlayProductConfig
        {
            EOpens = model.EOpens,
            EWatchTime = model.EWatchTime,
            EWatchTimeType = model.EWatchTimeType,
            EValidity = model.EValidity,
            EValidityType = model.EValidityType,
            TenantId = model.TenantIdInt,
            AccessStatus = model.AccessStatus,
            TotalAllowedDuration = model.TotalAllowedDuration,
            AllowedDevices = string.IsNullOrWhiteSpace(model.AllowedDevices) ? "1" : model.AllowedDevices,
            WithinDeviceCount = model.WithinDeviceCount,
            EDeviceLockingType = model.EDeviceLockingType,
            AllowSecondScreen = model.AllowSecondScreen ? true : null,
            EScreenResolution = model.EScreenResolution,
            SwitichingDevice = model.SwitichingDevice,
            SwitchCount = model.SwitchCount,
            AnalyticsRequired = model.AnalyticsRequired ? true : null,
            IsLiveClassIncluded = model.IsLiveClassIncluded ? true : null,
            IsTrial = model.IsTrial ? true : null,
            WaterMarkRequired = model.WaterMarkRequired ? true : null,
            WaterMarkText = model.WaterMarkText,
            UpdateFrequencyInDays = model.UpdateFrequencyInDays,
            ValidTill = model.ValidTill,
            TemplateSerialKeyId = model.TemplateSerialKeyId,
            IsActive = model.IsRioActive ?? 1,
        };
        configJson = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        }

        // ── Upsert: ONE active config per scope, whichever provider it names ────────────────────
        // The scope is (ProductId, ProductModeId) — null mode = the product-level default.
        //
        // This used to match on ProviderKey as well, which meant switching a product from Valence to
        // Superclass did not switch anything: it INSERTED a second active row and left the Valence one
        // in place. Every reader then resolved the scope by "oldest row wins", so the editor reloaded
        // the old provider and key generation kept dispatching to the old vendor — the save looked
        // like it had silently failed. Keying on the scope makes a provider change a change, and
        // deactivating any other row in the same scope repairs products that already carry a duplicate.
        var scopeQuery = _db.Set<ProductSerialKeyConfig>()
            .Where(c => c.ProductId == model.ProductId);
        scopeQuery = model.ProductModeId.HasValue
            ? scopeQuery.Where(c => c.ProductModeId == model.ProductModeId.Value)
            : scopeQuery.Where(c => c.ProductModeId == null);
        var rowsInScope = await scopeQuery.ToListAsync(ct);

        var live = rowsInScope.Where(c => c.IsActive)
            .OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.CreatedAt).ToList();

        var row =
            // Same provider as before → an ordinary update.
            live.FirstOrDefault(c => string.Equals(c.ProviderKey, model.ProviderKey, StringComparison.OrdinalIgnoreCase))
            // Switching provider → reuse the row that is actually live, so the scope keeps one row.
            ?? live.FirstOrDefault()
            // Nothing live: re-enable a previously cleared row for this provider rather than piling up.
            ?? rowsInScope.Where(c => string.Equals(c.ProviderKey, model.ProviderKey, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(c => c.UpdatedAt).FirstOrDefault();

        if (row == null)
        {
            row = new ProductSerialKeyConfig
            {
                Id = Guid.NewGuid(),
                ProductId = model.ProductId,
                ProductModeId = model.ProductModeId,
            };
            _db.Set<ProductSerialKeyConfig>().Add(row);
        }

        row.ProviderKey = model.ProviderKey;
        row.TenantId = model.TenantId;
        row.ProviderProductCode = providerProductCode;
        row.AutoActivate = model.AutoActivate;
        row.IsActive = model.IsActive;
        row.ConfigJson = configJson;

        // A scope has one provider. Anything else still flagged active here is a leftover from the
        // old provider-keyed upsert and would otherwise keep winning the "which config?" race.
        foreach (var stale in rowsInScope)
            if (!ReferenceEquals(stale, row) && stale.IsActive) stale.IsActive = false;

        await _db.SaveChangesAsync(ct);

        // ── Valence: register the product↔pack mapping on Valence's side on EVERY save ──
        // This used to run only when the pack was new or had changed. That assumed our stored pack
        // id proves a mapping exists on Valence — and it does not. If the very first mapping call
        // never landed (Valence down, config seeded straight into the database, a save that predates
        // this code), the pack id looks correct here forever while Valence keeps rejecting key
        // generation with "No pack found for this course" — and re-saving could never repair it,
        // because nothing had changed. The screen even reported a green "Saved".
        //
        // MapProductToPackAsync is idempotent — Valence's "This combination already exists" counts as
        // success — so re-sending it every save costs one call and removes the whole failure mode.
        if (string.Equals(model.ProviderKey, "valence", StringComparison.OrdinalIgnoreCase))
        {
            // EVERY pack this product sells, not just the one in the Pack dropdown. A COMBO lists its
            // packs in "Combo: Pack IDs" and the enqueue fans out one key record per pack — but only
            // the scalar pack was ever registered on Valence, so Valence knew the course had ONE pack.
            // Every record then registered the same student against the same course and Valence handed
            // back the SAME key, which the duplicate guard correctly rejected: the second pack could
            // never produce a key. Registering all of them is the prerequisite for a combo working.
            var packs = ValencePackIds(model.ValencePackId, model.ValencePackIdsCsv);
            if (packs.Count > 0)
            {
                var baseUrl = model.ValenceBaseUrl?.Trim();
                var pathSeg = model.ValencePathSegment?.Trim();
                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(pathSeg))
                    return (false, "Valence Base URL and Path segment are required to map the product to its pack.");

                var productName = await _db.Products
                    .Where(p => p.Id == model.ProductId)
                    .Select(p => p.Title)
                    .FirstOrDefaultAsync(ct) ?? "Course";

                var failures = new List<string>();
                // A combo registers each pack under its OWN course id, so Valence sees two distinct
                // courses and issues a key for each. A single-pack product keeps the bare product id.
                var isCombo = packs.Count >= 2;
                foreach (var pack in packs)
                {
                    var courseId = await ResolveValenceCourseAsync(model.ProductId, pack, isCombo, ct);
                    var (mapped, mapErr) = await _valencePacks.MapProductToPackAsync(
                        baseUrl!, pathSeg!, courseId, productName, pack, ct);
                    if (mapped)
                        _log.LogInformation("Valence pack mapping confirmed course={Course} pack={Pack}", courseId, pack);
                    else
                    {
                        _log.LogError("Valence pack mapping failed product={Product} pack={Pack} err={Err}", model.ProductId, pack, mapErr);
                        failures.Add($"#{pack}: {mapErr}");
                    }
                }

                // Config is saved either way; a mapping that did not land means key generation will
                // fail with "No pack found", so name the packs that failed rather than just "failed".
                if (failures.Count > 0)
                    return (false, $"Config saved, but registering {failures.Count} of {packs.Count} pack(s) on Valence failed — "
                                 + string.Join("; ", failures) + ". Fix and save again.");
            }
        }

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = "admin",
            Module = "Integrations",
            Action = "SerialKey.ProductConfigSaved",
            EntityType = "Product",
            EntityId = model.ProductId.ToString(),
            Status = "Success",
            Details = $"Provider={model.ProviderKey} Active={model.IsActive} AutoActivate={model.AutoActivate}",
        });
        return (true, null);
    }

    public async Task<bool> ClearProductConfigAsync(Guid productId, Guid? actorId, Guid? productModeId = null, CancellationToken ct = default)
    {
        // Scope the clear: null productModeId clears the product-level default; a set value clears just
        // that mode's override. Explicit null branch for unambiguous SQL.
        var clearQuery = _db.Set<ProductSerialKeyConfig>()
            .Where(c => c.ProductId == productId && c.IsActive);
        clearQuery = productModeId.HasValue
            ? clearQuery.Where(c => c.ProductModeId == productModeId.Value)
            : clearQuery.Where(c => c.ProductModeId == null);
        var rows = await clearQuery.ToListAsync(ct);
        if (rows.Count == 0) return false;
        foreach (var r in rows) r.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = "admin",
            Module = "Integrations",
            Action = "SerialKey.ProductConfigCleared",
            EntityType = "Product",
            EntityId = productId.ToString(),
            Status = "Success",
        });
        return true;
    }

    /// <summary>
    /// Every Valence pack a product sells: the scalar Pack plus each id in the COMBO CSV, de-duplicated
    /// and ordered. One list so the save path and the re-register action cannot disagree about which
    /// packs must exist on Valence.
    /// </summary>
    private static List<int> ValencePackIds(int? scalarPackId, string? packIdsCsv)
    {
        var ids = new List<int>();
        if (scalarPackId is { } s && s > 0) ids.Add(s);

        if (!string.IsNullOrWhiteSpace(packIdsCsv))
        {
            foreach (var part in packIdsCsv!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (int.TryParse(part, out var n) && n > 0 && !ids.Contains(n))
                    ids.Add(n);
        }
        return ids;
    }

    /// <summary>
    /// Valence only: (re)assert the course↔pack mapping that the registration about to run depends on,
    /// and refuse the registration outright when the purchased product has no pack.
    ///
    /// <para><b>No pack configured = no serial key.</b> Returns a failure result when the pack is
    /// missing/invalid or when the mapping could not be confirmed; the caller then registers nothing.
    /// Returns <c>null</c> only when the mapping is confirmed, or when the provider itself will report
    /// a more precise reason (non-Valence record, or missing Valence credentials).</para>
    ///
    /// <para>Per-record ConfigJson is always single-pack (the combo fan-out at enqueue splits one
    /// record per pack), so this asserts exactly the one pack this key is for. Pack identity comes from
    /// the purchased OrderItem's own product config — never from the order, never from ClassId (5702 is
    /// shared by every Valence product and identifies nothing), never from a default or the first
    /// combo pack.</para>
    ///
    /// <para><b>Known limitation — combo products.</b> Every record fanned out from a combo sends the
    /// SAME <c>course</c>, and Valence issues one key per (student, course). So the first pack yields a
    /// key and the rest are rejected by the duplicate guard and dead-lettered. That failure is left as
    /// it is, deliberately: it is visible and safe. Multiple pack-specific keys for the same Valence
    /// course require Edubees/Valence API support for pack-specific registration, or separate course
    /// IDs per subject.</para>
    ///
    /// <para><b>Known limitation — no post-hoc verification.</b> Valence's registration response
    /// carries only <c>key</c>, <c>message</c> and <c>status</c> — no pack_id, course_id or student_id —
    /// so the key cannot be re-checked against the expected pack after the fact. The guarantee is
    /// therefore procedural: the exact product's pack is mapped, the mapping is confirmed, and the
    /// registration that consumes it follows immediately with nothing in between.</para>
    /// </summary>
    private async Task<GenerateKeyResult?> EnsureValenceMappingAsync(
        SerialKeyRecord rec, GenerateKeyRequest req, CancellationToken ct)
    {
        if (!string.Equals(rec.ProviderKey, "valence", StringComparison.OrdinalIgnoreCase)) return null;

        Providers.ValenceProductConfig? cfg = null;
        if (!string.IsNullOrWhiteSpace(req.ConfigJson) && req.ConfigJson != "{}")
        {
            try
            {
                cfg = JsonSerializer.Deserialize<Providers.ValenceProductConfig>(
                    req.ConfigJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "Valence config unreadable for mapping guard id={Id}", rec.Id);
            }
        }

        // ── NO PACK CONFIGURED = NO SERIAL KEY ──────────────────────────────────────────────────
        // The pack IS the subject. Registration sends only `course`, so without a pack asserted for
        // that course Valence still registers the student and still returns a working key — for
        // whatever pack it happens to resolve. The customer then holds a key to someone else's
        // subject and nothing anywhere reports an error. Refusing is the only safe answer: a blocked
        // record is recoverable by picking the pack and retrying; a wrong key already emailed is not.
        //
        // This must NOT fall through to the provider — the provider never looks at the pack at all.
        if (cfg?.PackId is not { } packId || packId <= 0)
        {
            _log.LogError("Valence registration BLOCKED id={Id} product={Product} — no pack configured.",
                rec.Id, rec.ProductId);
            return GenerateKeyResult.Fail("no_pack_configured",
                "No Valence pack is configured for this product, so the key could not be tied to the "
              + "purchased subject. Select the pack on the product's serial-key config, then retry.");
        }

        // Credentials missing is a different failure with a different remedy, so it keeps the
        // provider's own wording rather than being reported as a pack problem.
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.PathSegment)) return null;

        // Map exactly the identifier the registration will send as `course` — not simply rec.ProductId.
        // Should the two ever diverge, mapping one while registering the other would silently rebuild
        // the original bug.
        // Exactly the string the registration will send as `course` — for a combo record that is the
        // per-pack course id, not the bare product id.
        var courseId = string.IsNullOrWhiteSpace(req.ProviderProductCode)
            ? rec.ProductId.ToString()
            : req.ProviderProductCode!;

        var (mapped, mapErr) = await _valencePacks.MapProductToPackAsync(
            cfg.BaseUrl!.Trim(), cfg.PathSegment!.Trim(), courseId, req.ProductTitle, packId, ct);

        if (mapped)
        {
            _log.LogInformation("Valence mapping asserted before registration id={Id} course={Course} pack={Pack}",
                rec.Id, courseId, packId);
            return null;
        }

        _log.LogError("Valence mapping NOT confirmed id={Id} course={Course} pack={Pack} err={Err} — registration skipped so no wrong-subject key is issued.",
            rec.Id, courseId, packId, mapErr);
        return GenerateKeyResult.Fail("pack_mapping_unconfirmed",
            $"Valence pack #{packId} could not be registered for this product ({mapErr ?? "rejected"}). "
            + "Registration was skipped to avoid issuing a key for the wrong subject — it will retry.");
    }

    public async Task<(bool ok, string? error)> RemapValencePackAsync(
        Guid productId, Guid? productModeId = null, CancellationToken ct = default)
    {
        // Resolve exactly the config the generator would use for this product+mode: the mode's own
        // row if it has one, otherwise the product-level row. Anything else would re-register a
        // different pack than the one actually in play.
        var q = _db.Set<ProductSerialKeyConfig>()
            .Where(c => c.ProductId == productId && c.IsActive
                     && c.ProviderKey.ToLower() == "valence");
        q = productModeId.HasValue
            ? q.Where(c => c.ProductModeId == productModeId.Value)
            : q.Where(c => c.ProductModeId == null);

        var row = await q.FirstOrDefaultAsync(ct);
        if (row == null) return (false, "No active Valence configuration found for this product.");

        Providers.ValenceProductConfig? cfg = null;
        if (!string.IsNullOrWhiteSpace(row.ConfigJson))
        {
            try { cfg = JsonSerializer.Deserialize<Providers.ValenceProductConfig>(row.ConfigJson); }
            catch (JsonException) { /* fall through to the validation below */ }
        }

        var baseUrl = cfg?.BaseUrl?.Trim();
        var pathSeg = cfg?.PathSegment?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(pathSeg))
            return (false, "The saved Valence config has no Base URL / Path segment. Open the config and save it once.");

        // Same rule as the save path: a COMBO must have EVERY one of its packs registered, or the
        // packs beyond the first can never yield a key.
        var packs = ValencePackIds(cfg?.PackId, cfg?.PackIdsCsv);
        if (packs.Count == 0)
            return (false, "The saved Valence config has no Pack selected.");

        var productName = await _db.Products
            .Where(p => p.Id == productId).Select(p => p.Title)
            .FirstOrDefaultAsync(ct) ?? "Course";

        var failures = new List<string>();
        // Same rule as the save path: a combo maps each pack under its own course id.
        var isCombo = packs.Count >= 2;
        foreach (var pack in packs)
        {
            var courseId = await ResolveValenceCourseAsync(productId, pack, isCombo, ct);
            var (mapped, mapErr) = await _valencePacks.MapProductToPackAsync(
                baseUrl!, pathSeg!, courseId, productName, pack, ct);
            _log.LogInformation("Valence re-register course={Course} pack={Pack} ok={Ok} err={Err}",
                courseId, pack, mapped, mapErr);
            if (!mapped) failures.Add($"#{pack}: {mapErr ?? "rejected"}");
        }

        return failures.Count == 0
            ? (true, null)
            : (false, $"{failures.Count} of {packs.Count} pack(s) failed — " + string.Join("; ", failures));
    }

    public async Task<BulkConfigResult> BulkSaveProductConfigAsync(BulkConfigRequest request, Guid? actorId, CancellationToken ct = default)
    {
        var result = new BulkConfigResult();

        if (string.IsNullOrWhiteSpace(request.ProviderKey) || !_factory.TryGet(request.ProviderKey, out _))
        {
            result.Items.Add(new BulkConfigItemResult { Ok = false, Error = $"Provider '{request.ProviderKey}' is not registered." });
            result.Failed = 1; result.Total = 1;
            return result;
        }

        // De-dupe product rows (keep the last product code entered for a product).
        var rows = request.Products
            .Where(p => p.ProductId != Guid.Empty)
            .GroupBy(p => p.ProductId)
            .Select(g => g.Last())
            .ToList();
        result.Total = rows.Count;
        if (rows.Count == 0) return result;

        // Titles for the result display.
        var ids = rows.Select(r => r.ProductId).ToList();
        var titles = await _db.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title, ct);

        var isValence = string.Equals(request.ProviderKey, "valence", StringComparison.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            // Build a single-product config item from the shared template + this row's product code.
            // Bulk mapping always writes the PRODUCT-LEVEL config (ProductModeId left null); per-mode
            // overrides are set individually on the Product Edit page.
            var item = new ProductSerialKeyConfigItem
            {
                ProductId = row.ProductId,
                ProviderKey = request.ProviderKey,
                ProviderProductCode = row.ProviderProductCode,
                AutoActivate = request.AutoActivate,
                IsActive = request.IsActive,
            };

            if (isValence)
            {
                item.ValenceBaseUrl = request.ValenceBaseUrl;
                item.ValencePathSegment = request.ValencePathSegment;
                item.ValencePackId = request.ValencePackId;
                item.ValenceClassId = request.ValenceClassId;
                item.ValenceKeyViews = request.ValenceKeyViews;
            }
            else
            {
                // RioPlay shared fields.
                item.TenantId = request.TenantId;
                item.EValidity = request.EValidity;
                item.EValidityType = request.EValidityType;
                item.AllowedDevices = string.IsNullOrWhiteSpace(request.AllowedDevices) ? "1" : request.AllowedDevices;
                item.TemplateSerialKeyId = request.TemplateSerialKeyId;
            }

            var itemResult = new BulkConfigItemResult
            {
                ProductId = row.ProductId,
                ProductTitle = titles.TryGetValue(row.ProductId, out var t) ? t : row.ProductId.ToString(),
            };

            try
            {
                var (ok, error) = await SaveProductConfigAsync(item, actorId, ct);
                itemResult.Ok = ok;
                itemResult.Error = error;
            }
            catch (Exception ex)
            {
                itemResult.Ok = false;
                itemResult.Error = ex.Message;
            }

            if (itemResult.Ok) result.Succeeded++; else result.Failed++;
            result.Items.Add(itemResult);
        }

        await _audit.WriteAsync(new AuditEntry
        {
            ActorUserId = actorId,
            ActorName = "admin",
            Module = "Integrations",
            Action = "SerialKey.BulkProductConfig",
            EntityType = "Product",
            Status = result.Failed == 0 ? "Success" : "Partial",
            Details = $"Provider {request.ProviderKey}: {result.Succeeded} ok / {result.Failed} failed of {result.Total}.",
        });

        return result;
    }

    public async Task<ErpMappingImportResult> ImportErpProviderMappingsAsync(ErpMappingImportRequest request, Guid? actorId, CancellationToken ct = default)
    {
        var result = new ErpMappingImportResult { DryRun = request.DryRun };

        if (string.IsNullOrWhiteSpace(request.SourceConnectionString))
        { result.Ok = false; result.Error = "ERP SQL Server connection string is required."; return result; }

        // Valence needs the static connection values present in each product's config (the provider
        // reads them from config, not from any central store). Require them up front.
        var hasValenceStatics = !string.IsNullOrWhiteSpace(request.ValenceBaseUrl)
                                && !string.IsNullOrWhiteSpace(request.ValencePathSegment);

        // New products by SKU (case-insensitive) → product Guid.
        var newBySku = await _db.Products
            .Where(p => p.Sku != null && p.Sku != "")
            .ToDictionaryAsync(p => p.Sku!.ToLower(), p => p.Id, ct);

        // Default Rio tenant Guid (Rio products all use the default tenant).
        var defaultTenantId = await _db.Set<RioPlayTenant>()
            .Where(t => t.IsDefault)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        // Locally-known Valence pack ids (already fetched), to validate PackId before we try to save.
        var knownPackIds = (await _db.ValencePacks.Select(p => p.ExternalId).ToListAsync(ct)).ToHashSet();

        // ── Read the ERP mapping ────────────────────────────────────────────
        var rows = new List<(string Sku, string? RioCode, short? EValidity, short? EValidityType,
                             string? AllowedDevices, int? TemplateId, int? PackId, short? EOpens, short? EWatchTime)>();
        try
        {
            await using var sql = new SqlConnection(request.SourceConnectionString);
            await sql.OpenAsync(ct);
            const string q = @"
SELECT p.[SKUCode], p.[RioplayProductCode], p.[eValidity], p.[eValidityType],
       p.[AllowedDevices], p.[TemplateSerialKeyId], p.[PackId], p.[eOpens], p.[eWatchTime]
FROM dbo.Products p
WHERE p.[SKUCode] IS NOT NULL AND LTRIM(RTRIM(p.[SKUCode])) <> '';";
            await using var cmd = new SqlCommand(q, sql);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                rows.Add((
                    rd.GetString(0).Trim(),
                    rd.IsDBNull(1) ? null : rd.GetString(1)?.Trim(),
                    rd.IsDBNull(2) ? (short?)null : rd.GetInt16(2),
                    rd.IsDBNull(3) ? (short?)null : rd.GetInt16(3),
                    rd.IsDBNull(4) ? null : rd.GetString(4)?.Trim(),
                    rd.IsDBNull(5) ? (int?)null : rd.GetInt32(5),
                    rd.IsDBNull(6) ? (int?)null : rd.GetInt32(6),
                    rd.IsDBNull(7) ? (short?)null : rd.GetInt16(7),
                    rd.IsDBNull(8) ? (short?)null : rd.GetInt16(8)));
            }
        }
        catch (Exception ex)
        {
            result.Ok = false;
            result.Error = "Could not read ERP: " + ex.Message;
            return result;
        }

        // De-dupe by SKU (keep last).
        var deduped = rows.GroupBy(r => r.Sku.ToLowerInvariant()).Select(g => g.Last()).ToList();
        result.Read = deduped.Count;

        foreach (var row in deduped)
        {
            ct.ThrowIfCancellationRequested();
            var isValence = row.PackId is { } pk && pk > 0;
            var item = new ErpMappingItemResult { Sku = row.Sku, Provider = isValence ? "valence" : "rioplay" };

            // Match SKU → new product.
            if (!newBySku.TryGetValue(row.Sku.ToLowerInvariant(), out var newProductId))
            { item.Outcome = "skipped"; item.Message = "No new product with this SKU."; result.Skipped++; result.Items.Add(item); continue; }

            var model = new ProductSerialKeyConfigItem
            {
                ProductId = newProductId,
                ProviderKey = isValence ? "valence" : "rioplay",
                AutoActivate = true,
                IsActive = true,
            };

            if (isValence)
            {
                if (!hasValenceStatics)
                { item.Outcome = "error"; item.Message = "Valence Base URL / Path segment not provided."; result.Errors++; result.Items.Add(item); continue; }
                if (!knownPackIds.Contains(row.PackId!.Value))
                { item.Outcome = "skipped"; item.Message = $"Valence pack #{row.PackId} not found locally (sync packs first)."; result.Skipped++; result.Items.Add(item); continue; }

                model.ValencePackId = row.PackId;
                model.ValenceBaseUrl = request.ValenceBaseUrl;
                model.ValencePathSegment = request.ValencePathSegment;
                model.ValenceClassId = string.IsNullOrWhiteSpace(request.ValenceClassId) ? null : request.ValenceClassId!.Trim();
                // Views is per-product: eWatchTime, else eOpens, else 1.
                model.ValenceKeyViews = (int?)(row.EWatchTime ?? row.EOpens) ?? 1;
            }
            else
            {
                model.ProviderProductCode = string.IsNullOrWhiteSpace(row.RioCode) ? null : row.RioCode;
                model.EValidity = row.EValidity;
                model.EValidityType = row.EValidityType;
                model.EOpens = row.EOpens;
                model.EWatchTime = row.EWatchTime;
                model.AllowedDevices = string.IsNullOrWhiteSpace(row.AllowedDevices) ? "1" : row.AllowedDevices!;
                model.TemplateSerialKeyId = row.TemplateId;
                model.TenantId = defaultTenantId;   // Rio: default tenant from settings
            }

            if (request.DryRun)
            {
                item.Outcome = "created"; // would create/update
                if (isValence) result.ValenceMapped++; else result.RioMapped++;
                result.Items.Add(item);
                continue;
            }

            try
            {
                var (ok, error) = await SaveProductConfigAsync(model, actorId, ct);
                if (ok)
                {
                    item.Outcome = "created";
                    if (isValence) result.ValenceMapped++; else result.RioMapped++;
                }
                else { item.Outcome = "error"; item.Message = error; result.Errors++; }
            }
            catch (Exception ex) { item.Outcome = "error"; item.Message = ex.Message; result.Errors++; }

            result.Items.Add(item);
        }

        if (!request.DryRun)
        {
            await _audit.WriteAsync(new AuditEntry
            {
                ActorUserId = actorId,
                ActorName = "admin",
                Module = "Integrations",
                Action = "SerialKey.ErpMappingImport",
                EntityType = "Product",
                Status = result.Errors == 0 ? "Success" : "Partial",
                Details = $"Rio={result.RioMapped}, Valence={result.ValenceMapped}, Skipped={result.Skipped}, Errors={result.Errors} of {result.Read}.",
            });
        }

        result.Ok = true;
        return result;
    }
}
