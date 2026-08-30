namespace RioCommerce.Core.Entities;

/// <summary>
/// One RioPlay tenancy. A single Rio environment can host multiple tenants — different
/// products can route to different tenants, so this is a many-to-one relationship from
/// <see cref="ProductSerialKeyConfig"/> down to here.
///
/// Secret is encrypted at rest via the ASP.NET Core Data Protection key ring (same
/// mechanism used by SMTP / payment gateway secrets in <c>AppSetting</c>). The protector
/// in <c>RioPlayTenantService</c> handles encrypt/decrypt — never read <see cref="SecretEncrypted"/> directly.
/// </summary>
public class RioPlayTenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Rio's numeric TenantId, sent as the <c>tenantId</c> request header on every call.
    /// This is the value Antargyan provides as part of the credentials pair (TenantId + Secret).</summary>
    public int TenantId { get; set; }

    /// <summary>Data-Protection-encrypted ciphertext. Use <c>IRioPlayTenantService.GetSecretAsync</c> to read.</summary>
    public string SecretEncrypted { get; set; } = string.Empty;

    /// <summary>Base URL of the Rio API for this tenant, e.g. <c>https://rio24.azurewebsites.net</c> — no trailing slash.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Exactly one row across all tenants gets this — products without an explicit tenant fall back here.</summary>
    public bool IsDefault { get; set; }
}
