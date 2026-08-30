using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using RioCommerce.Infrastructure.Services.SerialKeys.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.SerialKeys;

/// <summary>
/// Owns the Data-Protection encrypt/decrypt for the per-tenant Rio secret. The same protector
/// purpose string is used here as for SMTP/payment credentials, so existing key-ring rotation
/// covers this storage too.
/// </summary>
public sealed class RioPlayTenantService : IRioPlayTenantService
{
    private const string ProtectorPurpose = "RioCommerce.SerialKey.RioPlayTenant.Secret.v1";

    private readonly RioCommerceDbContext _db;
    private readonly IDataProtector _protector;
    private readonly RioPlayApiClient _api;

    public RioPlayTenantService(RioCommerceDbContext db, IDataProtectionProvider dp, RioPlayApiClient api)
    {
        _db = db;
        _protector = dp.CreateProtector(ProtectorPurpose);
        _api = api;
    }

    public async Task<List<RioPlayTenantItem>> ListAsync(CancellationToken ct = default)
        => await _db.Set<RioPlayTenant>()
            .OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .Select(t => new RioPlayTenantItem
            {
                Id = t.Id,
                Name = t.Name,
                TenantId = t.TenantId,
                BaseUrl = t.BaseUrl,
                IsActive = t.IsActive,
                IsDefault = t.IsDefault,
                HasSecret = !string.IsNullOrEmpty(t.SecretEncrypted),
            })
            .ToListAsync(ct);

    public async Task<RioPlayTenantEdit?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<RioPlayTenant>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row == null) return null;
        return new RioPlayTenantEdit
        {
            Id = row.Id,
            Name = row.Name,
            TenantId = row.TenantId,
            BaseUrl = row.BaseUrl,
            IsActive = row.IsActive,
            IsDefault = row.IsDefault,
            // Secret is intentionally not returned — admin re-enters to rotate.
        };
    }

    public async Task<(bool ok, string? error, Guid id)> SaveAsync(RioPlayTenantEdit model, CancellationToken ct = default)
    {
        if (model is null) return (false, "Model is null.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(model.Name)) return (false, "Name is required.", Guid.Empty);
        if (model.TenantId <= 0) return (false, "Tenant ID (numeric) is required.", Guid.Empty);
        if (string.IsNullOrWhiteSpace(model.BaseUrl)) return (false, "BaseUrl is required.", Guid.Empty);

        try
        {
            RioPlayTenant row;
            if (model.Id is Guid id && id != Guid.Empty)
            {
                var existing = await _db.Set<RioPlayTenant>().FirstOrDefaultAsync(t => t.Id == id, ct);
                if (existing == null) return (false, "Tenant not found.", Guid.Empty);
                row = existing;
            }
            else
            {
                row = new RioPlayTenant { Id = Guid.NewGuid() };
                _db.Set<RioPlayTenant>().Add(row);
                // First tenant ever created becomes the default automatically so a product with no
                // explicit tenant still resolves credentials. Query goes against committed state
                // (the Add above isn't yet flushed) — empty DB ⇒ AnyAsync returns false.
                if (!await _db.Set<RioPlayTenant>().AnyAsync(ct)) model.IsDefault = true;
            }

            row.Name = model.Name.Trim();
            row.TenantId = model.TenantId;
            row.BaseUrl = model.BaseUrl.Trim().TrimEnd('/');
            row.IsActive = model.IsActive;

            // Only update the secret when the admin actually entered a new value (blank on edit = keep).
            // CRITICAL: Trim() the secret first — copy/paste from email or chat often grabs trailing
            // whitespace or newlines, which silently corrupt the HTTP header value and produce
            // exactly the kind of 404 / 401 we'd otherwise spend hours chasing.
            if (!string.IsNullOrWhiteSpace(model.Secret))
            {
                var cleaned = model.Secret.Trim();
                try { row.SecretEncrypted = _protector.Protect(cleaned); }
                catch (Exception ex)
                {
                    return (false, $"Secret encryption failed: {ex.Message}", Guid.Empty);
                }
            }
            if (string.IsNullOrEmpty(row.SecretEncrypted))
                return (false, "Secret is required for new tenants.", Guid.Empty);

            // Exactly one default. If this row is being made the default, clear the flag on every other row
            // FIRST and save that change before stamping IsDefault on this row — otherwise the partial unique
            // index ("IsDefault = TRUE") sees a brief window with two rows flagged default and rejects the
            // transaction with an index-violation error.
            if (model.IsDefault)
            {
                var others = await _db.Set<RioPlayTenant>().Where(t => t.Id != row.Id && t.IsDefault).ToListAsync(ct);
                if (others.Count > 0)
                {
                    foreach (var o in others) o.IsDefault = false;
                    await _db.SaveChangesAsync(ct);
                }
                row.IsDefault = true;
            }
            else
            {
                row.IsDefault = false;
            }

            await _db.SaveChangesAsync(ct);
            return (true, null, row.Id);
        }
        catch (DbUpdateException ex)
        {
            // Bubble the most actionable text up — the inner Npgsql message is what tells the admin
            // "unique violation on rioplay_tenants_IsDefault" etc. Without this the Razor circuit
            // crashes via SignalR with a useless NRE in the framework's dispatcher.
            var inner = ex.InnerException?.Message ?? ex.Message;
            return (false, $"Database save failed: {inner}", Guid.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}", Guid.Empty);
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<RioPlayTenant>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row == null) return false;
        _db.Set<RioPlayTenant>().Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<RioPlayTenantCredentials?> ResolveCredentialsAsync(Guid? tenantId, CancellationToken ct = default)
    {
        RioPlayTenant? row;
        if (tenantId is Guid id && id != Guid.Empty)
        {
            row = await _db.Set<RioPlayTenant>().FirstOrDefaultAsync(t => t.Id == id && t.IsActive, ct);
        }
        else
        {
            // Default fallback. If no row is flagged IsDefault, take the first active one — better than failing hard.
            row = await _db.Set<RioPlayTenant>()
                .Where(t => t.IsActive)
                .OrderByDescending(t => t.IsDefault).ThenBy(t => t.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }
        if (row == null || string.IsNullOrEmpty(row.SecretEncrypted)) return null;

        string secret;
        try { secret = _protector.Unprotect(row.SecretEncrypted); }
        catch { return null; }   // Key-ring rotation gone wrong; admin must re-enter the secret.

        return new RioPlayTenantCredentials(row.TenantId, secret, row.BaseUrl);
    }

    public async Task<RioConnectionTestResult> TestConnectionAsync(Guid tenantId, CancellationToken ct = default)
    {
        var creds = await ResolveCredentialsAsync(tenantId, ct);
        if (creds == null)
            return new RioConnectionTestResult { Success = false, Error = "Tenant not found, inactive, or no secret saved." };

        var d = await _api.TestConnectionAsync(creds.BaseUrl, creds.TenantId, creds.Secret, ct);
        return new RioConnectionTestResult
        {
            Success = d.Success,
            StatusCode = d.StatusCode,
            RequestUrl = d.FinalUrl ?? d.RequestUrl,
            RequestHeaders = d.RequestHeaders,
            HttpVersion = d.HttpVersion,
            ServerHeader = d.ServerHeader,
            ResponseHeaders = d.ResponseHeaders,
            ResponseBody = d.ResponseBody,
            Error = d.Exception,
        };
    }

    public async Task<RioChainTestSummary> TestFullChainAsync(Guid tenantId, Guid? productConfigId, CancellationToken ct = default)
    {
        var creds = await ResolveCredentialsAsync(tenantId, ct);
        if (creds == null)
            return new RioChainTestSummary { AllPassed = false, Error = "Tenant not found, inactive, or no secret saved." };

        // Pull real product config. If an explicit id was given, use it. Otherwise auto-pick the
        // first active RioPlay product config so step 3 has a real Product code to test with.
        int productCode = 0;
        var cfg = new Providers.RioPlayProductConfig();
        RioCommerce.Core.Entities.ProductSerialKeyConfig? pc = null;
        if (productConfigId is Guid pcid && pcid != Guid.Empty)
        {
            pc = await _db.Set<RioCommerce.Core.Entities.ProductSerialKeyConfig>()
                .FirstOrDefaultAsync(c => c.Id == pcid, ct);
        }
        else
        {
            // No explicit config — grab any active RioPlay config with a numeric product code so
            // the create step is actually exercised end-to-end.
            pc = await _db.Set<RioCommerce.Core.Entities.ProductSerialKeyConfig>()
                .Where(c => c.IsActive && c.ProviderKey == "rioplay" && c.ProviderProductCode != null)
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }
        if (pc != null)
        {
            int.TryParse(pc.ProviderProductCode, out productCode);
            if (!string.IsNullOrWhiteSpace(pc.ConfigJson))
            {
                try { cfg = System.Text.Json.JsonSerializer.Deserialize<Providers.RioPlayProductConfig>(pc.ConfigJson) ?? cfg; }
                catch { /* use defaults */ }
            }
        }
        // Ensure the chain test has a TenantId in the entity (mandatory field). Fall back to the
        // tenant's numeric id if the product config didn't carry one.
        cfg.TenantId ??= creds.TenantId;

        var chain = await _api.RunCreateOnlyTestAsync(creds.BaseUrl, creds.TenantId, creds.Secret, productCode, cfg, ct);

        return new RioChainTestSummary
        {
            AllPassed = chain.Create?.Success ?? false,
            Steps = new List<RioChainStep>
            {
                Step(chain.Create),
            },
        };

        static RioChainStep Step(Providers.RioStepResult? s) => new()
        {
            Step = s?.Step ?? "(missing)",
            Success = s?.Success ?? false,
            Detail = s?.Detail,
            Raw = s?.Raw,
        };
    }
}
