using RioCommerce.Core.DTOs.SerialKeys;
using RioCommerce.Core.Entities;
using RioCommerce.Core.Interfaces;
using RioCommerce.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace RioCommerce.Infrastructure.Services.SerialKeys;

/// <summary>
/// Owns the singleton <see cref="SuperclassSettings"/> row and the Data-Protection encrypt/decrypt of
/// the Superclass API key. Same protector mechanism as <see cref="RioPlayTenantService"/> (SMTP /
/// payment secrets), so key-ring rotation covers this too. The plaintext key never leaves
/// <see cref="ResolveAsync"/> and is never returned to the UI.
/// </summary>
public sealed class SuperclassSettingsService : ISuperclassSettingsService
{
    private const string ProtectorPurpose = "RioCommerce.SerialKey.Superclass.ApiKey.v1";

    private readonly RioCommerceDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpFactory;

    public SuperclassSettingsService(RioCommerceDbContext db, IDataProtectionProvider dp, IHttpClientFactory httpFactory)
    {
        _db = db;
        _protector = dp.CreateProtector(ProtectorPurpose);
        _httpFactory = httpFactory;
    }

    public async Task<SuperclassSettingsEdit> GetAsync(CancellationToken ct = default)
    {
        var row = await CurrentAsync(ct);
        if (row == null)
            return new SuperclassSettingsEdit();   // blank model with defaults (Base URL, Class ID 318)

        return new SuperclassSettingsEdit
        {
            ApiBaseUrl = row.ApiBaseUrl,
            HasApiKey = !string.IsNullOrEmpty(row.ApiKeyEncrypted),
            DefaultClassId = row.DefaultClassId,
            Environment = row.Environment,
            LoggingEnabled = row.LoggingEnabled,
            MaxRetryAttempts = row.MaxRetryAttempts,
            IsActive = row.IsActive,
            // ApiKey intentionally not returned — admin re-enters to rotate.
        };
    }

    public async Task<(bool ok, string? error)> SaveAsync(SuperclassSettingsEdit model, CancellationToken ct = default)
    {
        if (model is null) return (false, "Model is null.");
        if (string.IsNullOrWhiteSpace(model.ApiBaseUrl)) return (false, "API Base URL is required.");
        if (!Uri.TryCreate(model.ApiBaseUrl.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
            return (false, "API Base URL must be an absolute http(s) URL.");
        if (model.DefaultClassId <= 0) return (false, "Class ID must be a positive number.");
        if (model.MaxRetryAttempts is < 1 or > 20) return (false, "Max retry attempts must be between 1 and 20.");

        try
        {
            var row = await CurrentAsync(ct);
            var isNew = row == null;
            if (row == null)
            {
                row = new SuperclassSettings { Id = Guid.NewGuid() };
                _db.Set<SuperclassSettings>().Add(row);
            }

            row.ApiBaseUrl = model.ApiBaseUrl.Trim().TrimEnd('/');
            row.DefaultClassId = model.DefaultClassId;
            row.Environment = string.IsNullOrWhiteSpace(model.Environment) ? "Production" : model.Environment.Trim();
            row.LoggingEnabled = model.LoggingEnabled;
            row.MaxRetryAttempts = model.MaxRetryAttempts;
            row.IsActive = model.IsActive;

            // Only rotate the key when a new value is entered (blank on edit = keep). Trim first —
            // copy/paste from email/chat often grabs trailing whitespace that corrupts the header.
            if (!string.IsNullOrWhiteSpace(model.ApiKey))
            {
                try { row.ApiKeyEncrypted = _protector.Protect(model.ApiKey.Trim()); }
                catch (Exception ex) { return (false, $"API key encryption failed: {ex.Message}"); }
            }
            if (string.IsNullOrEmpty(row.ApiKeyEncrypted))
                return (false, "API authentication key is required.");

            await _db.SaveChangesAsync(ct);
            return (true, null);
        }
        catch (DbUpdateException ex)
        {
            return (false, $"Database save failed: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<SuperclassCredentials?> ResolveAsync(CancellationToken ct = default)
    {
        var row = await _db.Set<SuperclassSettings>()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (row == null || string.IsNullOrEmpty(row.ApiKeyEncrypted)) return null;

        string key;
        try { key = _protector.Unprotect(row.ApiKeyEncrypted); }
        catch { return null; }   // key-ring rotation gone wrong — admin must re-enter the key.

        return new SuperclassCredentials(
            row.ApiBaseUrl, key, row.DefaultClassId, row.Environment, row.LoggingEnabled, row.MaxRetryAttempts);
    }

    public async Task<SuperclassConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var creds = await ResolveAsync(ct);
        if (creds == null)
            return new SuperclassConnectionTestResult { Success = false, Error = "No active settings or API key saved." };

        // Reachability check only — a GET against the base URL. We deliberately do NOT hit /api/register
        // here because that would create a real student registration. This confirms DNS/TLS/host reachability;
        // it does not validate the API key (which the register endpoint would).
        try
        {
            var client = _httpFactory.CreateClient("superclass");
            using var resp = await client.GetAsync(creds.ApiBaseUrl, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new SuperclassConnectionTestResult
            {
                Success = true,   // any HTTP reply means the host is reachable
                StatusCode = (int)resp.StatusCode,
                ResponseBody = body.Length > 1000 ? body[..1000] + "…" : body,
            };
        }
        catch (Exception ex)
        {
            return new SuperclassConnectionTestResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>The current settings row: the active one if present, else the most recently touched.</summary>
    private async Task<SuperclassSettings?> CurrentAsync(CancellationToken ct)
        => await _db.Set<SuperclassSettings>()
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);
}
